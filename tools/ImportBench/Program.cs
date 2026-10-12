using System.Diagnostics;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tansekak.Domain.Entities;
using Tansekak.Infrastructure.Persistence;
using Tansekak.Infrastructure.Services;

var phase = Environment.GetEnvironmentVariable("BENCH_PHASE") ?? "all";
var connectionString = Environment.GetEnvironmentVariable("BENCH_CONNECTION");
var rowCount = int.Parse(Environment.GetEnvironmentVariable("BENCH_ROWS") ?? "800000");
var filePath = Environment.GetEnvironmentVariable("BENCH_FILE") ?? Path.Combine(Path.GetTempPath(), "thanaweya-bench.xlsx");

if (phase is "generate" or "all")
{
    var generate = Stopwatch.StartNew();
    WriteWorkbook(filePath, rowCount);
    generate.Stop();
    Console.WriteLine($"FILE bytes={new FileInfo(filePath).Length} rows={rowCount} generateMs={generate.ElapsedMilliseconds}");
}

if (phase == "generate")
    return 0;

if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("BENCH_CONNECTION is required.");

var peakBytes = 0L;
using var sampler = new Timer(_ =>
{
    var bytes = Environment.WorkingSet;
    long seen;
    do
    {
        seen = Volatile.Read(ref peakBytes);
    }
    while (bytes > seen && Interlocked.CompareExchange(ref peakBytes, bytes, seen) != seen);
}, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));

var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
services.AddScoped<EntityIdAllocator>();
services.AddScoped<StudentResultImportService>();

await using var provider = services.BuildServiceProvider();
await using (var scope = provider.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    if (!await db.AdmissionYears.AnyAsync())
    {
        db.AdmissionYears.Add(new AdmissionYear { Id = 1, Year = 2027, MaximumScore = 410, IsCurrent = true });
        await db.SaveChangesAsync();
    }
}

var fileBytes = new FileInfo(filePath).Length;
var import = Stopwatch.StartNew();
await using (var scope = provider.CreateAsyncScope())
await using (var stream = File.OpenRead(filePath))
{
    var service = scope.ServiceProvider.GetRequiredService<StudentResultImportService>();
    var result = await service.ImportAsync(1, stream, "bench.xlsx", "bench@tansekak.local");
    import.Stop();
    Console.WriteLine(
        $"IMPORT success={result.Success} count={result.ImportedCount} message={result.Message} importMs={import.ElapsedMilliseconds}");
    if (!result.Success)
        return 1;
}

Console.WriteLine(
    $"PEAK workingSetBytes={Volatile.Read(ref peakBytes)} importMs={import.ElapsedMilliseconds}");
Console.WriteLine(ReadCgroupPeak());
return 0;

static void WriteWorkbook(string path, int rowCount)
{
    using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
    var workbookPart = document.AddWorkbookPart();
    workbookPart.Workbook = new Workbook();

    var sharedStrings = workbookPart.AddNewPart<SharedStringTablePart>();
    using (var writer = OpenXmlWriter.Create(sharedStrings))
    {
        writer.WriteStartElement(new SharedStringTable());
        foreach (var text in new[] { "seating_no", "arabic_name", "total_degree" })
            writer.WriteElement(new SharedStringItem(new Text(text)));
        for (var i = 0; i < rowCount; i++)
            writer.WriteElement(new SharedStringItem(new Text($"طالب {i}")));
        writer.WriteEndElement();
    }

    var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
    using (var writer = OpenXmlWriter.Create(worksheetPart))
    {
        writer.WriteStartElement(new Worksheet());
        writer.WriteStartElement(new SheetData());
        WriteRow(writer, 1, "A1", "0", CellValues.SharedString, "B1", "1", CellValues.SharedString, "C1", "2", CellValues.SharedString);
        for (var i = 0; i < rowCount; i++)
        {
            var rowNumber = i + 2;
            var score = (150 + (i % 250)) + (i % 2 == 0 ? ".5" : "");
            WriteRow(
                writer,
                (uint)rowNumber,
                $"A{rowNumber}",
                (1_000_000 + i).ToString(),
                null,
                $"B{rowNumber}",
                (i + 3).ToString(),
                CellValues.SharedString,
                $"C{rowNumber}",
                score,
                null);
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    workbookPart.Workbook.AppendChild(new Sheets(new Sheet
    {
        Id = workbookPart.GetIdOfPart(worksheetPart),
        SheetId = 1,
        Name = "Results"
    }));
    workbookPart.Workbook.Save();
}

static void WriteRow(
    OpenXmlWriter writer,
    uint rowIndex,
    string aRef,
    string aValue,
    CellValues? aType,
    string bRef,
    string bValue,
    CellValues? bType,
    string cRef,
    string cValue,
    CellValues? cType)
{
    writer.WriteStartElement(new Row { RowIndex = rowIndex });
    WriteCell(writer, aRef, aValue, aType);
    WriteCell(writer, bRef, bValue, bType);
    WriteCell(writer, cRef, cValue, cType);
    writer.WriteEndElement();
}

static void WriteCell(OpenXmlWriter writer, string reference, string value, CellValues? type)
{
    var cell = new Cell { CellReference = reference, CellValue = new CellValue(value) };
    if (type is not null)
        cell.DataType = type.Value;
    writer.WriteElement(cell);
}

static string ReadCgroupPeak()
{
    foreach (var path in new[] { "/sys/fs/cgroup/memory.peak", "/sys/fs/cgroup/memory/memory.max_usage_in_bytes" })
    {
        if (File.Exists(path))
            return $"CGROUP {path}={File.ReadAllText(path).Trim()}";
    }

    return "CGROUP unavailable";
}
