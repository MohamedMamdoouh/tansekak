using System.Globalization;
using System.Text;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Tansekak.Application.Common;
using Tansekak.Application.DTOs;

namespace Tansekak.Infrastructure.Import;

public sealed record ParsedStudentResultRow(
    int RowNumber,
    string SeatingNo,
    string ArabicName,
    decimal TotalDegree);

public sealed class StudentResultExcelReadOutcome
{
    public required IReadOnlyList<ImportValidationErrorDto> Errors { get; init; }
    public required int TotalErrorCount { get; init; }
    public required int ValidRowCount { get; init; }
}

/// <summary>
/// Single-pass, streaming reader for the student results workbook (first sheet, headers in row 1).
/// Memory stays proportional to the shared-strings table and the seating-number set, never to the sheet XML.
/// </summary>
public static class StudentResultExcelReader
{
    public const int MaxErrorsReturned = 100;
    public const int MaxSeatingNoLength = 20;
    public const int MaxArabicNameLength = 300;

    private const string SeatingNoHeader = "seating_no";
    private const string ArabicNameHeader = "arabic_name";
    private const string TotalDegreeHeader = "total_degree";

    private static readonly string[] RequiredHeaders = [SeatingNoHeader, ArabicNameHeader, TotalDegreeHeader];

    private const NumberStyles DecimalStyle =
        NumberStyles.AllowLeadingWhite
        | NumberStyles.AllowTrailingWhite
        | NumberStyles.AllowLeadingSign
        | NumberStyles.AllowDecimalPoint;

    /// <summary>
    /// Validates every row. <paramref name="onValidRow"/> receives valid rows only until the first error;
    /// validation continues afterwards so the total error count is known.
    /// </summary>
    public static StudentResultExcelReadOutcome ReadAndValidate(
        Stream stream,
        decimal maximumScore,
        Action<ParsedStudentResultRow>? onValidRow,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ImportValidationErrorDto>();
        var totalErrorCount = 0;
        var validRowCount = 0;

        void AddError(ImportValidationErrorDto error)
        {
            totalErrorCount++;
            if (errors.Count < MaxErrorsReturned)
                errors.Add(error);
        }

        StudentResultExcelReadOutcome Outcome() => new()
        {
            Errors = errors,
            TotalErrorCount = totalErrorCount,
            ValidRowCount = validRowCount
        };

        try
        {
            using var document = SpreadsheetDocument.Open(stream, false);
            var workbookPart = document.WorkbookPart;
            if (workbookPart?.Workbook?.Sheets?.Elements<Sheet>().FirstOrDefault()?.Id?.Value is not { } sheetId)
            {
                AddError(Err(0, "File", ApiErrorCodes.WorkbookEmpty));
                return Outcome();
            }

            if (!workbookPart.TryGetPartById(sheetId, out var sheetPart) || sheetPart is not WorksheetPart worksheetPart)
            {
                AddError(Err(0, "File", ApiErrorCodes.WorksheetEmpty));
                return Outcome();
            }

            var sharedStrings = LoadSharedStrings(workbookPart, cancellationToken);
            var seenSeatingNos = new HashSet<string>(StringComparer.Ordinal);
            ColumnMap? columns = null;
            var hasHeader = false;

            foreach (var row in ReadRows(worksheetPart, sharedStrings, cancellationToken))
            {
                if (!hasHeader)
                {
                    hasHeader = true;
                    columns = MapColumns(row, AddError);
                    if (columns is null)
                        return Outcome();
                    continue;
                }

                var values = columns!.Extract(row);
                if (values.IsEmpty)
                    continue;

                var parsed = ValidateRow(row.RowNumber, values, maximumScore, seenSeatingNos, AddError);
                if (parsed is null)
                    continue;

                validRowCount++;
                if (totalErrorCount == 0)
                    onValidRow?.Invoke(parsed);
            }

            if (!hasHeader)
                AddError(Err(0, "File", ApiErrorCodes.WorksheetEmpty));
            else if (validRowCount == 0 && totalErrorCount == 0)
                AddError(Err(0, "File", ApiErrorCodes.FileNoDataRows));

            return Outcome();
        }
        catch (Exception ex) when (IsInvalidWorkbookException(ex))
        {
            AddError(Err(0, "File", ApiErrorCodes.ImportFileInvalid));
            return Outcome();
        }
    }

