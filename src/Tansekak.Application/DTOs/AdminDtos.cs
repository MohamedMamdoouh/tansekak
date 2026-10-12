namespace Tansekak.Application.DTOs;

public record GovernorateDto(int Id, string NameAr);
public record CreateGovernorateDto(string NameAr);
public record UpdateGovernorateDto(string NameAr);

public record UniversityDto(int Id, string NameAr, int GovernorateId, string Type, string? GovernorateName = null);
public record CreateUniversityDto(string NameAr, int GovernorateId, string Type);
public record UpdateUniversityDto(string NameAr, int GovernorateId, string Type);

public record FacultyDto(int Id, string NameAr, IReadOnlyList<string> AllowedTracks);
public record CreateFacultyDto(string NameAr, IReadOnlyList<string> AllowedTracks);
public record UpdateFacultyDto(string NameAr, IReadOnlyList<string> AllowedTracks);

public record UniversityFacultyDto(
    int Id,
    int UniversityId,
    int FacultyId,
    string? UniversityName = null,
    string? FacultyName = null);
public record CreateUniversityFacultyDto(int UniversityId, int FacultyId);
public record UpdateUniversityFacultyDto(int UniversityId, int FacultyId);

public record DashboardDto(
    int GovernoratesCount,
    int FacultiesCount,
    int StudentResultsCount,
    int? CurrentYear);

public record StudentResultImportEventDto(
    string Action,
    string? FileName,
    int? RowCount,
    DateTime PerformedAt,
    string PerformedByEmail);

public record StudentResultsSummaryDto(
    int TotalCount,
    StudentResultImportEventDto? LatestEvent);

public record DeleteStudentResultsDto(int DeletedCount);
