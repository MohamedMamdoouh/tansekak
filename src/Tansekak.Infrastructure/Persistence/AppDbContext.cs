using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Tansekak.Domain.Entities;
using Tansekak.Infrastructure.Identity;

namespace Tansekak.Infrastructure.Persistence;

public class AppDbContext : IdentityDbContext<ApplicationUser>, IDataProtectionKeyContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Governorate> Governorates => Set<Governorate>();
    public DbSet<University> Universities => Set<University>();
    public DbSet<Faculty> Faculties => Set<Faculty>();
    public DbSet<UniversityFaculty> UniversityFaculties => Set<UniversityFaculty>();
    public DbSet<AdmissionYear> AdmissionYears => Set<AdmissionYear>();
    public DbSet<AdmissionCutoff> AdmissionCutoffs => Set<AdmissionCutoff>();
    public DbSet<StudentResult> StudentResults => Set<StudentResult>();
    public DbSet<StudentResultImportEvent> StudentResultImportEvents => Set<StudentResultImportEvent>();
    public DbSet<EntityIdSequence> EntityIdSequences => Set<EntityIdSequence>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
