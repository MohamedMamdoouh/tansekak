using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tansekak.Domain.Entities;

namespace Tansekak.Infrastructure.Persistence.Configurations;

public class StudentResultImportEventConfiguration : IEntityTypeConfiguration<StudentResultImportEvent>
{
    public void Configure(EntityTypeBuilder<StudentResultImportEvent> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.FileName).HasMaxLength(260);
        e.Property(x => x.PerformedByEmail).HasMaxLength(256).IsRequired();
        e.Property(x => x.Action).HasConversion<int>();
        e.HasIndex(x => new { x.AdmissionYearId, x.PerformedAt });
        e.HasOne(x => x.AdmissionYear).WithMany()
            .HasForeignKey(x => x.AdmissionYearId).OnDelete(DeleteBehavior.Restrict);
    }
}