    private static ParsedStudentResultRow? ValidateRow(
        int rowNumber,
        RowValues values,
        decimal maximumScore,
        HashSet<string> seenSeatingNos,
        Action<ImportValidationErrorDto> addError)
    {
        var valid = true;

        void Fail(ImportValidationErrorDto error)
        {
            valid = false;
            addError(error);
        }

        var seatingNo = values.SeatingNo;
        if (string.IsNullOrEmpty(seatingNo))
            Fail(Err(rowNumber, SeatingNoHeader, ApiErrorCodes.Required));
        else if (seatingNo.Length > MaxSeatingNoLength)
            Fail(FieldTooLong(rowNumber, SeatingNoHeader, MaxSeatingNoLength));
        else if (!IsAsciiDigits(seatingNo))
            Fail(Err(rowNumber, SeatingNoHeader, ApiErrorCodes.SeatingNoInvalid));

        var arabicName = values.ArabicName;
        if (string.IsNullOrEmpty(arabicName))
            Fail(Err(rowNumber, ArabicNameHeader, ApiErrorCodes.Required));
        else if (arabicName.Length > MaxArabicNameLength)
            Fail(FieldTooLong(rowNumber, ArabicNameHeader, MaxArabicNameLength));

        if (!TryParseTotalDegree(values.TotalDegree, values.TotalDegreeIsNumeric, out var totalDegree))
            Fail(Err(rowNumber, TotalDegreeHeader, ApiErrorCodes.TotalDegreeInvalid));
        else if (totalDegree < 0)
            Fail(Err(rowNumber, TotalDegreeHeader, ApiErrorCodes.TotalDegreeNegative));
        else if (totalDegree > maximumScore)
        {
            Fail(Err(
                rowNumber,
                TotalDegreeHeader,
                ApiErrorCodes.ScoreExceedsMax,
                ArabicErrorCatalog.GetMessage(ApiErrorCodes.ScoreExceedsMax, maximumScore)));
        }
        else if (decimal.Round(totalDegree, 2) != totalDegree)
            Fail(Err(rowNumber, TotalDegreeHeader, ApiErrorCodes.TotalDegreeInvalid));

        if (!valid)
            return null;

        if (!seenSeatingNos.Add(seatingNo!))
        {
            addError(Err(rowNumber, SeatingNoHeader, ApiErrorCodes.Duplicate));
            return null;
        }

        return new ParsedStudentResultRow(rowNumber, seatingNo!, arabicName!, totalDegree);
    }

