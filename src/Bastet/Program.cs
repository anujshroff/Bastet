using Bastet.Data;
using Bastet.Filters;
using Bastet.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.SetMinimumLevel(Enum.TryParse(Environment.GetEnvironmentVariable("BASTET_LOG_LEVEL_DEFAULT") ?? "Warning", true, out LogLevel level) ? level : LogLevel.Warning);
    builder.Logging.AddFilter("Microsoft.AspNetCore", Enum.TryParse(Environment.GetEnvironmentVariable("BASTET_LOG_LEVEL_ASPNETCORE") ?? "Warning", true, out LogLevel aspNetLevel) ? aspNetLevel : LogLevel.Warning);
    builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", Enum.TryParse(Environment.GetEnvironmentVariable("BASTET_LOG_LEVEL_ENTITYFRAMEWORK") ?? "Warning", true, out LogLevel efLevel) ? efLevel : LogLevel.Warning);
}

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add MVC with global sanitization filter
builder.Services.AddControllersWithViews(options => options.Filters.Add<GlobalSanitizationFilter>());

// Allow antiforgery tokens to be sent via the "RequestVerificationToken" header in addition to
// the default form field. Required by AJAX endpoints that POST application/json (e.g. the
// Bulk Azure Import preview/commit endpoints).
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");


// Get connection string (used by both DbContexts)
string? connectionString = Environment.GetEnvironmentVariable("BASTET_CONNECTION_STRING")
    ?? (builder.Environment.IsDevelopment()
        ? builder.Configuration.GetConnectionString("DefaultConnection")
        : throw new InvalidOperationException("Production environment requires BASTET_CONNECTION_STRING environment variable to be set."));

// Add DbContext
builder.Services.AddDbContext<BastetDbContext>(options =>
{
    options.UseSqlServer(connectionString);
});

// Add DataProtection DbContext for storing Data Protection keys
// This enables multi-replica deployments without session affinity
builder.Services.AddDbContext<DataProtectionDbContext>(options =>
{
    options.UseSqlServer(connectionString);
});

// Determine if we should use database for Data Protection keys
// If auto-migrate is enabled, assume the table will exist after migration runs
// Otherwise, check if the table currently exists
bool autoMigrate = bool.TryParse(Environment.GetEnvironmentVariable("BASTET_AUTO_MIGRATE"), out bool autoMigrateResult) && autoMigrateResult;
bool dataProtectionTableExists = false;

if (autoMigrate)
{
    // Auto-migrate will create the table, so we can use database persistence
    dataProtectionTableExists = true;
}
else
{
    // Check if table exists in database
    try
    {
        using SqlConnection connection = new(connectionString);
        connection.Open();
        using SqlCommand command = new(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DataProtectionKeys') THEN 1 ELSE 0 END",
            connection);
        dataProtectionTableExists = (int)command.ExecuteScalar() == 1;
    }
    catch
    {
        // Connection failed or query failed - assume table doesn't exist
        dataProtectionTableExists = false;
    }
}

// Configure Data Protection
// If table exists (or will exist via auto-migrate), use database for key storage (enables multi-replica without session affinity)
// If table doesn't exist, use default ephemeral keys (works for single replica only)
if (dataProtectionTableExists)
{
    builder.Services.AddDataProtection()
        .SetApplicationName("Bastet")
        .PersistKeysToDbContext<DataProtectionDbContext>();
}
else
{
    builder.Services.AddDataProtection()
        .SetApplicationName("Bastet");
}

// Register services
builder.Services.AddScoped<IIpUtilityService, IpUtilityService>();
builder.Services.AddScoped<Bastet.Services.Validation.ISubnetValidationService, Bastet.Services.Validation.SubnetValidationService>();
builder.Services.AddScoped<Bastet.Services.Validation.IHostIpValidationService, Bastet.Services.Validation.HostIpValidationService>();
builder.Services.AddScoped<Bastet.Services.Division.ISubnetDivisionService, Bastet.Services.Division.SubnetDivisionService>();
builder.Services.AddSingleton<Bastet.Services.Azure.AzureArmClientProvider>();
builder.Services.AddScoped<Bastet.Services.Azure.IAzureService, Bastet.Services.Azure.AzureService>();
builder.Services.AddScoped<Bastet.Services.Azure.IAzureBulkImportPlanner, Bastet.Services.Azure.AzureBulkImportPlanner>();
builder.Services.AddScoped<Bastet.Services.Azure.IAzureSubnetSnapshotService, Bastet.Services.Azure.AzureSubnetSnapshotService>();
builder.Services.AddScoped<Bastet.Services.Azure.IAzureReconciler, Bastet.Services.Azure.AzureReconciler>();

builder.Services.AddScoped<Bastet.Services.Security.IInputSanitizationService, Bastet.Services.Security.InputSanitizationService>();
builder.Services.AddSingleton<IVersionService, VersionService>();

