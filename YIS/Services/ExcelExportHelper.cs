using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;

namespace YIS.Services
{
    public class ExcelColumn<T>
    {
        public string Header { get; set; }
        public Func<T, object?> ValueSelector { get; set; }
        public string? NumberFormat { get; set; }
        public XLAlignmentHorizontalValues Alignment { get; set; }

        public ExcelColumn(string header, Func<T, object?> valueSelector, string? numberFormat = null, XLAlignmentHorizontalValues alignment = XLAlignmentHorizontalValues.Left)
        {
            Header = header;
            ValueSelector = valueSelector;
            NumberFormat = numberFormat;
            Alignment = alignment;
        }
    }

    public static class ExcelExportHelper
    {
        public static byte[] ExportToExcel<T>(
            string title,
            IEnumerable<ExcelColumn<T>> columns,
            IEnumerable<T> data,
            string companyName = "Yarn Pro",
            string? filterInfo = null,
            bool addTotalsRow = false)
        {
            return CreateMultiSheetWorkbook(workbook =>
            {
                var worksheet = workbook.Worksheets.Add(SanitizeSheetName(title));
                PopulateWorksheet(worksheet, title, columns, data, companyName, filterInfo, addTotalsRow);
            });
        }

        public static byte[] CreateMultiSheetWorkbook(Action<XLWorkbook> configure)
        {
            using var workbook = new XLWorkbook();
            configure(workbook);
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        public static void PopulateWorksheet<T>(
            IXLWorksheet worksheet,
            string title,
            IEnumerable<ExcelColumn<T>> columns,
            IEnumerable<T> data,
            string companyName = "Yarn Pro",
            string? filterInfo = null,
            bool addTotalsRow = false)
        {
            worksheet.ShowGridLines = true;

            var columnList = new List<ExcelColumn<T>>(columns);
            int totalCols = columnList.Count;
            if (totalCols == 0) totalCols = 1;

            // 1. Title Banner (Row 1)
            worksheet.Cell(1, 1).Value = $"{companyName} - {title}";
            worksheet.Range(1, 1, 1, totalCols).Merge();
            var titleRange = worksheet.Range(1, 1, 1, totalCols);
            titleRange.Style.Font.Bold = true;
            titleRange.Style.Font.FontSize = 14;
            titleRange.Style.Font.FontColor = XLColor.White;
            titleRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E293B"); // Navy Slate
            titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            titleRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            worksheet.Row(1).Height = 28;

            // 2. Subtitle / Metadata (Row 2)
            string meta = $"Generated: {DateTime.Now:dd-MM-yyyy HH:mm:ss}";
            if (!string.IsNullOrWhiteSpace(filterInfo))
            {
                meta += $" | Filter: {filterInfo}";
            }
            worksheet.Cell(2, 1).Value = meta;
            worksheet.Range(2, 1, 2, totalCols).Merge();
            var metaRange = worksheet.Range(2, 1, 2, totalCols);
            metaRange.Style.Font.FontSize = 9;
            metaRange.Style.Font.Italic = true;
            metaRange.Style.Font.FontColor = XLColor.FromHtml("#64748B");
            metaRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5F9");
            metaRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            metaRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            worksheet.Row(2).Height = 18;

            // Row 3 is an empty spacing row
            worksheet.Row(3).Height = 6;

            // 3. Table Headers (Row 4)
            int headerRow = 4;
            worksheet.Row(headerRow).Height = 24;
            for (int i = 0; i < columnList.Count; i++)
            {
                var cell = worksheet.Cell(headerRow, i + 1);
                cell.Value = columnList[i].Header;
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontSize = 10;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#2563EB"); // Primary Blue
                cell.Style.Alignment.Horizontal = columnList[i].Alignment;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                cell.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                cell.Style.Border.SetOutsideBorderColor(XLColor.FromHtml("#1D4ED8"));
            }

            // 4. Data Rows (Row 5+)
            int currentRow = headerRow + 1;
            int rowIdx = 0;

            foreach (var item in data)
            {
                worksheet.Row(currentRow).Height = 20;
                bool isAlternate = (rowIdx % 2 != 0);

                for (int i = 0; i < columnList.Count; i++)
                {
                    var col = columnList[i];
                    var cell = worksheet.Cell(currentRow, i + 1);
                    var val = col.ValueSelector(item);

                    if (val == null)
                    {
                        cell.Value = "-";
                    }
                    else if (val is DateTime dt)
                    {
                        cell.Value = dt;
                        cell.Style.DateFormat.Format = col.NumberFormat ?? "dd-MM-yyyy";
                    }
                    else if (val is decimal decVal)
                    {
                        cell.Value = decVal;
                        cell.Style.NumberFormat.Format = col.NumberFormat ?? "#,##0.00";
                    }
                    else if (val is double dblVal)
                    {
                        cell.Value = dblVal;
                        cell.Style.NumberFormat.Format = col.NumberFormat ?? "#,##0.00";
                    }
                    else if (val is int intVal)
                    {
                        cell.Value = intVal;
                        cell.Style.NumberFormat.Format = col.NumberFormat ?? "#,##0";
                    }
                    else if (val is long lngVal)
                    {
                        cell.Value = lngVal;
                        cell.Style.NumberFormat.Format = col.NumberFormat ?? "#,##0";
                    }
                    else if (val is bool bVal)
                    {
                        cell.Value = bVal ? "Yes" : "No";
                    }
                    else
                    {
                        cell.Value = val.ToString();
                        if (!string.IsNullOrEmpty(col.NumberFormat))
                        {
                            cell.Style.NumberFormat.Format = col.NumberFormat;
                        }
                    }

                    cell.Style.Alignment.Horizontal = col.Alignment;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    cell.Style.Font.FontSize = 9.5;

                    if (isAlternate)
                    {
                        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
                    }

                    cell.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                    cell.Style.Border.SetOutsideBorderColor(XLColor.FromHtml("#E2E8F0"));
                }

                currentRow++;
                rowIdx++;
            }

            // 5. Totals Row
            if (addTotalsRow && rowIdx > 0)
            {
                worksheet.Row(currentRow).Height = 22;
                var totalLabel = worksheet.Cell(currentRow, 1);
                totalLabel.Value = "Total";
                totalLabel.Style.Font.Bold = true;
                totalLabel.Style.Font.FontSize = 10;
                totalLabel.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                totalLabel.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                totalLabel.Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5F9");
                totalLabel.Style.Border.TopBorder = XLBorderStyleValues.Thin;
                totalLabel.Style.Border.BottomBorder = XLBorderStyleValues.Double;

                int startDataRow = headerRow + 1;
                int endDataRow = currentRow - 1;

                for (int i = 0; i < columnList.Count; i++)
                {
                    var col = columnList[i];
                    var cell = worksheet.Cell(currentRow, i + 1);
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontSize = 10;
                    cell.Style.Alignment.Horizontal = col.Alignment;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5F9");
                    cell.Style.Border.TopBorder = XLBorderStyleValues.Thin;
                    cell.Style.Border.BottomBorder = XLBorderStyleValues.Double;

                    if (i > 0 && !string.IsNullOrEmpty(col.NumberFormat) && (col.NumberFormat.Contains("0") || col.NumberFormat.Contains("#")))
                    {
                        string startCell = worksheet.Cell(startDataRow, i + 1).Address.ToString();
                        string endCell = worksheet.Cell(endDataRow, i + 1).Address.ToString();
                        cell.FormulaA1 = $"SUM({startCell}:{endCell})";
                        cell.Style.NumberFormat.Format = col.NumberFormat;
                    }
                    else if (i > 0)
                    {
                        cell.Value = "";
                    }
                }
            }

            // Auto-fit column widths with breathing room
            worksheet.Columns().AdjustToContents(1, 5, 10, 50);
        }

        public static string SanitizeSheetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Sheet1";
            var invalidChars = new[] { ':', '\\', '/', '?', '*', '[', ']' };
            foreach (var ch in invalidChars)
            {
                name = name.Replace(ch, ' ');
            }
            name = name.Trim();
            if (name.Length > 31) name = name.Substring(0, 31);
            return string.IsNullOrWhiteSpace(name) ? "Sheet1" : name;
        }
    }
}
