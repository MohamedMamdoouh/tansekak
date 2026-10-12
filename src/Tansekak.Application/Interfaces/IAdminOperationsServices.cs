using Tansekak.Application.DTOs;

namespace Tansekak.Application.Interfaces;

public interface IDashboardService
{
    Task<DashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default);
}

public interface IImportService
{
    Task<ImportResultDto> ImportAsync(int yearId, string track, Stream fileStream, string fileName, CancellationToken cancellationToken = default);
}

public interface IStudentResultImportService
{
    Task<StudentResultsSummaryDto> GetSummaryAsync(int yearId, CancellationToken cancellationToken = default);

    Task<ImportResultDto> ImportAsync(
        int yearId,
        Stream fileStream,
        string fileName,
        string performedByEmail,
        CancellationToken cancellationToken = default);

    Task<DeleteStudentResultsDto> DeleteAllAsync(
        int yearId,
        string performedByEmail,
        CancellationToken cancellationToken = default);
}
