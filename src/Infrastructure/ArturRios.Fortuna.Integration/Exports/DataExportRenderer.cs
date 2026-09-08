using System.Globalization;
using System.Text;
using ArturRios.Fortuna.Domain.Exports;
using ArturRios.Fortuna.Shared.Exports;
using ClosedXML.Excel;

namespace ArturRios.Fortuna.Integration.Exports;

public sealed class DataExportRenderer : IDataExportRenderer
{
    public RenderedDataExport Render(DataExportDocument document, DataExportFormat format) =>
        format switch
        {
            DataExportFormat.Csv => Csv(document),
            DataExportFormat.Excel => Excel(document),
            DataExportFormat.Pdf => Pdf(document),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

    private static RenderedDataExport Csv(DataExportDocument document)
    {
        var culture = CultureInfo.GetCultureInfo(document.Locale);
        var separator = culture.TextInfo.ListSeparator;
        var output = new StringBuilder();
        output.AppendLine(string.Join(separator,
            document.Columns.Select(column => Escape(column.Name, separator))));
        foreach (var row in document.Rows)
        {
            output.AppendLine(string.Join(separator, document.Columns.Select(column =>
                Escape(Format(RowValue(row, column.Name), culture), separator))));
        }

        AppendCsvTotals(output, document.Totals, culture, separator);
        return new RenderedDataExport(
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(output.ToString()),
            "text/csv; charset=utf-8",
            "csv");
    }

    private static RenderedDataExport Excel(DataExportDocument document)
    {
        var culture = CultureInfo.GetCultureInfo(document.Locale);
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Export");
        var columnIndex = 1;
        foreach (var column in document.Columns)
        {
            worksheet.Cell(1, columnIndex++).SetValue(column.Name);
        }

        var rowIndex = 2;
        foreach (var row in document.Rows)
        {
            columnIndex = 1;
            foreach (var column in document.Columns)
            {
                worksheet.Cell(rowIndex, columnIndex++)
                    .SetValue(Format(RowValue(row, column.Name), culture));
            }

            rowIndex++;
        }

        AppendExcelTotals(worksheet, document.Totals, culture, rowIndex + 1);
        worksheet.ColumnsUsed().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return new RenderedDataExport(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "xlsx");
    }

    private static RenderedDataExport Pdf(DataExportDocument document)
    {
        var culture = CultureInfo.GetCultureInfo(document.Locale);
        var lines = new List<string>
        {
            $"Fortuna export: {document.RecordSet}",
            string.Join(" | ", document.Columns.Select(column => column.Name))
        };
        lines.AddRange(document.Rows.Select(row => string.Join(" | ",
            document.Columns.Select(column => Format(RowValue(row, column.Name), culture)))));
        if (document.Totals.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Totals");
            lines.AddRange(document.Totals.Select(total =>
                $"{total.Column} | {total.CurrencyCode} | " +
                (total.Value.HasValue ? Format(total.Value.Value, culture) : string.Empty)));
        }

        return new RenderedDataExport(
            SimplePdf.Write(lines),
            "application/pdf",
            "pdf");
    }

    private static object? RowValue(
        IReadOnlyDictionary<string, object?> row,
        string column) => row.TryGetValue(column, out var value) ? value : null;

    private static string Format(object? value, CultureInfo culture) => value switch
    {
        null => string.Empty,
        decimal number => number.ToString("0.############################", culture),
        DateOnly date => date.ToString("d", culture),
        DateTimeOffset timestamp => timestamp.ToString("G", culture),
        DateTime timestamp => timestamp.ToString("G", culture),
        IFormattable formattable => formattable.ToString(null, culture) ?? string.Empty,
        _ => value.ToString() ?? string.Empty
    };

    private static string Escape(string value, string separator) =>
        value.Contains(separator, StringComparison.Ordinal) ||
        value.Contains('"') || value.Contains('\r') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;

    private static void AppendCsvTotals(
        StringBuilder output,
        IReadOnlyCollection<DataExportTotal> totals,
        CultureInfo culture,
        string separator)
    {
        if (totals.Count == 0)
        {
            return;
        }

        output.AppendLine();
        output.AppendLine("Totals");
        output.AppendLine(string.Join(separator, "Column", "Currency", "Value"));
        foreach (var total in totals)
        {
            output.AppendLine(string.Join(separator,
                Escape(total.Column, separator),
                Escape(total.CurrencyCode ?? string.Empty, separator),
                Escape(total.Value.HasValue ? Format(total.Value.Value, culture) : string.Empty,
                    separator)));
        }
    }

    private static void AppendExcelTotals(
        IXLWorksheet worksheet,
        IReadOnlyCollection<DataExportTotal> totals,
        CultureInfo culture,
        int rowIndex)
    {
        if (totals.Count == 0)
        {
            return;
        }

        worksheet.Cell(rowIndex++, 1).SetValue("Totals");
        worksheet.Cell(rowIndex, 1).SetValue("Column");
        worksheet.Cell(rowIndex, 2).SetValue("Currency");
        worksheet.Cell(rowIndex++, 3).SetValue("Value");
        foreach (var total in totals)
        {
            worksheet.Cell(rowIndex, 1).SetValue(total.Column);
            worksheet.Cell(rowIndex, 2).SetValue(total.CurrencyCode ?? string.Empty);
            worksheet.Cell(rowIndex++, 3).SetValue(
                total.Value.HasValue ? Format(total.Value.Value, culture) : string.Empty);
        }
    }

    private static class SimplePdf
    {
        private const int LinesPerPage = 55;

        public static byte[] Write(IReadOnlyCollection<string> source)
        {
            var pages = source
                .Select(line => Ascii(line.Length <= 115 ? line : line[..115]))
                .Chunk(LinesPerPage)
                .ToArray();
            if (pages.Length == 0)
            {
                pages = [[]];
            }

            var fontId = 3 + pages.Length * 2;
            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                $"<< /Type /Pages /Count {pages.Length} /Kids [" +
                string.Join(' ', Enumerable.Range(0, pages.Length)
                    .Select(index => $"{3 + index * 2} 0 R")) + "] >>"
            };
            for (var index = 0; index < pages.Length; index++)
            {
                var contentId = 4 + index * 2;
                objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 842] " +
                    $"/Resources << /Font << /F1 {fontId} 0 R >> >> " +
                    $"/Contents {contentId} 0 R >>");
                var content = "BT /F1 9 Tf 36 806 Td 13 TL " + string.Join(' ',
                    pages[index].Select(line => $"({PdfEscape(line)}) Tj T*")) + " ET";
                objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n" +
                    content + "\nendstream");
            }

            objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
            using var stream = new MemoryStream();
            WriteText(stream, "%PDF-1.4\n");
            var offsets = new List<long> { 0 };
            for (var index = 0; index < objects.Count; index++)
            {
                offsets.Add(stream.Position);
                WriteText(stream, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
            }

            var xref = stream.Position;
            WriteText(stream, $"xref\n0 {objects.Count + 1}\n");
            WriteText(stream, "0000000000 65535 f \n");
            foreach (var offset in offsets.Skip(1))
            {
                WriteText(stream, $"{offset:0000000000} 00000 n \n");
            }

            WriteText(stream, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\n" +
                $"startxref\n{xref}\n%%EOF");
            return stream.ToArray();
        }

        private static string Ascii(string value) => new(value
            .Select(character => character is >= ' ' and <= '~' ? character : '?')
            .ToArray());

        private static string PdfEscape(string value) => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);

        private static void WriteText(Stream stream, string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}
