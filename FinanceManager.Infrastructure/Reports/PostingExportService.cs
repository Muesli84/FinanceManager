using System.Buffers;
using System.Globalization;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FinanceManager.Application.Reports;
using FinanceManager.Domain;
using FinanceManager.Domain.Postings;
using FinanceManager.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinanceManager.Infrastructure.Reports;

public sealed class PostingExportService : IPostingExportService
{
    private readonly AppDbContext _db;
    private readonly ILogger<PostingExportService> _logger;

    public PostingExportService(AppDbContext db, ILogger<PostingExportService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async IAsyncEnumerable<PostingExportRow> QueryAsync(PostingExportQuery query, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var baseQuery = BuildFilteredJoinedQuery(query);

        var ordered = baseQuery
            .OrderByDescending(x => x.P.BookingDate)
            .ThenByDescending(x => x.P.Id)
            .Take(query.MaxRows + 1); // overtake to detect overflow

        await foreach (var row in ordered
            .Select(x => new PostingExportRow(
                x.P.BookingDate,
                x.P.ValutaDate,
                x.P.Amount,
                x.P.Kind,
                x.Subject,
                x.Recipient,
                x.Description,
                x.P.AccountId,
                x.P.ContactId,
                x.P.SavingsPlanId,
                x.P.SecurityId,
                x.P.SecuritySubType,
                x.P.Quantity))
            .AsAsyncEnumerable().WithCancellation(ct))
        {
            yield return row;
        }
    }

    // Centralizes building the filtered base query used by count/query
    private IQueryable<Joined> BuildFilteredJoinedQuery(PostingExportQuery query)
    {
        var baseQuery = BuildBaseQuery(query);

        // Apply filters
        if (query.From.HasValue)
        {
            var from = query.From.Value.Date;
            baseQuery = baseQuery.Where(x => x.P.BookingDate >= from);
        }
        if (query.To.HasValue)
        {
            var to = query.To.Value.Date.AddDays(1);
            baseQuery = baseQuery.Where(x => x.P.BookingDate < to);
        }
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            var termLower = term.ToLowerInvariant();
            baseQuery = baseQuery.Where(x =>
                (x.Subject != null && EF.Functions.Like(x.Subject.ToLower(), "%" + termLower + "%")) ||
                (x.Recipient != null && EF.Functions.Like(x.Recipient.ToLower(), "%" + termLower + "%")) ||
                (x.Description != null && EF.Functions.Like(x.Description.ToLower(), "%" + termLower + "%"))
            );
        }
        return baseQuery;
    }

    private IQueryable<Joined> BuildBaseQuery(PostingExportQuery query)
    {
        // Ownership per context
        switch (query.ContextKind)
        {
            case PostingKind.Bank:
                if (!_db.Accounts.AsNoTracking().Any(a => a.Id == query.ContextId && a.OwnerUserId == query.OwnerUserId))
                {
                    throw new UnauthorizedAccessException("Account not owned by user.");
                }
                break;
            case PostingKind.Contact:
                if (!_db.Contacts.AsNoTracking().Any(c => c.Id == query.ContextId && c.OwnerUserId == query.OwnerUserId))
                {
                    throw new UnauthorizedAccessException("Contact not owned by user.");
                }
                break;
            case PostingKind.SavingsPlan:
                if (!_db.SavingsPlans.AsNoTracking().Any(s => s.Id == query.ContextId && s.OwnerUserId == query.OwnerUserId))
                {
                    throw new UnauthorizedAccessException("Savings plan not owned by user.");
                }
                break;
            case PostingKind.Security:
                if (!_db.Securities.AsNoTracking().Any(s => s.Id == query.ContextId && s.OwnerUserId == query.OwnerUserId))
                {
                    throw new UnauthorizedAccessException("Security not owned by user.");
                }
                break;
            default:
                throw new InvalidOperationException("Unsupported context kind for export.");
        }

        var postings = _db.Postings.AsNoTracking().Where(p => p.Kind == query.ContextKind);
        postings = query.ContextKind switch
        {
            PostingKind.Bank => postings.Where(p => p.AccountId == query.ContextId),
            PostingKind.Contact => postings.Where(p => p.ContactId == query.ContextId),
            PostingKind.SavingsPlan => postings.Where(p => p.SavingsPlanId == query.ContextId),
            PostingKind.Security => postings.Where(p => p.SecurityId == query.ContextId),
            _ => throw new InvalidOperationException()
        };

        var joined = from p in postings
                     join se in _db.StatementEntries.AsNoTracking() on p.SourceId equals se.Id into seJoin
                     from seOpt in seJoin.DefaultIfEmpty()
                     select new Joined
                     {
                         P = p,
                         Subject = p.Subject ?? seOpt.Subject,
                         Recipient = p.RecipientName ?? seOpt.RecipientName,
                         Description = p.Description ?? seOpt.BookingDescription
                     };
        return joined;
    }

