using FinanceManager.Application.Statements;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using System.Globalization;
using System.Text.RegularExpressions;
using FinanceManager.Shared.Dtos;

namespace FinanceManager.Infrastructure.Statements.Reader
{
    public class ING_PDfReader : IStatementFileReader
    {
        protected IEnumerable<string> ReadContent(byte[] fileBytes)
        {
            using var ms = new MemoryStream(fileBytes, false);
            PdfReader iTextReader = new PdfReader(ms);
            try
            {
                PdfDocument pdfDoc = new PdfDocument(iTextReader);
                int numberofpages = pdfDoc.GetNumberOfPages();
                ITextExtractionStrategy strategy = new SimpleTextExtractionStrategy();
                var lastContent = "";
                for (int pageNo = 1; pageNo <= numberofpages; pageNo++)
                {
                    var page = pdfDoc.GetPage(pageNo);
                    var pageContent = PdfTextExtractor.GetTextFromPage(page, strategy).Replace("\r\n", "\n").Replace("\r", "\n");
                    var currentContent = pageContent;
                    if (!string.IsNullOrWhiteSpace(lastContent) && pageContent.StartsWith(lastContent))
                        pageContent = pageContent.Remove(0, lastContent.Length).TrimStart('\n');
                    lastContent = currentContent;

                    var pageLines = pageContent.TrimEnd('\n').Split('\n');
                    foreach (var line in pageLines)
                        yield return line;
                }
            }
            finally
            {
                iTextReader.Close();
            }
        }

        public StatementParseResult? Parse(string fileName, byte[] fileBytes)
        {
            return null;
        }

