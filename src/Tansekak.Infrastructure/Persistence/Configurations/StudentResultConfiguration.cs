using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tansekak.Domain.Entities;

namespace Tansekak.Infrastructure.Persistence.Configurations;

public class StudentResultConfiguration : IEntityTypeConfiguration<StudentResult>
{
    public void Configure(EntityTypeBuilder<StudentResult> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.SeatingNo).HasMaxLength(20).IsRequired();
        e.Property(x => x.ArabicName).HasMaxLength(300).IsRequired();
        e.Property(x => x.TotalDegree).HasPrecision(6, 2);
        e.HasIndex(x => new { x.AdmissionYearId, x.SeatingNo }).IsUnique();
        e.HasOne(x => x.AdmissionYear).WithMany(x => x.StudentResults)
            .HasForeignKey(x => x.AdmissionYearId).OnDelete(DeleteBehavior.Restrict);
    }
}