    private sealed class Joined
    {
        public Domain.Postings.Posting P { get; set; } = null!;
        public string? Subject { get; set; }
        public string? Recipient { get; set; }
        public string? Description { get; set; }
    }

    public async Task<(string ContentType, string FileName, Stream Content)> GenerateAsync(PostingExportQuery query, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rows = new List<PostingExportRow>(capacity: Math.Min(query.MaxRows, 10000));
        var count = 0;
        await foreach (var r in QueryAsync(query, ct))
        {
            rows.Add(r);
            count++;
            if (count > query.MaxRows)
            {
                throw new InvalidOperationException("MaxRowsExceeded");
            }
        }

        // Resolve entity names for referenced IDs to make export human-readable
        var accountIds = rows.Where(r => r.AccountId.HasValue).Select(r => r.AccountId!.Value).Distinct().ToList();
        var contactIds = rows.Where(r => r.ContactId.HasValue).Select(r => r.ContactId!.Value).Distinct().ToList();
        var savingsIds = rows.Where(r => r.SavingsPlanId.HasValue).Select(r => r.SavingsPlanId!.Value).Distinct().ToList();
        var securityIds = rows.Where(r => r.SecurityId.HasValue).Select(r => r.SecurityId!.Value).Distinct().ToList();

        var accountNames = accountIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Accounts.AsNoTracking()
                .Where(a => accountIds.Contains(a.Id) && a.OwnerUserId == query.OwnerUserId)
                .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

        var contactNames = contactIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Contacts.AsNoTracking()
                .Where(c => contactIds.Contains(c.Id) && c.OwnerUserId == query.OwnerUserId)
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var savingsNames = savingsIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.SavingsPlans.AsNoTracking()
                .Where(s => savingsIds.Contains(s.Id) && s.OwnerUserId == query.OwnerUserId)
                .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var securityNames = securityIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Securities.AsNoTracking()
                .Where(s => securityIds.Contains(s.Id) && s.OwnerUserId == query.OwnerUserId)
                .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        Stream stream;
        string contentType;
        string ext;
        if (query.Format == PostingExportFormat.Csv)
        {
            stream = new MemoryStream();
            await WriteCsvAsync(stream, rows, accountNames, contactNames, savingsNames, securityNames, ct);
            stream.Position = 0;
            contentType = "text/csv";
            ext = "csv";
        }
        else if (query.Format == PostingExportFormat.Xlsx)
        {
            stream = new MemoryStream();
            WriteXlsx(stream, rows, accountNames, contactNames, savingsNames, securityNames);
            stream.Position = 0;
            contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            ext = "xlsx";
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(query.Format));
        }

        sw.Stop();
        _logger.LogInformation("Posting export generated: Kind={Kind}, Id={Id}, Rows={Rows}, Format={Format}, DurationMs={Ms}", query.ContextKind, query.ContextId, rows.Count, query.Format, sw.ElapsedMilliseconds);

