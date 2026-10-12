namespace Tansekak.Domain.Entities;

public enum StudentResultImportAction
{
    Imported = 1,
    Deleted = 2
}

public class StudentResultImportEvent
{
    public int Id { get; set; }
    public int AdmissionYearId { get; set; }
    public StudentResultImportAction Action { get; set; }
    public string? FileName { get; set; }
    public int? RowCount { get; set; }
    public DateTime PerformedAt { get; set; }
    public string PerformedByEmail { get; set; } = string.Empty;

    public AdmissionYear AdmissionYear { get; set; } = null!;
}
