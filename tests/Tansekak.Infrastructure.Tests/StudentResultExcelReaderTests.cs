using System.IO.Compression;
using System.Text;
using Tansekak.Application.Common;
using Tansekak.Infrastructure.Import;
using Tansekak.Tests.Shared;

namespace Tansekak.Infrastructure.Tests;

public class StudentResultExcelReaderTests
{
    private const decimal MaximumScore = 320;

    private static readonly object?[] Header = ["seating_no", "arabic_name", "total_degree"];

    [Fact]
    public void Reads_shared_inline_and_numeric_cells()
    {
        using var stream = TestWorkbook.Create(
            Header,
            [1001660L, new InlineText("محمد ابو الحسن"), 163.5m],
            [" 1001661 ", "محمد احمد ابو زيد", "187.5"]);

        var (outcome, rows) = Read(stream);

        Assert.Equal(0, outcome.TotalErrorCount);
        Assert.Equal(2, outcome.ValidRowCount);
        Assert.Equal(
            [
                new ParsedStudentResultRow(2, "1001660", "محمد ابو الحسن", 163.5m),
                new ParsedStudentResultRow(3, "1001661", "محمد احمد ابو زيد", 187.5m)
            ],
            rows);
    }

    [Fact]
    public void Accepts_headers_in_any_order_and_case_and_ignores_extra_columns()
    {
        using var stream = TestWorkbook.Create(
            ["Total_Degree ", "notes", "ARABIC NAME", "seating_no"],
            [200, "ملاحظة", "محمد", 1001660L]);

        var (outcome, rows) = Read(stream);

        Assert.Equal(0, outcome.TotalErrorCount);
        Assert.Equal(new ParsedStudentResultRow(2, "1001660", "محمد", 200), Assert.Single(rows));
    }

    [Fact]
    public void Rejects_file_with_missing_header()
    {
        using var stream = TestWorkbook.Create(
            ["seating_no", "name", "total_degree"],
            [1001660L, "محمد", 200]);

        var (outcome, rows) = Read(stream);

        var error = Assert.Single(outcome.Errors);
        Assert.Equal(ApiErrorCodes.MissingColumn, error.ErrorCode);
        Assert.Equal("arabic_name", error.Column);
        Assert.Contains("arabic_name", error.Message);
        Assert.Empty(rows);
    }

    [Theory]
    [InlineData("12A4", ApiErrorCodes.SeatingNoInvalid)]
    [InlineData("١٢٣٤", ApiErrorCodes.SeatingNoInvalid)]
    [InlineData("123456789012345678901", ApiErrorCodes.FieldTooLong)]
    [InlineData(null, ApiErrorCodes.Required)]
    public void Rejects_invalid_seating_number(string? seatingNo, string expectedCode)
    {
        using var stream = TestWorkbook.Create(Header, [seatingNo, "محمد", 200]);

        var (outcome, _) = Read(stream);

        var error = Assert.Single(outcome.Errors);
        Assert.Equal((2, "seating_no", expectedCode), (error.RowNumber, error.Column, error.ErrorCode));
    }

    [Fact]
    public void Rejects_missing_or_too_long_name()
    {
        using var stream = TestWorkbook.Create(
            Header,
            [1L, null, 200],
            [2L, new string('م', 301), 200]);

        var (outcome, _) = Read(stream);

        Assert.Equal(
            [(2, ApiErrorCodes.Required), (3, ApiErrorCodes.FieldTooLong)],
            outcome.Errors.Select(e => (e.RowNumber, e.ErrorCode)));
    }

    [Theory]
    [InlineData("-1", ApiErrorCodes.TotalDegreeNegative)]
    [InlineData("320.5", ApiErrorCodes.ScoreExceedsMax)]
    [InlineData("163.555", ApiErrorCodes.TotalDegreeInvalid)]
    [InlineData("abc", ApiErrorCodes.TotalDegreeInvalid)]
    [InlineData("1,234", ApiErrorCodes.TotalDegreeInvalid)]
    public void Rejects_invalid_total_degree(string totalDegree, string expectedCode)
    {
        using var stream = TestWorkbook.Create(Header, [1001660L, "محمد", totalDegree]);

        var (outcome, _) = Read(stream);

        var error = Assert.Single(outcome.Errors);
        Assert.Equal((2, "total_degree", expectedCode), (error.RowNumber, error.Column, error.ErrorCode));
    }

    [Fact]
    public void Score_above_maximum_message_states_the_maximum()
    {
        using var stream = TestWorkbook.Create(Header, [1001660L, "محمد", 400]);

        var (outcome, _) = Read(stream);

        Assert.Contains("320", Assert.Single(outcome.Errors).Message);
    }