        public StatementParseResult? ParseDetails(string originalFileName, byte[] fileBytes)
        {
            try
            {
                var culture = new CultureInfo("de-DE");
                var lines = ReadContent(fileBytes).ToList();

                // Felder
                bool isDividend = false;
                string? isin = null;
                string? securityName = null;
                decimal? quantity = null; // Hinweis: StatementMovement hat kein Mengenfeld – wird in PostingDescription vermerkt
                string? currency = null;
                decimal? amount = null;
                string? iban = null;
                string? postingDescription = null;
                DateTime? bookingDate = null;
                DateTime? valutaDate = null;

                // Steuern / Provision
                decimal? capitalGainsTax = null;      // Kapitalertragsteuer
                decimal? solidaritySurcharge = null;  // Solidaritätszuschlag
                decimal? churchTax = null;            // Kirchensteuer (optional)
                string? taxCurrency = null;
                decimal? provision = null;            // Provision/Kommission

                var rxIsin = new Regex(@"^ISIN\s*\(WKN\)\s*(?<isin>[A-Z0-9]{10,12})(?:\s*\([A-Z0-9]+\))?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                var rxWertpapier = new Regex(@"^Wertpapierbezeichnung\s*(?<name>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                var rxNominale = new Regex(@"^Nominale\s+(?:St(?:ü|ue)ck\s*)?(?<num>[0-9.,]+)(?:\s*St(?:ü|ue)ck)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                var rxAmount = new Regex(@"^(?<ignore1>Gesamtbetrag|Endbetrag) zu Ihren\s+(?<dir>Gunsten|Lasten)\s+(?<cur>[A-Z]{3})\s+(?<amt>[+\-]?\s*[0-9\.,]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                var rxIban = new Regex(@"^Abrechnungs-IBAN\s+(?<iban>[A-Z]{2}[0-9A-Z ]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                var rxZahltag = new Regex(@"^Zahltag\s+(?<date>\d{2}\.\d{2}\.\d{4})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                var rxValuta = new Regex(@"^Valuta\s+(?<date>\d{2}\.\d{2}\.\d{4})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                var rxDate = new Regex(@"^Datum:\s+(?<date>\d{2}\.\d{2}\.\d{4})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                // Steuer/Provision
                var rxTax = new Regex(
                    @"^(?<name>Kapitalertragsteuer|Solidarit[aä]tszuschlag|Kirchensteuer)\s+(?<rate>\d{1,3},\d{2})%\s+(?<cur>[A-Z]{3})\s+(?<amt>[+\-]?\s*[0-9\.\,]+)$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
                );
                var rxProvision = new Regex(@"^Provision\s+(?<cur>[A-Z]{3})\s+(?<amt>[+\-]?\s*[0-9\.\,]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                // Transaktionsart (Kauf/Verkauf)
                var rxSell = new Regex(@"^Wertpapierabrechnung\s+(Verkauf(\s+aus\s+Kapitalmaßnahme)?|Verkauf)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                var rxBuy  = new Regex(@"^Wertpapierabrechnung\s+(Kauf(\s+aus\s+Sparplan)?|Kauf)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                var rxOrder = new Regex(@"^Ordernummer\s+(?<orderno>[0-9\.]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                SecurityTransactionType? txType = null;
                string? orderNo = null;

                foreach (var raw in lines)
                {
                    var line = raw.Trim();
                    if (string.IsNullOrEmpty(line)) { continue; }

                    if (!isDividend && (line.Contains("Dividendengutschrift", StringComparison.OrdinalIgnoreCase) || line.Contains("Ertragsgutschrift", StringComparison.OrdinalIgnoreCase)))
                    {
                        txType = SecurityTransactionType.Dividend;
                        isDividend = true;
                        postingDescription = "Dividendengutschrift";
                        continue;
                    }

                    if (rxSell.IsMatch(line))
                    {
                        txType = SecurityTransactionType.Sell;
                        continue;
                    }
                    if (rxBuy.IsMatch(line))
                    {
                        txType = SecurityTransactionType.Buy;
                        continue;
                    }

                    var mOrder = rxOrder.Match(line);
                    if (mOrder.Success)
                    {
                        orderNo = mOrder.Groups["orderno"].Value.Replace(" ", string.Empty);
                        continue;
                    }

                    var mIsin = rxIsin.Match(line);
                    if (mIsin.Success)
                    {
                        isin = mIsin.Groups["isin"].Value.Trim();
                        continue;
                    }

                    var mWp = rxWertpapier.Match(line);
                    if (mWp.Success)
                    {
                        securityName = mWp.Groups["name"].Value.Trim();
                        continue;
                    }

                    var mNom = rxNominale.Match(line);
                    if (mNom.Success)
                    {
                        var numTxt = mNom.Groups["num"].Value.Trim();
                        if (decimal.TryParse(numTxt, NumberStyles.Number, culture, out var q))
                        {
                            quantity = q;
                        }
                        continue;
                    }

                    var mAmt = rxAmount.Match(line);
                    if (mAmt.Success)
                    {
                        currency = mAmt.Groups["cur"].Value.Trim();
                        var amtTxt = mAmt.Groups["amt"].Value.Trim();

                        // Leerzeichen zwischen Vorzeichen und Zahl entfernen
                        amtTxt = amtTxt.Replace(" ", "");

                        if (decimal.TryParse(amtTxt, NumberStyles.Number | NumberStyles.AllowLeadingSign, culture, out var parsed))
                        {
                            var dir = mAmt.Groups["dir"].Value;
                            var abs = Math.Abs(parsed);
                            amount = string.Equals(dir, "Lasten", StringComparison.OrdinalIgnoreCase) ? -abs : abs;
                        }
                        continue;
                    }

                    var mIban = rxIban.Match(line);
                    if (mIban.Success)
                    {
                        iban = new string(mIban.Groups["iban"].Value.Where(c => !char.IsWhiteSpace(c)).ToArray());
                        continue;
                    }

                    var mZ = rxZahltag.Match(line);
                    if (mZ.Success && bookingDate == null)
                    {
                        if (DateTime.TryParse(mZ.Groups["date"].Value, culture, DateTimeStyles.None, out var d))
                        {
                            bookingDate = d;
                        }
                        continue;
                    }

                    var mD = rxDate.Match(line);
                    if (mD.Success)
                    {
                        if (DateTime.TryParse(mD.Groups["date"].Value, culture, DateTimeStyles.None, out var d))
                        {
                            bookingDate = d;
                        }
                        continue;
                    }

                    var mV = rxValuta.Match(line);
                    if (mV.Success)
                    {
                        if (DateTime.TryParse(mV.Groups["date"].Value, culture, DateTimeStyles.None, out var d))
                        {
                            valutaDate = d;
                        }
                        continue;
                    }

                    // Steuern parsen
                    var mTax = rxTax.Match(line);
                    if (mTax.Success)
                    {
                        taxCurrency ??= mTax.Groups["cur"].Value.Trim();
                        var amtTxt = mTax.Groups["amt"].Value.Trim().Replace(" ", "");
                        if (decimal.TryParse(amtTxt, NumberStyles.Number | NumberStyles.AllowLeadingSign, culture, out var taxAmt))
                        {
                            var name = mTax.Groups["name"].Value;
                            if (name.StartsWith("Kapitalertragsteuer", StringComparison.OrdinalIgnoreCase))
                            {
                                capitalGainsTax = taxAmt;
                            }
                            else if (name.StartsWith("Solidar", StringComparison.OrdinalIgnoreCase))
                            {
                                solidaritySurcharge = taxAmt;
                            }
                            else if (name.StartsWith("Kirchen", StringComparison.OrdinalIgnoreCase))
                            {
                                churchTax = taxAmt;
                            }
                        }
                        continue;
                    }

                    // Provision parsen
                    var mProv = rxProvision.Match(line);
                    if (mProv.Success)
                    {
                        var cur = mProv.Groups["cur"].Value.Trim();
                        var amtTxt = mProv.Groups["amt"].Value.Trim().Replace(" ", "");
                        if (decimal.TryParse(amtTxt, NumberStyles.Number | NumberStyles.AllowLeadingSign, culture, out var provAmt))
                        {
                            provision = provAmt;
                        }
                        continue;
                    }
                }

                if (amount is null)
                    return null;

                // Subject zusammensetzen
                var subjectParts = new List<string>();
                if (txType.HasValue) { subjectParts.Add($"{txType}"); }
                if (!string.IsNullOrWhiteSpace(isin)) { subjectParts.Add(isin!); }
                if (!string.IsNullOrWhiteSpace(securityName)) { subjectParts.Add(securityName!); }
                if (!string.IsNullOrWhiteSpace(orderNo)) { subjectParts.Add($"Order {orderNo}"); }

                // Steuer-Zusammenfassung
                var taxItems = new List<string>();
                var taxCur = taxCurrency ?? currency ?? "EUR";
                if (capitalGainsTax.HasValue) { taxItems.Add($"KESt {capitalGainsTax.Value.ToString(culture)} {taxCur}"); }
                if (solidaritySurcharge.HasValue) { taxItems.Add($"SolZ {solidaritySurcharge.Value.ToString(culture)} {taxCur}"); }
                if (churchTax.HasValue) { taxItems.Add($"KiSt {churchTax.Value.ToString(culture)} {taxCur}"); }
                if (provision.HasValue) { taxItems.Add($"Prov {provision.Value.ToString(culture)} {currency ?? "EUR"}"); }
                if (taxItems.Count > 0)
                {
                    subjectParts.Add("Steuern/Gebühren: " + string.Join("; ", taxItems));
                }

                var subject = string.Join(" · ", subjectParts);

                var header = new StatementHeader()
                {
                    IBAN = iban,
                    AccountNumber = iban,
                    Description = $"ING PDF Import {originalFileName}"
                };

                var movement = new StatementMovement()
                {
                    BookingDate = bookingDate ?? default,
                    ValutaDate = valutaDate ?? bookingDate ?? default,
                    Amount = amount ?? 0m,
                    CurrencyCode = currency ?? "EUR",
                    Subject = subject,
                    PostingDescription = postingDescription,
                    IsPreview = false,
                    IsError = false,
                    Quantity = quantity,
                    TaxAmount = (capitalGainsTax ?? 0m) + (solidaritySurcharge ?? 0m) + (churchTax ?? 0m),
                    FeeAmount = provision
                };

                if (movement.TaxAmount == 0m) movement.TaxAmount = null;

                if (txType.HasValue)
                {
                    movement.PostingDescription = $"{txType.Value}";
                }

                return new StatementParseResult(header, new List<StatementMovement> { movement });
            }
            catch
            {
                return null;
            }
        }
    }
}
