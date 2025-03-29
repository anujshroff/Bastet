using Bastet.Data;
using Bastet.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
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
builder.Services.AddScoped<Bastet.Services.Division.ISubnetDivisionService, Bastet.Services.Division.SubnetDivisionService>();

// Add HttpContextAccessor for accessing the current user
builder.Services.AddHttpContextAccessor();

// Register UserContextService
builder.Services.AddScoped<IUserContextService, UserContextService>();

// Add CORS for Web UI
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod()));

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;

})
    .AddCookie()
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
     });

builder.Services.AddAuthorization();

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
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.MapOpenApi();
}

// Enable static files
app.UseStaticFiles();

app.UseCors();

app.UseRouting();

// Only use Authentication and Authorization if not in Development
if (!app.Environment.IsDevelopment())
{
    app.UseAuthentication();
    app.UseAuthorization();
}

var defaultRoute = new { name = "default", pattern = "{controller=Home}/{action=Index}/{id?}" };

_ = app.Environment.IsDevelopment() ? app.MapControllers() : app.MapControllers().RequireAuthorization();
_ = app.Environment.IsDevelopment()
    ? app.MapControllerRoute(defaultRoute.name, defaultRoute.pattern)
    : app.MapControllerRoute(defaultRoute.name, defaultRoute.pattern).RequireAuthorization();

app.Run();