// Register subnet locking service with auto-detection based on database provider
builder.Services.AddScoped<Bastet.Services.Locking.ISubnetLockingService>(provider =>
{
    BastetDbContext context = provider.GetRequiredService<BastetDbContext>();

    return context.Database.ProviderName?.ToLower() switch
    {
        "microsoft.entityframeworkcore.sqlite" => new Bastet.Services.Locking.SqliteSubnetLockingService(),
        "microsoft.entityframeworkcore.sqlserver" => new Bastet.Services.Locking.SqlServerSubnetLockingService(context),
        _ => new Bastet.Services.Locking.SqlServerSubnetLockingService(context) // Default to SQL Server
    };
});

// Add HttpContextAccessor for accessing the current user
builder.Services.AddHttpContextAccessor();

// Register UserContextService
builder.Services.AddScoped<IUserContextService, UserContextService>();

// CORS is opt-in. Bastet renders its own UI and all of its AJAX is same-origin, so no cross-origin
// access is needed by default. Set BASTET_CORS_ORIGINS to a comma-separated list of origins to
// allow specific callers. Credentials are deliberately not allowed: the antiforgery token is
// accepted via the RequestVerificationToken header, so permitting cross-origin credentialed
// requests would open up CSRF.
string[] corsOrigins = (Environment.GetEnvironmentVariable("BASTET_CORS_ORIGINS") ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

if (corsOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(corsOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()));
}

// Configure authentication based on environment
if (builder.Environment.IsDevelopment())
{
    // Development authentication (always succeeds with all roles)
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = "DevAuthScheme";
        options.DefaultChallengeScheme = "DevAuthScheme";
    })
    .AddScheme<DevAuthOptions, DevAuthHandler>("DevAuthScheme", options => options.AccessDeniedPath = "/Account/AccessDenied");
}
else
{
    // Production authentication with OIDC
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(1);
        options.SlidingExpiration = true;
    })
    .AddOpenIdConnect(options =>
     {
         options.ClientId = Environment.GetEnvironmentVariable("BASTET_OIDC_CLIENT_ID") ?? "mvc_client";
         options.Authority = Environment.GetEnvironmentVariable("BASTET_OIDC_AUTHORITY") ?? "https://localhost";
         options.ClientSecret = Environment.GetEnvironmentVariable("BASTET_OIDC_CLIENT_SECRET") ?? null;
         options.CallbackPath = "/signin-oidc";
         options.SignedOutCallbackPath = "/signout-callback-oidc";
         options.ResponseType = Environment.GetEnvironmentVariable("BASTET_OIDC_RESPONSE_TYPE") ?? "code";
         options.UsePkce = true;
         options.SaveTokens = true;
         options.UseTokenLifetime = true;
         options.GetClaimsFromUserInfoEndpoint = true;
         options.Scope.Add("openid");
         options.Scope.Add("profile");
         options.Scope.Add("email");
         options.Scope.Add("roles");
         options.Scope.Add("offline_access");
     });
}

builder.Services.AddAuthorizationBuilder()
    // Anything without an explicit [Authorize] or [AllowAnonymous] requires an authenticated user,
    // so a new action that forgets its attribute fails closed rather than being served anonymously.
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy("RequireViewRole", policy =>
        policy.RequireRole(Bastet.Models.ApplicationRoles.View, Bastet.Models.ApplicationRoles.Edit, Bastet.Models.ApplicationRoles.Delete, Bastet.Models.ApplicationRoles.Admin))
    .AddPolicy("RequireEditRole", policy =>
        policy.RequireRole(Bastet.Models.ApplicationRoles.Edit, Bastet.Models.ApplicationRoles.Delete, Bastet.Models.ApplicationRoles.Admin))
    .AddPolicy("RequireDeleteRole", policy =>
        policy.RequireRole(Bastet.Models.ApplicationRoles.Delete, Bastet.Models.ApplicationRoles.Admin))
    .AddPolicy("RequireAdminRole", policy =>
        policy.RequireRole(Bastet.Models.ApplicationRoles.Admin));

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

WebApplication app = builder.Build();

