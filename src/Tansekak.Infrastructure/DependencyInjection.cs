using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tansekak.Application;
using Tansekak.Infrastructure.Configuration;
using Tansekak.Application.Common;
using Tansekak.Application.Interfaces;
using Tansekak.Infrastructure.Identity;
using Tansekak.Infrastructure.Persistence;
using Tansekak.Infrastructure.Seeding;
using Tansekak.Infrastructure.Services;

namespace Tansekak.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (!configuration.GetValue<bool>("Testing:UseSqlite"))
        {
            var connectionString = DatabaseConnectionResolver.Resolve(configuration);
            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(connectionString));
        }

        services.AddDataProtection()
            .PersistKeysToDbContext<AppDbContext>();

        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        if (!environment.IsDevelopment())
        {
            services.ConfigureApplicationCookie(options =>
            {
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
            });
        }

        services.AddSingleton<ISeedDataSource, SeedFileSystemReader>();
        services.AddScoped<EntityIdAllocator>();
        services.AddScoped<CurrentAdmissionYearProvider>();
        services.AddScoped<IDataSeeder, JsonSeedService>();
        services.AddScoped<FacultyAllowedTracksRepairService>();
        services.AddScoped<CutoffBootstrapService>();
        services.AddScoped<IConfigService, ConfigService>();
        services.AddScoped<IAdmissionPredictionService, AdmissionPredictionService>();
        services.AddScoped<IGovernorateService, GovernorateService>();
        services.AddScoped<IUniversityService, UniversityService>();
        services.AddScoped<IFacultyService, FacultyService>();
        services.AddScoped<IUniversityFacultyService, UniversityFacultyService>();
        services.AddScoped<IAdmissionYearService, AdmissionYearService>();
        services.AddScoped<IAdmissionCutoffService, AdmissionCutoffService>();
        services.AddScoped<IImportService, ImportService>();
        services.AddScoped<IStudentResultImportService, StudentResultImportService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IStudentResultService, StudentResultService>();

        return services;
    }

    public static async Task SeedDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Tansekak.Startup");

        ProductionConfigurationValidator.Validate(config, environment);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (db.Database.IsRelational())
            await db.Database.MigrateAsync();
        else
            await db.Database.EnsureCreatedAsync();

        var seeder = scope.ServiceProvider.GetRequiredService<IDataSeeder>();
        await seeder.SeedAsync();

        var facultyRepair = scope.ServiceProvider.GetRequiredService<FacultyAllowedTracksRepairService>();
        await facultyRepair.RepairAsync();

        var cutoffBootstrap = scope.ServiceProvider.GetRequiredService<CutoffBootstrapService>();
        await cutoffBootstrap.BootstrapMissingTracksAsync();

        await SeedAdminUserAsync(scope.ServiceProvider);
    }

    private static async Task SeedAdminUserAsync(IServiceProvider sp)
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();

        const string role = Roles.Administrator;
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));

        var environment = sp.GetRequiredService<IHostEnvironment>();
        var email = config["AdminSeed:Email"];
        var password = config["AdminSeed:Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "AdminSeed:Email and AdminSeed:Password must be configured outside Development.");
            }

            email = string.IsNullOrWhiteSpace(email) ? "admin@tansekak.local" : email;
            password = string.IsNullOrWhiteSpace(password) ? "Admin@12345" : password;
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                LockoutEnabled = true
            };
            await userManager.CreateAsync(user, password);
            await userManager.AddToRoleAsync(user, role);
            return;
        }

        if (!user.LockoutEnabled)
        {
            user.LockoutEnabled = true;
            await userManager.UpdateAsync(user);
        }
    }
}
