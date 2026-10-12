using Microsoft.EntityFrameworkCore;
using Tansekak.Application.DTOs;
using Tansekak.Application.Interfaces;
using Tansekak.Infrastructure.Persistence;

namespace Tansekak.Infrastructure.Services;

public class StudentResultService(AppDbContext db, CurrentAdmissionYearProvider yearProvider) : IStudentResultService
{
    public async Task<StudentResultDto?> GetBySeatingNoAsync(string seatingNo, CancellationToken cancellationToken = default)
    {
        var normalized = seatingNo.Trim();
        if (string.IsNullOrEmpty(normalized))
            return null;

        var currentYear = await yearProvider.GetCurrentAsync(cancellationToken);

        var entity = await db.StudentResults.AsNoTracking()
            .Where(x => x.AdmissionYearId == currentYear.Id && x.SeatingNo == normalized)
            .FirstOrDefaultAsync(cancellationToken);

        if (entity is null)
            return null;

        return new StudentResultDto(
            entity.SeatingNo,
            entity.ArabicName,
            entity.TotalDegree,
            currentYear.Year,
            currentYear.MaximumScore);
    }
}