// Auto-run migrations if environment variable is set to true
if (autoMigrate)
{
    using IServiceScope scope = app.Services.CreateScope();

    // EF Core's Migrate() is not safe to run concurrently from multiple processes: on a
    // multi-replica cold start (e.g. Container Apps scale-out), two instances can both see a
    // pending migration and both apply it. Serialize replicas with a session-owned application
    // lock held on a dedicated connection for the duration of both Migrate() calls. A replica
    // that cannot get the lock within the timeout fails startup loudly; on restart it finds the
    // migrations applied and Migrate() no-ops.
    using SqlConnection migrationLockConnection = new(connectionString);
    migrationLockConnection.Open();

    using (SqlCommand getLock = new("sp_getapplock", migrationLockConnection))
    {
        getLock.CommandType = System.Data.CommandType.StoredProcedure;
        getLock.CommandTimeout = 330; // seconds; must exceed @LockTimeout below
        getLock.Parameters.AddWithValue("@Resource", "Bastet:Migration");
        getLock.Parameters.AddWithValue("@LockMode", "Exclusive");
        getLock.Parameters.AddWithValue("@LockOwner", "Session");
        getLock.Parameters.AddWithValue("@LockTimeout", 300000); // 5 min - a peer may be mid-migration
        SqlParameter lockResult = getLock.Parameters.Add("@ReturnValue", System.Data.SqlDbType.Int);
        lockResult.Direction = System.Data.ParameterDirection.ReturnValue;
        getLock.ExecuteNonQuery();

        if ((int)lockResult.Value < 0)
        {
            throw new InvalidOperationException(
                $"Could not acquire the 'Bastet:Migration' application lock (result code {lockResult.Value}). "
                + "Another replica appears to be stuck applying migrations. Startup was aborted rather than "
                + "risking a concurrent migration.");
        }
    }

    try
    {
        // Migrate main application database
        BastetDbContext dbContext = scope.ServiceProvider.GetRequiredService<BastetDbContext>();
        dbContext.Database.Migrate();

        // Migrate Data Protection keys database
        DataProtectionDbContext dpContext = scope.ServiceProvider.GetRequiredService<DataProtectionDbContext>();
        dpContext.Database.Migrate();
    }
    finally
    {
        // Closing the connection would release the session lock too; releasing explicitly keeps
        // the intent visible and covers connection-pool reuse.
        using SqlCommand releaseLock = new("sp_releaseapplock", migrationLockConnection);
        releaseLock.CommandType = System.Data.CommandType.StoredProcedure;
        releaseLock.Parameters.AddWithValue("@Resource", "Bastet:Migration");
        releaseLock.Parameters.AddWithValue("@LockOwner", "Session");
        releaseLock.ExecuteNonQuery();
    }
}

// Log warning if DataProtectionKeys table doesn't exist
// This helps users understand why multi-replica deployments may have authentication issues
if (!dataProtectionTableExists)
{
    app.Logger.LogWarning(
        "DataProtectionKeys table not found in database. Data Protection keys will use ephemeral storage. " +
        "This works for single-replica deployments but will cause authentication issues with multiple replicas " +
        "without session affinity. Run the 2.5.sql or higher migration script or enable BASTET_AUTO_MIGRATE=true to resolve this.");
}

// Configure the HTTP request pipeline.
app.UseForwardedHeaders(); // Process forwarded headers early to ensure HTTPS scheme is preserved

// Security response headers on every response (any scheme/host, so plain-HTTP self-hosting and any
// proxy work). Framing is configurable for hosts that legitimately embed the app; it defaults to
// disallowing all framing (clickjacking protection). A full CSP is intentionally not added - the app
// uses many inline <script> blocks.
string? configuredFrameAncestors = Environment.GetEnvironmentVariable("BASTET_FRAME_ANCESTORS");
string frameAncestors = string.IsNullOrWhiteSpace(configuredFrameAncestors) ? "'none'" : configuredFrameAncestors;
app.Use(async (context, next) =>
{
    IHeaderDictionary headers = context.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers.ContentSecurityPolicy = $"frame-ancestors {frameAncestors}";
    if (frameAncestors == "'none'")
    {
        headers.XFrameOptions = "DENY"; // legacy-browser parity with frame-ancestors 'none'
    }

    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.MapOpenApi();

    // In development, also use status code pages but with direct re-execution
    // This allows us to see the custom error pages while still getting detailed error info
    app.UseStatusCodePagesWithReExecute("/Error/{0}");
}
else
{
    // Configure status code pages with re-execution
    app.UseStatusCodePagesWithReExecute("/Error/{0}");

    // Configure global exception handler
    app.UseExceptionHandler("/Error");

    // Use HSTS and HTTPS redirection in non-development environments
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Enable static files
app.UseStaticFiles();

// Only registered when BASTET_CORS_ORIGINS is set; calling UseCors without a policy would throw.
if (corsOrigins.Length > 0)
{
    app.UseCors();
}

app.UseRouting();

// Always use Authentication and Authorization in all environments
// In development, our DevAuthHandler will auto-authenticate
app.UseAuthentication();
app.UseAuthorization();

var defaultRoute = new { name = "default", pattern = "{controller=Home}/{action=Index}/{id?}" };

// No need for RequireAuthorization anymore since we're handling auth with policies
app.MapControllers();
app.MapControllerRoute(defaultRoute.name, defaultRoute.pattern);

app.Run();
