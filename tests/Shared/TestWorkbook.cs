using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Tansekak.Tests.Shared;

public sealed record InlineText(string Value);

public sealed record NumericText(string Value);

/// <summary>
/// Builds small .xlsx files for tests. Strings become shared strings (as Excel saves them),
/// <see cref="InlineText"/> becomes an inline string, numbers and <see cref="NumericText"/> become numeric cells,
/// and null leaves the cell out.
/// </summary>
public static class TestWorkbook
{
    public static MemoryStream Create(params object?[][] rows) => Create((IEnumerable<object?[]>)rows);

    public static MemoryStream Create(IEnumerable<object?[]> rows)
    {
        var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var sharedStrings = new List<string>();
            var sharedIndex = new Dictionary<string, int>(StringComparer.Ordinal);

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            uint rowIndex = 1;
            foreach (var values in rows)
            {
                var row = new Row { RowIndex = rowIndex };
                for (var col = 0; col < values.Length; col++)
                {
                    if (values[col] is { } value)
                        row.Append(CreateCell(value, $"{(char)('A' + col)}{rowIndex}", sharedStrings, sharedIndex));
                }

                sheetData.Append(row);
                rowIndex++;
            }

            worksheetPart.Worksheet = new Worksheet(sheetData);

            var sharedStringPart = workbookPart.AddNewPart<SharedStringTablePart>();
            sharedStringPart.SharedStringTable = new SharedStringTable(
                sharedStrings.Select(s => (OpenXmlElement)new SharedStringItem(
                    new Text(s) { Space = SpaceProcessingModeValues.Preserve })));

            workbookPart.Workbook.AppendChild(new Sheets(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Sheet1"
            }));
            workbookPart.Workbook.Save();
        }

        stream.Position = 0;
        return stream;
    }

    private static Cell CreateCell(
        object value,
        string reference,
        List<string> sharedStrings,
        Dictionary<string, int> sharedIndex) =>
        value switch
        {
            InlineText inline => new Cell
            {
                CellReference = reference,
                DataType = CellValues.InlineString,
                InlineString = new InlineString(new Text(inline.Value) { Space = SpaceProcessingModeValues.Preserve })
            },
            string text => new Cell
            {
                CellReference = reference,
                DataType = CellValues.SharedString,
                CellValue = new CellValue(GetSharedIndex(text, sharedStrings, sharedIndex))
            },
            NumericText raw => new Cell
            {
                CellReference = reference,
                CellValue = new CellValue(raw.Value)
            },
            IFormattable number => new Cell
            {
                CellReference = reference,
                CellValue = new CellValue(number.ToString(null, CultureInfo.InvariantCulture))
            },
            _ => throw new ArgumentException($"Unsupported cell value type {value.GetType()}.", nameof(value))
        };

    private static int GetSharedIndex(string text, List<string> sharedStrings, Dictionary<string, int> sharedIndex)
    {
        if (sharedIndex.TryGetValue(text, out var index))
            return index;

        index = sharedStrings.Count;
        sharedStrings.Add(text);
        sharedIndex[text] = index;
        return index;
    }
}