    private static ColumnMap? MapColumns(SheetRow headerRow, Action<ImportValidationErrorDto> addError)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var cell in headerRow.Cells)
            map.TryAdd(NormalizeHeader(cell.Value), cell.Column);

        var missing = false;
        foreach (var header in RequiredHeaders)
        {
            if (map.ContainsKey(header))
                continue;

            missing = true;
            addError(Err(
                headerRow.RowNumber,
                header,
                ApiErrorCodes.MissingColumn,
                ArabicErrorCatalog.GetMessage(ApiErrorCodes.MissingColumn, header)));
        }

        return missing
            ? null
            : new ColumnMap(map[SeatingNoHeader], map[ArabicNameHeader], map[TotalDegreeHeader]);
    }

    /// <summary>
    /// Streams the sheet one row at a time. The yielded <see cref="SheetRow"/> is reused, so callers
    /// must read it before moving to the next row.
    /// </summary>
    private static IEnumerable<SheetRow> ReadRows(
        WorksheetPart worksheetPart,
        string[] sharedStrings,
        CancellationToken cancellationToken)
    {
        using var reader = OpenXmlReader.Create(worksheetPart);
        var row = new SheetRow();
        var inlineText = new StringBuilder();
        var column = 0;
        string? cellType = null;
        string? cellValue = null;

        while (reader.Read())
        {
            var type = reader.ElementType;
            if (type == typeof(Row))
            {
                if (reader.IsStartElement)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    row.Start(ParseRowNumber(GetAttribute(reader, "r")) ?? row.RowNumber + 1);
                    column = 0;
                }
                else
                {
                    yield return row;
                }
            }
            else if (type == typeof(Cell))
            {
                if (reader.IsStartElement)
                {
                    column = ParseColumnNumber(GetAttribute(reader, "r")) ?? column + 1;
                    cellType = GetAttribute(reader, "t");
                    cellValue = null;
                    inlineText.Clear();
                }
                else
                {
                    var value = ResolveCellValue(cellType, cellValue, inlineText, sharedStrings).Trim();
                    if (value.Length > 0)
                        row.Cells.Add(new SheetCell(column, value, cellType is null or "n"));
                }
            }
            else if (reader.IsStartElement && type == typeof(CellValue))
            {
                cellValue = reader.GetText();
            }
            else if (reader.IsStartElement && type == typeof(Text))
            {
                inlineText.Append(reader.GetText());
            }
        }
    }

    private static string ResolveCellValue(
        string? cellType,
        string? cellValue,
        StringBuilder inlineText,
        string[] sharedStrings) =>
        cellType switch
        {
            "s" => int.TryParse(cellValue, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                   && index < sharedStrings.Length
                ? sharedStrings[index]
                : string.Empty,
            "inlineStr" => inlineText.ToString(),
            _ => cellValue ?? string.Empty
        };

    private static string[] LoadSharedStrings(WorkbookPart workbookPart, CancellationToken cancellationToken)
    {
        if (workbookPart.SharedStringTablePart is not { } part)
            return [];

        var strings = new List<string>();
        var item = new StringBuilder();
        var inPhoneticRun = false;

        using var reader = OpenXmlReader.Create(part);
        while (reader.Read())
        {
            var type = reader.ElementType;
            if (type == typeof(SharedStringItem))
            {
                if (reader.IsStartElement)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    item.Clear();
                }
                else
                {
                    strings.Add(item.ToString());
                }
            }
            else if (type == typeof(PhoneticRun))
            {
                inPhoneticRun = reader.IsStartElement;
            }
            else if (reader.IsStartElement && type == typeof(Text) && !inPhoneticRun)
            {
                item.Append(reader.GetText());
            }
        }

        return strings.ToArray();
    }

    // Numeric cells hold Excel's binary double (for example 163.55000000000001); converting through
    // double rounds to Excel's 15 significant digits, which is the value the admin sees.
    // Values beyond 1e15 are rejected because the decimal conversion would overflow or lose digits.
    private static bool TryParseTotalDegree(string? value, bool isNumericCell, out decimal result)
    {
        result = 0;
        if (string.IsNullOrEmpty(value))
            return false;

        if (isNumericCell)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                || !double.IsFinite(number)
                || Math.Abs(number) > 1e15)
                return false;

            result = (decimal)number;
            return true;
        }

        return decimal.TryParse(value, DecimalStyle, CultureInfo.InvariantCulture, out result)
            || decimal.TryParse(value.Replace(',', '.'), DecimalStyle, CultureInfo.InvariantCulture, out result);
    }

    private static bool IsAsciiDigits(string value)
    {
        foreach (var ch in value)
        {
            if (!char.IsAsciiDigit(ch))
                return false;
        }

        return true;
    }

    private static string? GetAttribute(OpenXmlReader reader, string localName)
    {
        foreach (var attribute in reader.Attributes)
        {
            if (attribute.LocalName == localName && string.IsNullOrEmpty(attribute.NamespaceUri))
                return attribute.Value;
        }

        return null;
    }

    private static int? ParseRowNumber(string? value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static int? ParseColumnNumber(string? cellReference)
    {
        if (string.IsNullOrEmpty(cellReference))
            return null;

        var number = 0;
        foreach (var ch in cellReference)
        {
            if (!char.IsAsciiLetter(ch))
                break;
            number = number * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }

        return number > 0 ? number : null;
    }

    private static string NormalizeHeader(string value) =>
        value.Trim().ToLowerInvariant().Replace(' ', '_');

    private static bool IsInvalidWorkbookException(Exception ex) =>
        ex is OpenXmlPackageException
            or FileFormatException
            or InvalidDataException
            or XmlException
            or IOException;

    private static ImportValidationErrorDto Err(int row, string col, string code, string? message = null) =>
        new(row, col, code, message ?? ArabicErrorCatalog.GetMessage(code));

    private static ImportValidationErrorDto FieldTooLong(int row, string col, int maxLength) =>
        new(
            row,
            col,
            ApiErrorCodes.FieldTooLong,
            ArabicErrorCatalog.GetMessage(ApiErrorCodes.FieldTooLong, col, maxLength));

    private readonly record struct SheetCell(int Column, string Value, bool IsNumeric);

    private sealed class SheetRow
    {
        public int RowNumber { get; private set; }
        public List<SheetCell> Cells { get; } = [];

        public void Start(int rowNumber)
        {
            RowNumber = rowNumber;
            Cells.Clear();
        }
    }

    private readonly record struct RowValues(
        string? SeatingNo,
        string? ArabicName,
        string? TotalDegree,
        bool TotalDegreeIsNumeric)
    {
        public bool IsEmpty => SeatingNo is null && ArabicName is null && TotalDegree is null;
    }

    private sealed record ColumnMap(int SeatingNo, int ArabicName, int TotalDegree)
    {
        public RowValues Extract(SheetRow row)
        {
            string? seatingNo = null, arabicName = null, totalDegree = null;
            var totalDegreeIsNumeric = false;
            foreach (var cell in row.Cells)
            {
                if (cell.Column == SeatingNo)
                    seatingNo = cell.Value;
                else if (cell.Column == ArabicName)
                    arabicName = cell.Value;
                else if (cell.Column == TotalDegree)
                {
                    totalDegree = cell.Value;
                    totalDegreeIsNumeric = cell.IsNumeric;
                }
            }

            return new RowValues(seatingNo, arabicName, totalDegree, totalDegreeIsNumeric);
        }
    }
}
