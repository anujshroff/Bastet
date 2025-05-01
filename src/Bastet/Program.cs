using Bastet.Data;
using Bastet.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
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
builder.Services.AddControllers();

// Add MVC
builder.Services.AddControllersWithViews();

// Add DbContext
builder.Services.AddDbContext<BastetDbContext>(options =>
{
    string? connectionString = Environment.GetEnvironmentVariable("BASTET_CONNECTION_STRING")
        ?? (builder.Environment.IsDevelopment()
            ? builder.Configuration.GetConnectionString("DefaultConnection")
            : throw new InvalidOperationException("Production environment requires BASTET_CONNECTION_STRING environment variable to be set."));

    options.UseSqlServer(connectionString);
});

// Register services
builder.Services.AddScoped<IIpUtilityService, IpUtilityService>();
builder.Services.AddScoped<Bastet.Services.Validation.ISubnetValidationService, Bastet.Services.Validation.SubnetValidationService>();
builder.Services.AddScoped<Bastet.Services.Validation.IHostIpValidationService, Bastet.Services.Validation.HostIpValidationService>();
builder.Services.AddScoped<Bastet.Services.Division.ISubnetDivisionService, Bastet.Services.Division.SubnetDivisionService>();
builder.Services.AddScoped<Bastet.Services.Azure.IAzureService, Bastet.Services.Azure.AzureService>();
builder.Services.AddSingleton<IVersionService, VersionService>();

// Add HttpContextAccessor for accessing the current user
builder.Services.AddHttpContextAccessor();

// Register UserContextService
builder.Services.AddScoped<IUserContextService, UserContextService>();

// Add CORS for Web UI
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod()));

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
    .AddCookie(options => options.AccessDeniedPath = "/Account/AccessDenied")
    .AddOpenIdConnect(options =>
     {
         options.ClientId = Environment.GetEnvironmentVariable("BASTET_OIDC_CLIENT_ID") ?? "mvc_client";
         options.Authority = Environment.GetEnvironmentVariable("BASTET_OIDC_AUTHORITY") ?? "https://localhost";
         options.CallbackPath = "/signin-oidc";
         options.SignedOutCallbackPath = "/signout-callback-oidc";
         options.ResponseType = "id_token";
         options.SaveTokens = true;
         options.UseTokenLifetime = true;
         options.Scope.Add("openid");
         options.Scope.Add("profile");
         options.Scope.Add("email");
         options.Scope.Add("roles");
     });
}

builder.Services.AddAuthorizationBuilder()
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
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

WebApplication app = builder.Build();

// Auto-run migrations if environment variable is set to true
bool autoMigrate = bool.TryParse(Environment.GetEnvironmentVariable("BASTET_AUTO_MIGRATE"), out bool result) && result;
if (autoMigrate)
{
    using IServiceScope scope = app.Services.CreateScope();
    BastetDbContext dbContext = scope.ServiceProvider.GetRequiredService<BastetDbContext>();
    dbContext.Database.Migrate();
}

// Configure the HTTP request pipeline.
app.UseForwardedHeaders(); // Process forwarded headers early to ensure HTTPS scheme is preserved

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

app.UseCors();

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
