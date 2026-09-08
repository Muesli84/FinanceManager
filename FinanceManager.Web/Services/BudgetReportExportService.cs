using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Drawing.Spreadsheet;
using DocumentFormat.OpenXml.Drawing.Charts;
using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using FinanceManager.Application.Budget;
using FinanceManager.Domain.Budget;
using FinanceManager.Infrastructure;
using FinanceManager.Shared.Dtos.Budget;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.Services
{
    /// <summary>
    /// Minimal fallback implementation of <see cref="IBudgetReportExportService"/> used
    /// when no dedicated exporter is registered. Returns a small CSV stream.
    /// Production should replace this with a proper XLSX generator.
    /// </summary>
    internal sealed class BudgetReportExportService : IBudgetReportExportService
    {
        private const uint DefaultStyleIndex = 0;
        private const uint HeaderStyleIndex = 1;
        private const uint HeaderRightStyleIndex = 2;
        private const uint RightStyleIndex = 3;
        private readonly IBudgetReportService _reportService;
        private readonly AppDbContext _db;
        private readonly IStringLocalizer<BudgetReportExportService> _localizer;

        public BudgetReportExportService(
            IBudgetReportService reportService,
            AppDbContext db,
            IStringLocalizer<BudgetReportExportService> localizer)
        {
            _reportService = reportService ?? throw new ArgumentNullException(nameof(reportService));
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        }
        /// <summary>
        /// Generates a real XLSX workbook containing multiple sheets derived from the budget report raw data.
        /// Uses the OpenXML SDK to construct a minimal valid workbook that can be parsed by the test helper.
        /// Sheets produced: "Bank", "Contact", "SavingsPlan", "CategoriesAndPurposes".
        /// </summary>
        /// <param name="ownerUserId">The owner user id.</param>
        /// <param name="request">Request payload.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The result.</returns>
        public async Task<(string ContentType, string FileName, Stream Content)> GenerateXlsxAsync(Guid ownerUserId, BudgetReportExportRequest request, CancellationToken ct)
        {
            var to = request.AsOfDate;
            var from = new DateOnly(to.Year, to.Month, 1).AddMonths(-(request.Months - 1));
            var raw = await _reportService.GetRawDataAsync(ownerUserId, from, to, request.DateBasis, ct);
            var rules = await _db.BudgetRules
                .AsNoTracking()
                .Where(r => r.OwnerUserId == ownerUserId)
                .ToListAsync(ct);
            var culture = CultureInfo.CurrentUICulture;

            string L(string key, string fallback)
            {
                var value = _localizer[key].Value;
                return string.IsNullOrWhiteSpace(value) || value == key ? fallback : value;
            }

            var ms = new MemoryStream();
            // create spreadsheet document
            using (var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Create(ms, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook, true))
            {
                var workbookPart = doc.AddWorkbookPart();
                workbookPart.Workbook = new DocumentFormat.OpenXml.Spreadsheet.Workbook();

                var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
                stylesPart.Stylesheet = CreateStylesheet();
                stylesPart.Stylesheet.Save();

                var sstPart = workbookPart.AddNewPart<DocumentFormat.OpenXml.Packaging.SharedStringTablePart>();
                sstPart.SharedStringTable = new DocumentFormat.OpenXml.Spreadsheet.SharedStringTable();

                var sheets = workbookPart.Workbook.AppendChild(new DocumentFormat.OpenXml.Spreadsheet.Sheets());

                uint sheetId = 1;

                // helper to manage shared strings
                int InsertSharedString(string text)
                {
                    if (string.IsNullOrEmpty(text)) return -1;
                    var sst = sstPart.SharedStringTable;
                    var existing = sst.Elements<DocumentFormat.OpenXml.Spreadsheet.SharedStringItem>()
                        .Select((item, index) => new { item, index })
                        .FirstOrDefault(x => x.item.InnerText == text);
                    if (existing != null) return existing.index;
                    sst.AppendChild(new DocumentFormat.OpenXml.Spreadsheet.SharedStringItem(new DocumentFormat.OpenXml.Spreadsheet.Text(text)));
                    return sst.Count() - 1;
                }

                // gather postings
                var postings = new List<BudgetReportPostingRawDataDto>();
                foreach (var cat in raw.Categories ?? Array.Empty<BudgetReportCategoryRawDataDto>())
                {
                    foreach (var pur in cat.Purposes ?? Array.Empty<BudgetReportPurposeRawDataDto>())
                    {
                        foreach (var p in (pur.Postings ?? Array.Empty<BudgetReportPostingRawDataDto>()).Where(p => p.IsValuedForBudgetPurpose))
                        {
                            postings.Add(p with { BudgetCategoryName = cat.CategoryName, BudgetPurposeName = pur.PurposeName });
                        }
                    }
                }
                foreach (var pur in raw.UncategorizedPurposes ?? Array.Empty<BudgetReportPurposeRawDataDto>())
                {
                    foreach (var p in (pur.Postings ?? Array.Empty<BudgetReportPostingRawDataDto>()).Where(p => p.IsValuedForBudgetPurpose))
                    {
                        postings.Add(p with { BudgetCategoryName = null, BudgetPurposeName = pur.PurposeName });
                    }
                }
                foreach (var p in raw.UnbudgetedPostings ?? Array.Empty<BudgetReportPostingRawDataDto>())
                {
                    postings.Add(p with { BudgetCategoryName = p.BudgetCategoryName, BudgetPurposeName = p.BudgetPurposeName });
                }

                var periods = BuildPeriods(from, request.Months, raw, rules, request.DateBasis);
                var currentPeriod = periods.Last();
                var unbudgetedLabel = L("Budget_Report_Export_Row_Unbudgeted", "Unbudgeted");
                var currentMonthRows = BuildCurrentMonthRows(raw, rules, request.DateBasis, currentPeriod.From, currentPeriod.To, unbudgetedLabel);

                void CreatePostingSheet(string name, List<BudgetReportPostingRawDataDto> rows)
                {
                    var worksheetPart = workbookPart.AddNewPart<DocumentFormat.OpenXml.Packaging.WorksheetPart>();
                    var sheetData = new DocumentFormat.OpenXml.Spreadsheet.SheetData();
                    var headers = new[]
                    {
                        L("Budget_Report_Export_Header_BookingDate", "Booking date"),
                        L("Budget_Report_Export_Header_ValutaDate", "Valuta date"),
                        L("Budget_Report_Export_Header_Amount", "Amount"),
                        L("Budget_Report_Export_Header_Subject", "Subject"),
                        L("Budget_Report_Export_Header_Contact", "Contact"),
                        L("Budget_Report_Export_Header_SavingsPlan", "Savings plan"),
                        L("Budget_Report_Export_Header_Account", "Account"),
                        L("Budget_Report_Export_Header_Category", "Category"),
                        L("Budget_Report_Export_Header_Purpose", "Purpose")
                    };

                    var rowsData = new List<string?[]>(rows.Count);
                    foreach (var r in rows)
                    {
                        var bookingText = r.BookingDate.ToString("d", culture);
                        var valutaText = r.ValutaDate.HasValue ? r.ValutaDate.Value.ToString("d", culture) : string.Empty;
                        var amountText = r.Amount.ToString(CultureInfo.InvariantCulture);
                        rowsData.Add(new[]
                        {
                            bookingText,
                            valutaText,
                            amountText,
                            r.Description ?? string.Empty,
                            r.ContactName ?? string.Empty,
                            r.SavingsPlanName ?? string.Empty,
                            r.AccountName ?? string.Empty,
                            r.BudgetCategoryName ?? string.Empty,
                            r.BudgetPurposeName ?? string.Empty
                        });
                    }

                    var maxLengths = headers.Select(h => h.Length).ToArray();
                    foreach (var dataRow in rowsData)
                    {
                        UpdateMaxLengths(maxLengths, dataRow);
                    }

                    var worksheet = new DocumentFormat.OpenXml.Spreadsheet.Worksheet();
                    worksheet.Append(CreateAutoSizedColumns(maxLengths));
                    worksheet.Append(sheetData);
                    worksheetPart.Worksheet = worksheet;

                    var headerRow = new DocumentFormat.OpenXml.Spreadsheet.Row();
                    for (int i = 0; i < headers.Length; i++)
                    {
                        var idx = InsertSharedString(headers[i]);
                        var style = i == 2 ? HeaderRightStyleIndex : HeaderStyleIndex;
                        var cell = new DocumentFormat.OpenXml.Spreadsheet.Cell()
                        {
                            DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.SharedString,
                            CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue(idx >= 0 ? idx.ToString() : string.Empty),
                            StyleIndex = style
                        };
                        headerRow.Append(cell);
                    }
                    sheetData.Append(headerRow);

                    foreach (var dataRow in rowsData)
                    {
                        var row = new DocumentFormat.OpenXml.Spreadsheet.Row();
                        // BookingDate
                        var bIdx = InsertSharedString(dataRow[0] ?? string.Empty);
                        row.Append(new DocumentFormat.OpenXml.Spreadsheet.Cell() { DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.SharedString, CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue(bIdx.ToString()) });
                        // ValutaDate
                        var vIdx = InsertSharedString(dataRow[1] ?? string.Empty);
                        row.Append(new DocumentFormat.OpenXml.Spreadsheet.Cell() { DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.SharedString, CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue(vIdx.ToString()) });
                        // Amount as number
                        row.Append(new DocumentFormat.OpenXml.Spreadsheet.Cell() { CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue(dataRow[2] ?? string.Empty), DataType = null, StyleIndex = RightStyleIndex });
                        // Subject/Description
                        var subj = InsertSharedString(dataRow[3] ?? string.Empty);
                        row.Append(new DocumentFormat.OpenXml.Spreadsheet.Cell() { DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.SharedString, CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue(subj.ToString()) });
                        // Contact
                        var cidx = InsertSharedString(dataRow[4] ?? string.Empty);
                        row.Append(new DocumentFormat.OpenXml.Spreadsheet.Cell() { DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.SharedString, CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue(cidx.ToString()) });
                        // SavingsPlan
                        var sidx = InsertSharedString(dataRow[5] ?? string.Empty);
                        row.Append(new DocumentFormat.OpenXml.Spreadsheet.Cell() { DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.SharedString, CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue(sidx.ToString()) });
                        // Account
                        var aidx = InsertSharedString(dataRow[6] ?? string.Empty);
                        row.Append(new DocumentFormat.OpenXml.Spreadsheet.Cell() { DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.SharedString, CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue(aidx.ToString()) });
                        // Category
                        var catIdx = InsertSharedString(dataRow[7] ?? string.Empty);
                        row.Append(new DocumentFormat.OpenXml.Spreadsheet.Cell() { DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.SharedString, CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue(catIdx.ToString()) });
                        // Purpose
                        var purIdx = InsertSharedString(dataRow[8] ?? string.Empty);
                        row.Append(new DocumentFormat.OpenXml.Spreadsheet.Cell() { DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.SharedString, CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue(purIdx.ToString()) });

                        sheetData.Append(row);
                    }

                    var sheet = new DocumentFormat.OpenXml.Spreadsheet.Sheet()
                    {
                        Id = workbookPart.GetIdOfPart(worksheetPart),
                        SheetId = sheetId++,
                        Name = name
                    };
                    sheets.Append(sheet);
                }

                var monthlySheetName = L("Budget_Report_Export_Sheet_MonthlyOverview", "MonthlyOverview");
                var currentMonthSheetName = L("Budget_Report_Export_Sheet_CurrentMonth", "CurrentMonth");
                var contactSheetName = L("Budget_Report_Export_Sheet_ContactPostings", "ContactPostings");

                var monthlyHeaders = new[]
                {
                    L("Budget_Report_Export_Header_Period", "Period"),
                    L("Budget_Report_Export_Header_Budget", "Budget"),
                    L("Budget_Report_Export_Header_Actual", "Actual"),
                    L("Budget_Report_Export_Header_Delta", "Delta"),
                    L("Budget_Report_Export_Header_DeltaPct", "Delta %")
                };
                var currentMonthHeaders = new[]
                {
                    L("Budget_Report_Export_Header_Category", "Category"),
                    L("Budget_Report_Export_Header_Purpose", "Purpose"),
                    L("Budget_Report_Export_Header_Budget", "Budget"),
                    L("Budget_Report_Export_Header_Actual", "Actual"),
                    L("Budget_Report_Export_Header_Delta", "Delta"),
                    L("Budget_Report_Export_Header_DeltaPct", "Delta %")
                };

                CreateMonthlyOverviewSheet(
                    workbookPart,
                    sheets,
                    sstPart,
                    InsertSharedString,
                    ref sheetId,
                    monthlySheetName,
                    monthlyHeaders,
                    culture,
                    periods);
                CreateCurrentMonthSheet(workbookPart, sheets, sstPart, InsertSharedString, ref sheetId, currentMonthSheetName, currentMonthHeaders, currentMonthRows);

                var contactRows = postings.Where(p => p.PostingKind == PostingKind.Contact).ToList();
                if (contactRows.Count > 0)
                {
                    CreatePostingSheet(contactSheetName, contactRows);
                }

                workbookPart.Workbook.Save();
            }

            ms.Position = 0;
            return ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"budget-report-{request.AsOfDate:yyyy-MM-dd}.xlsx", (Stream)ms);
        }

        private static IReadOnlyList<BudgetReportPeriodDto> BuildPeriods(
            DateOnly from,
            int months,
            BudgetReportRawDataDto raw,
            IReadOnlyList<BudgetRule> rules,
            BudgetReportDateBasis dateBasis)
        {
            var periods = new List<BudgetReportPeriodDto>(months);
            for (int i = 0; i < months; i++)
            {
                var periodFrom = new DateOnly(from.Year, from.Month, 1).AddMonths(i);
                var periodTo = new DateOnly(periodFrom.Year, periodFrom.Month, DateTime.DaysInMonth(periodFrom.Year, periodFrom.Month));

                decimal actual = 0m;

                foreach (var cat in raw.Categories ?? Array.Empty<BudgetReportCategoryRawDataDto>())
                {
                    foreach (var pur in cat.Purposes ?? Array.Empty<BudgetReportPurposeRawDataDto>())
                    {
                        foreach (var p in (pur.Postings ?? Array.Empty<BudgetReportPostingRawDataDto>()).Where(p => p.IsValuedForBudgetPurpose))
                        {
                            var dd = DateOnly.FromDateTime(GetPostingDate(p, dateBasis));
                            if (dd >= periodFrom && dd <= periodTo)
                            {
                                actual += p.Amount;
                            }
                        }
                    }
                }

                foreach (var pur in raw.UncategorizedPurposes ?? Array.Empty<BudgetReportPurposeRawDataDto>())
                {
                    foreach (var p in (pur.Postings ?? Array.Empty<BudgetReportPostingRawDataDto>()).Where(p => p.IsValuedForBudgetPurpose))
                    {
                        var dd = DateOnly.FromDateTime(GetPostingDate(p, dateBasis));
                        if (dd >= periodFrom && dd <= periodTo)
                        {
                            actual += p.Amount;
                        }
                    }
                }

                foreach (var p in raw.UnbudgetedPostings ?? Array.Empty<BudgetReportPostingRawDataDto>())
                {
                    var dd = DateOnly.FromDateTime(GetPostingDate(p, dateBasis));
                    if (dd >= periodFrom && dd <= periodTo)
                    {
                        actual += p.Amount;
                    }
                }

                var budget = ComputeBudgetedAmountForPeriod(rules, periodFrom, periodTo);
                var delta = ComputeDelta(budget, actual);
                var deltaPct = ComputeDeltaPct(budget, delta);
                periods.Add(new BudgetReportPeriodDto(periodFrom, periodTo, budget, actual, delta, deltaPct));
            }

            return periods;
        }

        private static IReadOnlyList<CurrentMonthRow> BuildCurrentMonthRows(
            BudgetReportRawDataDto raw,
            IReadOnlyList<BudgetRule> rules,
            BudgetReportDateBasis dateBasis,
            DateOnly from,
            DateOnly to,
            string unbudgetedLabel)
        {
            var result = new List<CurrentMonthRow>();
            var unbudgetedPostings = raw.UnbudgetedPostings ?? Array.Empty<BudgetReportPostingRawDataDto>();
            foreach (var cat in raw.Categories ?? Array.Empty<BudgetReportCategoryRawDataDto>())
            {
                var catRules = rules.Where(r => r.BudgetCategoryId == cat.CategoryId).ToList();
                var catBudget = ComputeBudgetedAmountForPeriod(catRules, from, to);
                var catActual = 0m;
                var purposeRows = new List<CurrentMonthRow>();
                foreach (var pur in cat.Purposes ?? Array.Empty<BudgetReportPurposeRawDataDto>())
                {
                    foreach (var p in (pur.Postings ?? Array.Empty<BudgetReportPostingRawDataDto>()).Where(p => p.IsValuedForBudgetPurpose))
                    {
                        var dd = DateOnly.FromDateTime(GetPostingDate(p, dateBasis));
                        if (dd >= from && dd <= to)
                        {
                            catActual += p.Amount;
                        }
                    }
                }

                foreach (var p in unbudgetedPostings.Where(p => p.BudgetCategoryId == cat.CategoryId))
                {
                    var dd = DateOnly.FromDateTime(GetPostingDate(p, dateBasis));
                    if (dd >= from && dd <= to)
                    {
                        catActual += p.Amount;
                    }
                }

                foreach (var pur in cat.Purposes ?? Array.Empty<BudgetReportPurposeRawDataDto>())
                {
                    var purRules = rules.Where(r => r.BudgetPurposeId == pur.PurposeId).ToList();
                    var purBudget = ComputeBudgetedAmountForPeriod(purRules, from, to);
                    catBudget += purBudget;
                    var purActual = 0m;
                    foreach (var p in (pur.Postings ?? Array.Empty<BudgetReportPostingRawDataDto>()).Where(p => p.IsValuedForBudgetPurpose))
                    {
                        var dd = DateOnly.FromDateTime(GetPostingDate(p, dateBasis));
                        if (dd >= from && dd <= to)
                        {
                            purActual += p.Amount;
                        }
                    }

                    foreach (var p in unbudgetedPostings.Where(p => p.BudgetPurposeId == pur.PurposeId))
                    {
                        var dd = DateOnly.FromDateTime(GetPostingDate(p, dateBasis));
                        if (dd >= from && dd <= to)
                        {
                            purActual += p.Amount;
                        }
                    }

                    purposeRows.Add(CurrentMonthRow.CreatePurpose(cat.CategoryName, pur.PurposeName, purBudget, purActual));
                }

                result.Add(CurrentMonthRow.CreateCategory(cat.CategoryName, catBudget, catActual));
                result.AddRange(purposeRows);
            }

            var unbudgetedActual = unbudgetedPostings
                .Where(p => !p.BudgetPurposeId.HasValue && !p.BudgetCategoryId.HasValue)
                .Where(p =>
                {
                    var dd = DateOnly.FromDateTime(GetPostingDate(p, dateBasis));
                    return dd >= from && dd <= to;
                })
                .Sum(p => p.Amount);

            if (unbudgetedActual != 0m)
            {
                result.Add(CurrentMonthRow.CreateCategory(unbudgetedLabel, 0m, unbudgetedActual));
            }

            return result;
        }

        private static DateTime GetPostingDate(BudgetReportPostingRawDataDto posting, BudgetReportDateBasis dateBasis)
            => dateBasis == BudgetReportDateBasis.ValutaDate ? (posting.ValutaDate ?? posting.BookingDate) : posting.BookingDate;

        private static decimal ComputeDelta(decimal budget, decimal actual)
            => actual - budget;

        private static decimal ComputeDeltaPct(decimal budget, decimal delta)
            => budget == 0m ? 0m : delta / Math.Abs(budget);

        private static decimal ComputeBudgetedAmountForPeriod(IReadOnlyList<BudgetRule> rules, DateOnly from, DateOnly to)
        {
            if (rules == null || rules.Count == 0)
            {
                return 0m;
            }

            decimal sum = 0m;
            foreach (var rule in rules)
            {
                var step = rule.Interval switch
                {
                    BudgetIntervalType.Monthly => 1,
                    BudgetIntervalType.Quarterly => 3,
                    BudgetIntervalType.Yearly => 12,
                    BudgetIntervalType.CustomMonths => rule.CustomIntervalMonths ?? 1,
                    _ => 1
                };

                var occ = rule.StartDate;
                var ruleEnd = rule.EndDate ?? to;

                while (occ < from)
                {
                    occ = occ.AddMonths(step);
                    if (occ > ruleEnd)
                    {
                        break;
                    }
                }

                while (occ <= to && occ <= ruleEnd)
                {
                    sum += rule.Amount;
                    occ = occ.AddMonths(step);
                }
            }

            return sum;
        }

        private static void CreateMonthlyOverviewSheet(
            WorkbookPart workbookPart,
            Sheets sheets,
            SharedStringTablePart sstPart,
            Func<string, int> insertSharedString,
            ref uint sheetId,
            string sheetName,
            IReadOnlyList<string> headers,
            CultureInfo culture,
            IReadOnlyList<BudgetReportPeriodDto> periods)
        {
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            var maxLengths = headers.Select(h => h.Length).ToArray();

            var periodRows = new List<string[]>(periods.Count);
            foreach (var period in periods)
            {
                var periodText = period.From.ToString("d", culture);
                var budgetText = period.Budget.ToString(CultureInfo.InvariantCulture);
                var actualText = period.Actual.ToString(CultureInfo.InvariantCulture);
                var deltaText = period.Delta.ToString(CultureInfo.InvariantCulture);
                var deltaPctValue = Math.Round(period.DeltaPct * 100m, 0, MidpointRounding.AwayFromZero);
                var deltaPctText = deltaPctValue.ToString("0", CultureInfo.InvariantCulture);
                var values = new[] { periodText, budgetText, actualText, deltaText, deltaPctText };
                periodRows.Add(values);
                UpdateMaxLengths(maxLengths, values);
            }

            var worksheet = new Worksheet();
            worksheet.Append(CreateAutoSizedColumns(maxLengths));
            worksheet.Append(sheetData);
            worksheetPart.Worksheet = worksheet;

            var headerRow = new Row();
            foreach (var header in headers)
            {
                var idx = insertSharedString(header);
                var style = header == headers[0] ? HeaderStyleIndex : HeaderRightStyleIndex;
                headerRow.Append(new Cell
                {
                    DataType = CellValues.SharedString,
                    CellValue = new CellValue(idx.ToString()),
                    StyleIndex = style
                });
            }
            sheetData.Append(headerRow);

            foreach (var values in periodRows)
            {
                var row = new Row();
                var pidx = insertSharedString(values[0]);
                row.Append(new Cell { DataType = CellValues.SharedString, CellValue = new CellValue(pidx.ToString()) });
                row.Append(new Cell { CellValue = new CellValue(values[1]), StyleIndex = RightStyleIndex });
                row.Append(new Cell { CellValue = new CellValue(values[2]), StyleIndex = RightStyleIndex });
                row.Append(new Cell { CellValue = new CellValue(values[3]), StyleIndex = RightStyleIndex });
                row.Append(new Cell { CellValue = new CellValue(values[4]), StyleIndex = RightStyleIndex });
                sheetData.Append(row);
            }

            var drawingsPart = worksheetPart.AddNewPart<DrawingsPart>();
            worksheet.Append(new Drawing { Id = worksheetPart.GetIdOfPart(drawingsPart) });
            drawingsPart.WorksheetDrawing = new WorksheetDrawing();

            var chartPart = drawingsPart.AddNewPart<ChartPart>();
            chartPart.ChartSpace = new ChartSpace();
            chartPart.ChartSpace.Append(new EditingLanguage { Val = "en-US" });
            var chart = chartPart.ChartSpace.AppendChild(new DocumentFormat.OpenXml.Drawing.Charts.Chart());
            var plotArea = chart.AppendChild(new PlotArea());
            plotArea.AppendChild(new Layout());

            const uint categoryAxisId = 48650112u;
            const uint valueAxisId = 48672768u;

            var lineChart = plotArea.AppendChild(new LineChart());
            lineChart.AppendChild(new Grouping { Val = GroupingValues.Standard });
            lineChart.AppendChild(new VaryColors { Val = false });

            AddLineSeries(lineChart, sheetName, 1, "B", periods.Count + 1, "A", periods.Count + 1, "1F77B4");
            AddLineSeries(lineChart, sheetName, 2, "C", periods.Count + 1, "A", periods.Count + 1, "2CA02C");

            lineChart.Append(new AxisId { Val = categoryAxisId });
            lineChart.Append(new AxisId { Val = valueAxisId });

            var catAxis = plotArea.AppendChild(new CategoryAxis());
            catAxis.Append(new AxisId { Val = categoryAxisId });
            catAxis.Append(new Scaling { Orientation = new Orientation { Val = DocumentFormat.OpenXml.Drawing.Charts.OrientationValues.MinMax } });
            catAxis.Append(new AxisPosition { Val = AxisPositionValues.Bottom });
            catAxis.Append(new TickLabelPosition { Val = TickLabelPositionValues.Low });
            catAxis.Append(new LabelOffset { Val = 100 });
            catAxis.Append(new CrossingAxis { Val = valueAxisId });

            var valAxis = plotArea.AppendChild(new ValueAxis());
            valAxis.Append(new AxisId { Val = valueAxisId });
            valAxis.Append(new Scaling { Orientation = new Orientation { Val = DocumentFormat.OpenXml.Drawing.Charts.OrientationValues.MinMax } });
            valAxis.Append(new AxisPosition { Val = AxisPositionValues.Left });
            valAxis.Append(new TickLabelPosition { Val = TickLabelPositionValues.NextTo });
            valAxis.Append(new MajorGridlines());
            valAxis.Append(new CrossingAxis { Val = categoryAxisId });

            chart.Append(new PlotVisibleOnly { Val = true });
            chart.Append(new Legend(new LegendPosition { Val = LegendPositionValues.Right }, new Layout()));

            var graphicFrame = new DocumentFormat.OpenXml.Drawing.Spreadsheet.GraphicFrame(
                new DocumentFormat.OpenXml.Drawing.Spreadsheet.NonVisualGraphicFrameProperties(
                    new DocumentFormat.OpenXml.Drawing.Spreadsheet.NonVisualDrawingProperties { Id = 2u, Name = "Chart 1" },
                    new DocumentFormat.OpenXml.Drawing.Spreadsheet.NonVisualGraphicFrameDrawingProperties()),
                new Transform(
                    new Offset { X = 0L, Y = 0L },
                    new Extents { Cx = 0L, Cy = 0L }),
                new Graphic(new GraphicData(new ChartReference { Id = drawingsPart.GetIdOfPart(chartPart) })
                {
                    Uri = "http://schemas.openxmlformats.org/drawingml/2006/chart"
                }));

            var startRow = periods.Count + 2;
            var endRow = startRow + 14;
            drawingsPart.WorksheetDrawing.Append(
                new TwoCellAnchor(
                    new DocumentFormat.OpenXml.Drawing.Spreadsheet.FromMarker(new ColumnId("0"), new ColumnOffset("0"), new RowId(startRow.ToString(CultureInfo.InvariantCulture)), new RowOffset("0")),
                    new DocumentFormat.OpenXml.Drawing.Spreadsheet.ToMarker(new ColumnId("7"), new ColumnOffset("0"), new RowId(endRow.ToString(CultureInfo.InvariantCulture)), new RowOffset("0")),
                    graphicFrame,
                    new ClientData()));

            var sheet = new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = sheetId++,
                Name = sheetName
            };
            sheets.Append(sheet);
        }

        private static void AddLineSeries(LineChart chart, string sheetName, uint index, string valueColumn, int valueRowEnd, string categoryColumn, int categoryRowEnd, string colorHex)
        {
            var sheetRef = GetSheetReference(sheetName);
            var series = chart.AppendChild(new LineChartSeries(
                new DocumentFormat.OpenXml.Drawing.Charts.Index { Val = index },
                new Order { Val = index }));

            series.Append(new SeriesText(new StringReference
            {
                Formula = new DocumentFormat.OpenXml.Drawing.Charts.Formula { Text = $"{sheetRef}!${valueColumn}$1" }
            }));

            series.Append(new ChartShapeProperties(
                new DocumentFormat.OpenXml.Drawing.Outline(
                    new DocumentFormat.OpenXml.Drawing.SolidFill(new DocumentFormat.OpenXml.Drawing.RgbColorModelHex { Val = colorHex }))
                {
                    Width = 19050
                }));

            series.Append(new Marker(new Symbol { Val = MarkerStyleValues.None }));

            series.Append(new CategoryAxisData(new StringReference
            {
                Formula = new DocumentFormat.OpenXml.Drawing.Charts.Formula { Text = $"{sheetRef}!${categoryColumn}$2:${categoryColumn}${categoryRowEnd}" }
            }));

            series.Append(new DocumentFormat.OpenXml.Drawing.Charts.Values(new NumberReference
            {
                Formula = new DocumentFormat.OpenXml.Drawing.Charts.Formula { Text = $"{sheetRef}!${valueColumn}$2:${valueColumn}${valueRowEnd}" }
            }));
        }

        private static void CreateCurrentMonthSheet(
            WorkbookPart workbookPart,
            Sheets sheets,
            SharedStringTablePart sstPart,
            Func<string, int> insertSharedString,
            ref uint sheetId,
            string sheetName,
            IReadOnlyList<string> headers,
            IReadOnlyList<CurrentMonthRow> rows)
        {
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            var maxLengths = headers.Select(h => h.Length).ToArray();

            var dataRows = new List<string[]>(rows.Count);
            foreach (var row in rows)
            {
                var budgetText = row.Budget.ToString(CultureInfo.InvariantCulture);
                var actualText = row.Actual.ToString(CultureInfo.InvariantCulture);
                var deltaText = row.Delta.ToString(CultureInfo.InvariantCulture);
                var deltaPctValue = Math.Round(row.DeltaPct * 100m, 0, MidpointRounding.AwayFromZero);
                var deltaPctText = deltaPctValue.ToString("0", CultureInfo.InvariantCulture);
                var values = new[]
                {
                    row.Category ?? string.Empty,
                    row.Purpose ?? string.Empty,
                    budgetText,
                    actualText,
                    deltaText,
                    deltaPctText
                };
                dataRows.Add(values);
                UpdateMaxLengths(maxLengths, values);
            }

            var worksheet = new Worksheet();
            worksheet.Append(CreateAutoSizedColumns(maxLengths));
            worksheet.Append(sheetData);
            worksheetPart.Worksheet = worksheet;

            var headerRow = new Row();
            foreach (var header in headers)
            {
                var idx = insertSharedString(header);
                var style = header == headers[0] || header == headers[1] ? HeaderStyleIndex : HeaderRightStyleIndex;
                headerRow.Append(new Cell { DataType = CellValues.SharedString, CellValue = new CellValue(idx.ToString()), StyleIndex = style });
            }
            sheetData.Append(headerRow);

            foreach (var values in dataRows)
            {
                var dataRow = new Row();
                var catIdx = insertSharedString(values[0]);
                dataRow.Append(new Cell { DataType = CellValues.SharedString, CellValue = new CellValue(catIdx.ToString()) });
                var purIdx = insertSharedString(values[1]);
                dataRow.Append(new Cell { DataType = CellValues.SharedString, CellValue = new CellValue(purIdx.ToString()) });
                dataRow.Append(new Cell { CellValue = new CellValue(values[2]), StyleIndex = RightStyleIndex });
                dataRow.Append(new Cell { CellValue = new CellValue(values[3]), StyleIndex = RightStyleIndex });
                dataRow.Append(new Cell { CellValue = new CellValue(values[4]), StyleIndex = RightStyleIndex });
                dataRow.Append(new Cell { CellValue = new CellValue(values[5]), StyleIndex = RightStyleIndex });
                sheetData.Append(dataRow);
            }

            var sheet = new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = sheetId++,
                Name = sheetName
            };
            sheets.Append(sheet);
        }

        private static void UpdateMaxLengths(int[] maxLengths, IReadOnlyList<string?> values)
        {
            for (var i = 0; i < maxLengths.Length && i < values.Count; i++)
            {
                var length = values[i]?.Length ?? 0;
                if (length > maxLengths[i])
                {
                    maxLengths[i] = length;
                }
            }
        }

        private static Columns CreateAutoSizedColumns(IReadOnlyList<int> maxLengths)
        {
            var columns = new Columns();
            for (var i = 0; i < maxLengths.Count; i++)
            {
                var width = CalculateColumnWidth(maxLengths[i]);
                columns.Append(new Column
                {
                    Min = (uint)(i + 1),
                    Max = (uint)(i + 1),
                    Width = width,
                    CustomWidth = true
                });
            }

            return columns;
        }

        private static double CalculateColumnWidth(int maxLength)
        {
            var clamped = Math.Clamp(maxLength + 2, 8, 60);
            return Convert.ToDouble(clamped, CultureInfo.InvariantCulture);
        }

        private static string GetSheetReference(string sheetName)
        {
            var escaped = sheetName.Replace("'", "''");
            return $"'{escaped}'";
        }

        private static Stylesheet CreateStylesheet()
        {
            var fonts = new DocumentFormat.OpenXml.Spreadsheet.Fonts(
                new DocumentFormat.OpenXml.Spreadsheet.Font(),
                new DocumentFormat.OpenXml.Spreadsheet.Font(new DocumentFormat.OpenXml.Spreadsheet.Bold()));

            var fills = new DocumentFormat.OpenXml.Spreadsheet.Fills(
                new DocumentFormat.OpenXml.Spreadsheet.Fill(new DocumentFormat.OpenXml.Spreadsheet.PatternFill { PatternType = PatternValues.None }),
                new DocumentFormat.OpenXml.Spreadsheet.Fill(new DocumentFormat.OpenXml.Spreadsheet.PatternFill { PatternType = PatternValues.Gray125 }));

            var borders = new Borders(
                new Border(),
                new Border(
                    new DocumentFormat.OpenXml.Spreadsheet.LeftBorder(),
                    new DocumentFormat.OpenXml.Spreadsheet.RightBorder(),
                    new DocumentFormat.OpenXml.Spreadsheet.TopBorder(),
                    new DocumentFormat.OpenXml.Spreadsheet.BottomBorder(new DocumentFormat.OpenXml.Spreadsheet.Color { Auto = true }) { Style = BorderStyleValues.Thin },
                    new DocumentFormat.OpenXml.Spreadsheet.DiagonalBorder()));

            var cellStyleFormats = new CellStyleFormats(new CellFormat());

            var cellFormats = new CellFormats(
                new CellFormat { FontId = 0, FillId = 0, BorderId = 0, ApplyFont = true },
                new CellFormat { FontId = 1, FillId = 0, BorderId = 1, ApplyFont = true, ApplyBorder = true, ApplyAlignment = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Left } },
                new CellFormat { FontId = 1, FillId = 0, BorderId = 1, ApplyFont = true, ApplyBorder = true, ApplyAlignment = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right } },
                new CellFormat { FontId = 0, FillId = 0, BorderId = 0, ApplyAlignment = true, Alignment = new Alignment { Horizontal = HorizontalAlignmentValues.Right } });

            return new Stylesheet(fonts, fills, borders, cellStyleFormats, cellFormats);
        }

        private sealed record CurrentMonthRow(string? Category, string? Purpose, decimal Budget, decimal Actual, decimal Delta, decimal DeltaPct)
        {
            public static CurrentMonthRow CreateCategory(string? name, decimal budget, decimal actual)
            {
                var delta = ComputeDelta(budget, actual);
                var pct = ComputeDeltaPct(budget, delta);
                return new CurrentMonthRow(name, null, budget, actual, delta, pct);
            }

            public static CurrentMonthRow CreatePurpose(string? category, string? purpose, decimal budget, decimal actual)
            {
                var delta = ComputeDelta(budget, actual);
                var pct = ComputeDeltaPct(budget, delta);
                return new CurrentMonthRow(category, purpose, budget, actual, delta, pct);
            }
        }
    }
}
