using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tansekak.Application.Common;
using Tansekak.Domain.Entities;
using Tansekak.Domain.Enums;
using Tansekak.Infrastructure.Identity;
using Tansekak.Infrastructure.Persistence;

namespace Tansekak.Api.Tests;

public sealed class TansekakWebApplicationFactory : WebApplicationFactory<Program>
{
    private SqliteConnection? _connection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Testing:SkipStartupSeed", "true");
        builder.UseSetting("Testing:UseSqlite", "true");

        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        builder.ConfigureServices(services =>
        {
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        SeedIntegrationData(db);
        SeedAdmin(scope.ServiceProvider);

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _connection?.Dispose();

        base.Dispose(disposing);
    }

    private static void SeedIntegrationData(AppDbContext db)
    {
        if (db.AdmissionYears.Any())
            return;

        db.AdmissionYears.Add(new AdmissionYear
        {
            Id = 1,
            Year = 2026,
            MaximumScore = 410,
            IsCurrent = true,
        });
        db.Governorates.Add(new Governorate { Id = 1, NameAr = "القاهرة" });
        db.Universities.Add(new University
        {
            Id = 1,
            NameAr = "جامعة القاهرة",
            GovernorateId = 1,
            Type = UniversityType.Public,
        });
        db.Faculties.AddRange(
            new Faculty { Id = 1, NameAr = "طب", AllowedTracks = [AcademicTrack.Science] },
            new Faculty { Id = 2, NameAr = "هندسة", AllowedTracks = [AcademicTrack.Mathematics] },
            new Faculty { Id = 3, NameAr = "صيدلة", AllowedTracks = [AcademicTrack.Science] });
        db.UniversityFaculties.AddRange(
            new UniversityFaculty { Id = 1, UniversityId = 1, FacultyId = 1 },
            new UniversityFaculty { Id = 2, UniversityId = 1, FacultyId = 2 },
            new UniversityFaculty { Id = 3, UniversityId = 1, FacultyId = 3 });
        db.AdmissionCutoffs.AddRange(
            new AdmissionCutoff
            {
                Id = 1,
                AdmissionYearId = 1,
                UniversityFacultyId = 1,
                Track = AcademicTrack.Science,
                CutoffScore = 350,
            },
            new AdmissionCutoff
            {
                Id = 2,
                AdmissionYearId = 1,
                UniversityFacultyId = 2,
                Track = AcademicTrack.Mathematics,
                CutoffScore = 280,
            },
            new AdmissionCutoff
            {
                Id = 3,
                AdmissionYearId = 1,
                UniversityFacultyId = 3,
                Track = AcademicTrack.Science,
                CutoffScore = 360,
            });
        db.StudentResults.AddRange(
            new StudentResult
            {
                Id = 1,
                AdmissionYearId = 1,
                SeatingNo = "2410001",
                ArabicName = "طالب أ",
                TotalDegree = 390,
            },
            new StudentResult
            {
                Id = 2,
                AdmissionYearId = 1,
                SeatingNo = "2410002",
                ArabicName = "طالب ب",
                TotalDegree = 380,
            });
        db.SaveChanges();
    }

    private static void SeedAdmin(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        if (!roleManager.RoleExistsAsync(Roles.Administrator).GetAwaiter().GetResult())
            roleManager.CreateAsync(new IdentityRole(Roles.Administrator)).GetAwaiter().GetResult();

        if (userManager.FindByEmailAsync(AdminEmail).GetAwaiter().GetResult() is not null)
            return;

        var user = new ApplicationUser
        {
            UserName = AdminEmail,
            Email = AdminEmail,
            EmailConfirmed = true
        };
        var created = userManager.CreateAsync(user, AdminPassword).GetAwaiter().GetResult();
        if (!created.Succeeded)
            throw new InvalidOperationException(string.Join(", ", created.Errors.Select(e => e.Description)));

        userManager.AddToRoleAsync(user, Roles.Administrator).GetAwaiter().GetResult();
    }

    public const string AdminEmail = "admin@tansekak.local";
    public const string AdminPassword = "Admin@12345";
}
