using Bastet.Data;
using Bastet.Services.Data;
using Bastet.Filters;
using Bastet.Services;
using Bastet.Services.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Console;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.SetMinimumLevel(Enum.TryParse(Environment.GetEnvironmentVariable("BASTET_LOG_LEVEL_DEFAULT") ?? "Warning", true, out LogLevel level) ? level : LogLevel.Warning);
    builder.Logging.AddFilter("Microsoft.AspNetCore", Enum.TryParse(Environment.GetEnvironmentVariable("BASTET_LOG_LEVEL_ASPNETCORE") ?? "Warning", true, out LogLevel aspNetLevel) ? aspNetLevel : LogLevel.Warning);
    builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", Enum.TryParse(Environment.GetEnvironmentVariable("BASTET_LOG_LEVEL_ENTITYFRAMEWORK") ?? "Warning", true, out LogLevel efLevel) ? efLevel : LogLevel.Warning);
}

builder.Logging.AddConsoleFormatter<SanitizingConsoleFormatter, ConsoleFormatterOptions>();
builder.Logging.AddConsole(options => options.FormatterName = SanitizingConsoleFormatter.FormatterName);

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<GlobalSanitizationFilter>();

    options.Filters.Add(new Microsoft.AspNetCore.Mvc.ResponseCacheAttribute
    {
        NoStore = true,
        Location = Microsoft.AspNetCore.Mvc.ResponseCacheLocation.None
    });
});

builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");

string? connectionString = Environment.GetEnvironmentVariable("BASTET_CONNECTION_STRING")
    ?? (builder.Environment.IsDevelopment()
        ? builder.Configuration.GetConnectionString("DefaultConnection")
        : throw new InvalidOperationException("Production environment requires BASTET_CONNECTION_STRING environment variable to be set."));

builder.Services.AddDbContext<BastetDbContext>(options =>
{
    options.UseSqlServer(connectionString);
});

builder.Services.AddDbContext<DataProtectionDbContext>(options =>
{
    options.UseSqlServer(connectionString);
});

bool autoMigrate = bool.TryParse(Environment.GetEnvironmentVariable("BASTET_AUTO_MIGRATE"), out bool autoMigrateResult) && autoMigrateResult;
bool dataProtectionTableExists = false;

if (autoMigrate)
{

    dataProtectionTableExists = true;
}
else
{

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

        dataProtectionTableExists = false;
    }
}

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

builder.Services.AddScoped<IIpUtilityService, IpUtilityService>();
builder.Services.AddScoped<Bastet.Services.Validation.ISubnetValidationService, Bastet.Services.Validation.SubnetValidationService>();
builder.Services.AddScoped<Bastet.Services.Validation.IHostIpValidationService, Bastet.Services.Validation.HostIpValidationService>();
builder.Services.AddSingleton<Bastet.Services.Azure.AzureArmClientProvider>();
builder.Services.AddScoped<Bastet.Services.Azure.IAzureService, Bastet.Services.Azure.AzureService>();
builder.Services.AddScoped<Bastet.Services.Azure.IAzureBulkImportPlanner, Bastet.Services.Azure.AzureBulkImportPlanner>();
builder.Services.AddScoped<Bastet.Services.Azure.IAzureSubnetSnapshotService, Bastet.Services.Azure.AzureSubnetSnapshotService>();
builder.Services.AddScoped<Bastet.Services.Azure.IAzureReconciler, Bastet.Services.Azure.AzureReconciler>();

builder.Services.AddScoped<Bastet.Services.Security.IInputSanitizationService, Bastet.Services.Security.InputSanitizationService>();
builder.Services.AddSingleton<IVersionService, VersionService>();

builder.Services.AddScoped<Bastet.Services.Locking.ISubnetLockingService>(provider =>
{
    BastetDbContext context = provider.GetRequiredService<BastetDbContext>();
    ILogger<Bastet.Services.Locking.SqlServerSubnetLockingService> lockLogger =
        provider.GetRequiredService<ILogger<Bastet.Services.Locking.SqlServerSubnetLockingService>>();

    return context.Database.ProviderName?.ToLower() switch
    {
        "microsoft.entityframeworkcore.sqlite" => new Bastet.Services.Locking.SqliteSubnetLockingService(),
        "microsoft.entityframeworkcore.sqlserver" => new Bastet.Services.Locking.SqlServerSubnetLockingService(context, lockLogger),
        _ => new Bastet.Services.Locking.SqlServerSubnetLockingService(context, lockLogger)
    };
});

builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<IUserContextService, UserContextService>();

string[] corsOrigins = (Environment.GetEnvironmentVariable("BASTET_CORS_ORIGINS") ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

if (corsOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(corsOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()));
}

if (builder.Environment.IsDevelopment())
{

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = "DevAuthScheme";
        options.DefaultChallengeScheme = "DevAuthScheme";
    })

    .AddScheme<DevAuthOptions, DevAuthHandler>("DevAuthScheme", _ => { });
}
else
{

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

         options.Events.OnTicketReceived = context =>
         {
             AuthenticationProperties? properties = context.Properties;
             properties?.StoreTokens(
                 [.. properties.GetTokens().Where(token => token.Name == "id_token")]);
             return Task.CompletedTask;
         };

         options.Events.OnRemoteFailure = context =>
         {

             context.HttpContext.RequestServices
                 .GetRequiredService<ILoggerFactory>()
                 .CreateLogger("Bastet.Authentication")
                 .LogWarning("OIDC sign-in did not complete: {Reason}", context.Failure?.Message);

             context.Response.Redirect("/Account/SignInFailed");
             context.HandleResponse();
             return Task.CompletedTask;
         };
     });
}

builder.Services.AddAuthorizationBuilder()

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

if (autoMigrate)
{
    using IServiceScope scope = app.Services.CreateScope();

    using SqlConnection migrationLockConnection = OpenMigrationLockConnection();

    SqlConnection OpenMigrationLockConnection()
    {
        try
        {
            return Open(MigrationLockConnectionString.Configured(connectionString));
        }
        catch (SqlException ex) when (ex.Number == 4060)
        {

            SqlConnection bootstrapConnection;

            try
            {
                bootstrapConnection = Open(MigrationLockConnectionString.MasterBootstrap(connectionString));
            }
            catch (SqlException bootstrapException)
            {

                throw new InvalidOperationException(
                    "BASTET_AUTO_MIGRATE is enabled, but the configured database does not exist and the "
                    + $"login could not open '{MigrationLockConnectionString.BootstrapCatalog}' to create it. "
                    + "Either create the database first and grant the login db_owner inside it, or grant the "
                    + $"login access to '{MigrationLockConnectionString.BootstrapCatalog}'. "
                    + "See BASTET_CONNECTION_STRING and BASTET_AUTO_MIGRATE.", bootstrapException);
            }

            string configuredCatalog = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
            int? catalogAccess = null;

            try
            {
                using SqlCommand probe = new("SELECT HAS_DBACCESS(@catalog)", bootstrapConnection);
                probe.Parameters.AddWithValue("@catalog", configuredCatalog);
                catalogAccess = probe.ExecuteScalar() is int access ? access : null;
            }
            catch (SqlException)
            {

            }

            if (catalogAccess == 0)
            {
                bootstrapConnection.Dispose();

                throw new InvalidOperationException(
                    $"The configured database '{configuredCatalog}' exists on this server but could not be "
                    + "opened, which SQL Server reports as error 4060 using the same text it uses for a "
                    + "database that does not exist. Either the login in BASTET_CONNECTION_STRING has no "
                    + $"user inside that database (CREATE USER inside '{configuredCatalog}', then db_owner "
                    + "for BASTET_AUTO_MIGRATE=true - if the database was restored or failed over the user "
                    + "may be orphaned: ALTER USER ... WITH LOGIN), or the database is offline or "
                    + "recovering. Do not grant the login permission to create databases; it does not need "
                    + "it.", ex);
            }

            return bootstrapConnection;
        }

        static SqlConnection Open(string? lockConnectionString)
        {
            SqlConnection connection = new(lockConnectionString);

            try
            {
                connection.Open();
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }
    }

    using (SqlCommand getLock = new("sp_getapplock", migrationLockConnection))
    {
        getLock.CommandType = System.Data.CommandType.StoredProcedure;
        getLock.CommandTimeout = 330;
        getLock.Parameters.AddWithValue("@Resource", "Bastet:Migration");
        getLock.Parameters.AddWithValue("@LockMode", "Exclusive");
        getLock.Parameters.AddWithValue("@LockOwner", "Session");
        getLock.Parameters.AddWithValue("@LockTimeout", 300000);
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

        BastetDbContext dbContext = scope.ServiceProvider.GetRequiredService<BastetDbContext>();
        dbContext.Database.SetCommandTimeout(330);
        dbContext.Database.Migrate();

        DataProtectionDbContext dpContext = scope.ServiceProvider.GetRequiredService<DataProtectionDbContext>();
        dpContext.Database.SetCommandTimeout(330);
        dpContext.Database.Migrate();
    }
    catch (SqlException ex) when (SqlSaveOutcome.IsIndeterminateErrorNumber(ex.Number))
    {
        throw new InvalidOperationException(
            "Timed out waiting for another replica to finish applying migrations. "
            + "Another replica appears to be stuck applying migrations. Startup was aborted rather than "
            + "risking a concurrent migration.", ex);
    }
    finally
    {

        try
        {
            using SqlCommand releaseLock = new("sp_releaseapplock", migrationLockConnection);
            releaseLock.CommandType = System.Data.CommandType.StoredProcedure;
            releaseLock.Parameters.AddWithValue("@Resource", "Bastet:Migration");
            releaseLock.Parameters.AddWithValue("@LockOwner", "Session");
            releaseLock.ExecuteNonQuery();
        }
        catch (Exception releaseException)
        {
            app.Logger.LogError(releaseException,
                "Failed to release the 'Bastet:Migration' application lock after migration; discarding the pooled "
                + "connection so the session-owned lock is dropped rather than stranded. Startup continues.");

            try
            {
                SqlConnection.ClearPool(migrationLockConnection);
            }
            catch (Exception discardException)
            {
                app.Logger.LogError(discardException,
                    "Failed to discard the pooled migration-lock connection after a failed release.");
            }
        }
    }
}

if (!dataProtectionTableExists)
{
    app.Logger.LogWarning(
        "DataProtectionKeys table not found in database. Data Protection keys will use ephemeral storage. " +
        "This works for single-replica deployments but will cause authentication issues with multiple replicas " +
        "without session affinity. Run the 2.5.sql or higher migration script or enable BASTET_AUTO_MIGRATE=true to resolve this.");
}

app.UseForwardedHeaders();

string? configuredFrameAncestors = Environment.GetEnvironmentVariable("BASTET_FRAME_ANCESTORS")?.Trim();
if (!string.IsNullOrWhiteSpace(configuredFrameAncestors)
    && !Bastet.Services.Security.HttpHeaderValue.IsValid(configuredFrameAncestors))
{
    throw new InvalidOperationException(
        "BASTET_FRAME_ANCESTORS contains a character that cannot be sent in an HTTP header " +
        "(non-ASCII or control characters). Check the value for stray line endings or smart quotes.");
}

string frameAncestors = string.IsNullOrWhiteSpace(configuredFrameAncestors) ? "'none'" : configuredFrameAncestors;

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();

    app.UseStatusCodePagesWithReExecute("/Error/{0}");
}
else
{

    app.UseStatusCodePagesWithReExecute("/Error/{0}");

    app.UseExceptionHandler("/Error");

    app.UseHsts();
    app.UseHttpsRedirection();
}

app.Use(async (context, next) =>
{
    IHeaderDictionary headers = context.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers.ContentSecurityPolicy = $"frame-ancestors {frameAncestors}";
    if (frameAncestors == "'none'")
    {
        headers.XFrameOptions = "DENY";
    }

    await next();
});

app.UseStaticFiles();

if (corsOrigins.Length > 0)
{
    app.UseCors();
}

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

var defaultRoute = new { name = "default", pattern = "{controller=Home}/{action=Index}/{id?}" };

app.MapControllers();
app.MapControllerRoute(defaultRoute.name, defaultRoute.pattern);

app.Run();