    [Theory]
    [InlineData("16,5", 16.5)]
    [InlineData("320", 320)]
    [InlineData("0", 0)]
    public void Accepts_text_scores_including_decimal_comma(string totalDegree, decimal expected)
    {
        using var stream = TestWorkbook.Create(Header, [1001660L, "محمد", totalDegree]);

        var (_, rows) = Read(stream);

        Assert.Equal(expected, Assert.Single(rows).TotalDegree);
    }

    [Fact]
    public void Rounds_numeric_cell_binary_noise_to_excel_precision()
    {
        using var stream = TestWorkbook.Create(Header, [1001660L, "محمد", new NumericText("163.55000000000001")]);

        var (outcome, rows) = Read(stream);

        Assert.Equal(0, outcome.TotalErrorCount);
        Assert.Equal(163.55m, Assert.Single(rows).TotalDegree);
    }

    [Fact]
    public void Rejects_duplicate_seating_numbers()
    {
        using var stream = TestWorkbook.Create(
            Header,
            [1001660L, "أ", 200],
            ["1001660", "ب", 210]);

        var (outcome, _) = Read(stream);

        var error = Assert.Single(outcome.Errors);
        Assert.Equal((3, ApiErrorCodes.Duplicate), (error.RowNumber, error.ErrorCode));
    }

    [Fact]
    public void Skips_empty_rows_and_reports_real_row_numbers()
    {
        using var stream = TestWorkbook.Create(
            Header,
            [1L, "أ", 200],
            [null, null, null],
            ["  ", new InlineText(" "), null],
            [2L, "ب", 999]);

        var (outcome, _) = Read(stream);

        Assert.Equal(1, outcome.ValidRowCount);
        Assert.Equal(5, Assert.Single(outcome.Errors).RowNumber);
    }

    [Fact]
    public void Returns_first_100_errors_and_counts_all()
    {
        var rows = new List<object?[]> { Header };
        for (var i = 0; i < 150; i++)
            rows.Add([1000L + i, "محمد", "abc"]);
        using var stream = TestWorkbook.Create(rows);

        var (outcome, _) = Read(stream);

        Assert.Equal(StudentResultExcelReader.MaxErrorsReturned, outcome.Errors.Count);
        Assert.Equal(150, outcome.TotalErrorCount);
    }

    [Fact]
    public void Stops_emitting_rows_after_first_error_but_keeps_counting()
    {
        using var stream = TestWorkbook.Create(
            Header,
            [1L, "أ", 200],
            [2L, "ب", "abc"],
            [3L, "ج", 210]);

        var (outcome, rows) = Read(stream);

        Assert.Equal("1", Assert.Single(rows).SeatingNo);
        Assert.Equal(2, outcome.ValidRowCount);
        Assert.Equal(1, outcome.TotalErrorCount);
    }

    [Fact]
    public void Header_without_data_rows_is_rejected()
    {
        using var stream = TestWorkbook.Create(Header);

        var (outcome, _) = Read(stream);

        Assert.Equal(ApiErrorCodes.FileNoDataRows, Assert.Single(outcome.Errors).ErrorCode);
    }

    [Fact]
    public void Empty_sheet_is_rejected()
    {
        using var stream = TestWorkbook.Create();

        var (outcome, _) = Read(stream);

        Assert.Equal(ApiErrorCodes.WorksheetEmpty, Assert.Single(outcome.Errors).ErrorCode);
    }

    [Fact]
    public void Corrupt_zip_is_rejected()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("PK\u0003\u0004 not really a zip"));

        var (outcome, _) = Read(stream);

        Assert.Equal(ApiErrorCodes.ImportFileInvalid, Assert.Single(outcome.Errors).ErrorCode);
    }

    [Fact]
    public void Zip_without_a_workbook_is_rejected()
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("readme.txt").Open());
            writer.Write("hello");
        }

        stream.Position = 0;
        var (outcome, _) = Read(stream);

        Assert.Equal(ApiErrorCodes.WorkbookEmpty, Assert.Single(outcome.Errors).ErrorCode);
    }

    private static (StudentResultExcelReadOutcome Outcome, List<ParsedStudentResultRow> Rows) Read(Stream stream)
    {
        var rows = new List<ParsedStudentResultRow>();
        var outcome = StudentResultExcelReader.ReadAndValidate(stream, MaximumScore, rows.Add);
        return (outcome, rows);
    }
}
