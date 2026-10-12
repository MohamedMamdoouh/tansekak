using Tansekak.Domain.Enums;

namespace Tansekak.Application.Common;

public static class TrackHelper
{
    private static readonly Dictionary<string, AcademicTrack> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Science"] = AcademicTrack.Science,
        ["Mathematics"] = AcademicTrack.Mathematics,
        ["Literature"] = AcademicTrack.Literature,
        ["علمي علوم"] = AcademicTrack.Science,
        ["علمي رياضة"] = AcademicTrack.Mathematics,
        ["علمي رياضه"] = AcademicTrack.Mathematics,
        ["أدبي"] = AcademicTrack.Literature,
        ["ادبي"] = AcademicTrack.Literature,
        ["الادبي"] = AcademicTrack.Literature
    };

    public static bool TryParse(string? value, out AcademicTrack track)
    {
        var normalized = Normalize(value);
        if (Map.TryGetValue(normalized, out track))
            return true;

        track = default;
        return false;
    }

    public static string ToDisplayName(AcademicTrack track) => track switch
    {
        AcademicTrack.Science => "Science",
        AcademicTrack.Mathematics => "Mathematics",
        AcademicTrack.Literature => "Literature",
        _ => track.ToString()
    };

    public static string ToArabicName(AcademicTrack track) => track switch
    {
        AcademicTrack.Science => "علمي علوم",
        AcademicTrack.Mathematics => "علمي رياضة",
        AcademicTrack.Literature => "أدبي",
        _ => track.ToString()
    };

    public static string ToArabicName(string track) =>
        TryParse(track, out var t) ? ToArabicName(t) : track;

    public static IReadOnlyList<string> AllTracks { get; } =
        ["Science", "Mathematics", "Literature"];

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim()
            .Replace('ى', 'ي')
            .Replace('أ', 'ا')
            .Replace('إ', 'ا')
            .Replace('آ', 'ا');
}