        // Filename responsibility of caller (needs entity name). Use generic fallback here
        var fileName = $"Postings_{query.ContextKind}_{DateTime.UtcNow:yyyyMMddHHmm}.{ext}";
        return (contentType, fileName, stream);
    }

    public async Task<int> CountAsync(PostingExportQuery query, CancellationToken ct)
    {
        var filtered = BuildFilteredJoinedQuery(query);
        return await filtered.CountAsync(ct);
    }

    public async Task StreamCsvAsync(PostingExportQuery query, Stream output, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Build a streaming query that also resolves names via left joins filtered by owner
        var postings = _db.Postings.AsNoTracking().Where(p => p.Kind == query.ContextKind);
        postings = query.ContextKind switch
        {
            PostingKind.Bank => postings.Where(p => p.AccountId == query.ContextId),
            PostingKind.Contact => postings.Where(p => p.ContactId == query.ContextId),
            PostingKind.SavingsPlan => postings.Where(p => p.SavingsPlanId == query.ContextId),
            PostingKind.Security => postings.Where(p => p.SecurityId == query.ContextId),
            _ => postings.Where(_ => false)
        };

        // Ownership checks already enforced in BuildBaseQuery in regular paths; do it again for safety
        _ = BuildBaseQuery(query);

        var accounts = _db.Accounts.AsNoTracking().Where(a => a.OwnerUserId == query.OwnerUserId);
        var contacts = _db.Contacts.AsNoTracking().Where(c => c.OwnerUserId == query.OwnerUserId);
        var savings = _db.SavingsPlans.AsNoTracking().Where(s => s.OwnerUserId == query.OwnerUserId);
        var securities = _db.Securities.AsNoTracking().Where(s => s.OwnerUserId == query.OwnerUserId);
        var stmt = _db.StatementEntries.AsNoTracking();

        var q = from p in postings
                join se in stmt on p.SourceId equals se.Id into seJoin
                from seOpt in seJoin.DefaultIfEmpty()
                join ac in accounts on p.AccountId equals ac.Id into acJoin
                from acOpt in acJoin.DefaultIfEmpty()
                join co in contacts on p.ContactId equals co.Id into coJoin
                from coOpt in coJoin.DefaultIfEmpty()
                join sp in savings on p.SavingsPlanId equals sp.Id into spJoin
                from spOpt in spJoin.DefaultIfEmpty()
                join sc in securities on p.SecurityId equals sc.Id into scJoin
                from scOpt in scJoin.DefaultIfEmpty()
                select new
                {
                    p.Id,
                    p.BookingDate,
                    p.ValutaDate,
                    p.Amount,
                    p.Kind,
                    Subject = p.Subject ?? seOpt.Subject,
                    Recipient = p.RecipientName ?? seOpt.RecipientName,
                    Description = p.Description ?? seOpt.BookingDescription,
                    AccountName = acOpt != null ? acOpt.Name : null,
                    ContactName = coOpt != null ? coOpt.Name : null,
                    SavingsName = spOpt != null ? spOpt.Name : null,
                    SecurityName = scOpt != null ? scOpt.Name : null,
                    p.SecuritySubType,
                    p.Quantity
                };

        // Filters
        if (query.From.HasValue)
        {
            var from = query.From.Value.Date;
            q = q.Where(x => x.BookingDate >= from);
        }
        if (query.To.HasValue)
        {
            var to = query.To.Value.Date.AddDays(1);
            q = q.Where(x => x.BookingDate < to);
        }
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            var termLower = term.ToLowerInvariant();
            q = q.Where(x =>
                (x.Subject != null && EF.Functions.Like(x.Subject.ToLower(), "%" + termLower + "%")) ||
                (x.Recipient != null && EF.Functions.Like(x.Recipient.ToLower(), "%" + termLower + "%")) ||
                (x.Description != null && EF.Functions.Like(x.Description.ToLower(), "%" + termLower + "%"))
            );
        }

        q = q.OrderByDescending(x => x.BookingDate).ThenByDescending(x => x.Id).Take(query.MaxRows);

        // Write header + rows directly to output (async only)
        var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 16 * 1024, leaveOpen: true)
        {
            AutoFlush = false
        };
        await writer.WriteLineAsync("BookingDate;ValutaDate;Amount;Kind;Subject;RecipientName;Description;Account;Contact;SavingsPlan;Security;SecuritySubType;Quantity");

        await foreach (var r in q.AsAsyncEnumerable().WithCancellation(ct))
        {
            ct.ThrowIfCancellationRequested();
            string[] cols = new[]
            {
                r.BookingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                r.ValutaDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                r.Amount.ToString(CultureInfo.InvariantCulture),
                r.Kind.ToString(),
                CsvEscape(r.Subject),
                CsvEscape(r.Recipient),
                CsvEscape(r.Description),
                CsvEscape(r.AccountName),
                CsvEscape(r.ContactName),
                CsvEscape(r.SavingsName),
                CsvEscape(r.SecurityName),
                r.SecuritySubType?.ToString() ?? string.Empty,
                r.Quantity?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
            };
            await writer.WriteLineAsync(string.Join(';', cols));
        }
        await writer.FlushAsync();
        await output.FlushAsync(ct);

        sw.Stop();
        _logger.LogInformation("Posting export streamed: Kind={Kind}, Id={Id}, Format=CSV, DurationMs={Ms}", query.ContextKind, query.ContextId, sw.ElapsedMilliseconds);
    }

    private static async Task WriteCsvAsync(Stream output, IReadOnlyList<PostingExportRow> rows,
        IReadOnlyDictionary<Guid, string> accountNames,
        IReadOnlyDictionary<Guid, string> contactNames,
        IReadOnlyDictionary<Guid, string> savingsNames,
        IReadOnlyDictionary<Guid, string> securityNames,
        CancellationToken ct)
    {
        using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 1024, leaveOpen: true);
        // Header
        await writer.WriteLineAsync("BookingDate;ValutaDate;Amount;Kind;Subject;RecipientName;Description;Account;Contact;SavingsPlan;Security;SecuritySubType;Quantity");
        // Rows
        foreach (var r in rows)
        {
            ct.ThrowIfCancellationRequested();
            var acc = r.AccountId.HasValue && accountNames.TryGetValue(r.AccountId.Value, out var an) ? an : string.Empty;
            var con = r.ContactId.HasValue && contactNames.TryGetValue(r.ContactId.Value, out var cn) ? cn : string.Empty;
            var sav = r.SavingsPlanId.HasValue && savingsNames.TryGetValue(r.SavingsPlanId.Value, out var sn) ? sn : string.Empty;
            var sec = r.SecurityId.HasValue && securityNames.TryGetValue(r.SecurityId.Value, out var scn) ? scn : string.Empty;
            string[] cols = new[]
            {
                r.BookingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                r.ValutaDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                r.Amount.ToString(CultureInfo.InvariantCulture),
                r.Kind.ToString(),
                CsvEscape(r.Subject),
                CsvEscape(r.RecipientName),
                CsvEscape(r.Description),
                CsvEscape(acc),
                CsvEscape(con),
                CsvEscape(sav),
                CsvEscape(sec),
                r.SecuritySubType?.ToString() ?? string.Empty,
                r.Quantity?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
            };
            await writer.WriteLineAsync(string.Join(';', cols));
        }
        await writer.FlushAsync();
    }

    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value)) { return string.Empty; }
        var v = value.Replace("\r", string.Empty).Replace("\n", " ").Replace("\"", "\"\"");
        return $"\"{v}\"";
    }

    private static void WriteXlsx(Stream output, IReadOnlyList<PostingExportRow> rows,
        IReadOnlyDictionary<Guid, string> accountNames,
        IReadOnlyDictionary<Guid, string> contactNames,
        IReadOnlyDictionary<Guid, string> savingsNames,
        IReadOnlyDictionary<Guid, string> securityNames)
    {
        using var doc = SpreadsheetDocument.Create(output, SpreadsheetDocumentType.Workbook, autoSave: true);
        var wbPart = doc.AddWorkbookPart();
        wbPart.Workbook = new Workbook();
        var wsPart = wbPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        wsPart.Worksheet = new Worksheet(sheetData);

        var sheets = wbPart.Workbook.AppendChild(new Sheets());
        var sheet = new Sheet() { Id = wbPart.GetIdOfPart(wsPart), SheetId = 1, Name = "Postings" };
        sheets.Append(sheet);

        // Header
        string[] headers = new[] { "BookingDate", "ValutaDate", "Amount", "Kind", "Subject", "RecipientName", "Description", "Account", "Contact", "SavingsPlan", "Security", "SecuritySubType", "Quantity" };
        sheetData.AppendChild(CreateRow(headers.Select(h => (object)h).ToArray()));

        foreach (var r in rows)
        {
            var acc = r.AccountId.HasValue && accountNames.TryGetValue(r.AccountId.Value, out var an) ? an : string.Empty;
            var con = r.ContactId.HasValue && contactNames.TryGetValue(r.ContactId.Value, out var cn) ? cn : string.Empty;
            var sav = r.SavingsPlanId.HasValue && savingsNames.TryGetValue(r.SavingsPlanId.Value, out var sn) ? sn : string.Empty;
            var sec = r.SecurityId.HasValue && securityNames.TryGetValue(r.SecurityId.Value, out var scn) ? scn : string.Empty;
            sheetData.AppendChild(CreateRow(new object[]
            {
                r.BookingDate,
                r.ValutaDate,
                r.Amount,
                r.Kind.ToString(),
                r.Subject ?? string.Empty,
                r.RecipientName ?? string.Empty,
                r.Description ?? string.Empty,
                acc,
                con,
                sav,
                sec,
                r.SecuritySubType?.ToString() ?? string.Empty,
                r.Quantity ?? (object)string.Empty
            }));
        }

        wbPart.Workbook.Save();
    }

    private static Row CreateRow(object[] values)
    {
        var row = new Row();
        foreach (var v in values)
        {
            if (v is DateTime dt)
            {
                // Excel stores dates as serial numbers from 1899-12-30 by default
                // For simplicity write as string ISO-8601 to avoid styles
                row.Append(new Cell { DataType = CellValues.String, CellValue = new CellValue(dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) });
            }
            else if (v is decimal dec)
            {
                row.Append(new Cell { DataType = CellValues.Number, CellValue = new CellValue(dec.ToString(CultureInfo.InvariantCulture)) });
            }
            else if (v is string s)
            {
                row.Append(new Cell { DataType = CellValues.String, CellValue = new CellValue(s) });
            }
            else if (v is Enum e)
            {
                row.Append(new Cell { DataType = CellValues.String, CellValue = new CellValue(e.ToString()) });
            }
            else if (v == null)
            {
                row.Append(new Cell { DataType = CellValues.String, CellValue = new CellValue(string.Empty) });
            }
            else
            {
                row.Append(new Cell { DataType = CellValues.String, CellValue = new CellValue(v.ToString()) });
            }
        }
        return row;
    }
}
