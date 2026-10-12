namespace Tansekak.Application.DTOs;

public record ConfigDto(
    string AppName,
    int CurrentYear,
    decimal MaximumScore,
    IReadOnlyList<string> Tracks,
    bool StudentResultsAvailable);

public record PredictRequestDto(string Track, decimal Score, int Page = 1, int PageSize = 10);

public record NamedEntityDto(string NameAr);

public record AdmissionResultDto(
    NamedEntityDto University,
    NamedEntityDto Faculty);

public record PredictResponseDto(
    IReadOnlyList<AdmissionResultDto> Results,
    bool HasMore,
    int TotalCount);

public record StudentResultDto(
    string SeatingNo,
    string ArabicName,
    decimal TotalDegree,
    int Year,
    decimal MaximumScore);
