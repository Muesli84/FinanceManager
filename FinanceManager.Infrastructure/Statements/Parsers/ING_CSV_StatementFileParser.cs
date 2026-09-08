using FinanceManager.Application.Statements;
using FinanceManager.Infrastructure.Statements.Files;
using Microsoft.Extensions.Logging;
using System.Text;

namespace FinanceManager.Infrastructure.Statements.Parsers
{
    /// <summary>
    /// Statement file reader for ING statement exports using template-based parsing.
    /// Provides templates used by the base <see cref="TemplateStatementFileParser"/> to detect account
    /// information and table columns. The reader exposes a UTF-8 content reader suitable for PDF-to-text
    /// conversions that produce UTF-8 encoded bytes.
    /// Supports multi-result parsing for ING collection statements (Sammelauszüge) that contain
    /// multiple IBAN blocks in a single file.
    /// </summary>
    public class ING_CSV_StatementFileParser : TemplateStatementFileParser
    {
        /// <summary>
        /// Templates used by the parsing engine to recognize ING statement layout variations.
        /// Each template contains sections and field mappings consumed by the base template parser.
        /// </summary>
        private static readonly string[] _Templates = new string[]
        {
            @"
<template>
  <section name='Title' type='ignore'>
  </section>
  <section name='AccountInfo' type='keyvalue'>
    <key name='IBAN' variable='BankAccountNo' mode='always'/>
    <key name='Kunde' variable='BankAccountNo' mode='onlywhenempty'/>
  </section>
  <section name='Sortierung ' type='ignore'>
  </section>
  <section name='BlaBla' type='ignore'>
  </section>
  <section name='table' type='table' containsheader='true'>
    <field name='Buchung' variable='PostingDate'/>
    <field name='Valuta' variable='ValutaDate'/>
    <field name='Auftraggeber/Empfänger' variable='SourceName'/>
    <field name='Buchungstext' variable='PostingDescription'/>
    <field name='Verwendungszweck' variable='Description'/>
    <field name='Saldo' variable=''/>
    <field name='Währung' variable='CurrencyCode'/>
    <field name='Betrag' variable='Amount'/>
    <field name='Währung' variable='CurrencyCode'/>
  </section>
</template>"
        };
        /// <summary>
        /// Initializes a new instance of the ING_StatementFileReader class using the predefined templates.
        /// </summary>
        /// <param name="logger">Logger instance for logging parser operations and errors.</param>
        public ING_CSV_StatementFileParser(ILogger<ING_CSV_StatementFileParser> logger) : base(_Templates, logger)
        {
        }
        /// <summary>
        /// Determines whether the specified statement file is of a supported type and can be parsed by this parser.
        /// </summary>
        /// <param name="statementFile">The statement file to evaluate for compatibility with this parser. Cannot be null.</param>
        /// <returns>true if the statement file is of a supported type and can be parsed; otherwise, false.</returns>
        protected override bool CanParse(IStatementFile statementFile)
        {
            return new Type[] { typeof(ING_Csv_StatementFile), typeof(InlineING_CsvStatementFile) }.Any(t => t.IsAssignableFrom(statementFile.GetType()));
        }

        /// <summary>
        /// Parses the specified statement file. For ING collection statements (Sammelauszüge) containing
        /// multiple IBAN blocks, returns one <see cref="StatementParseResult"/> per IBAN block.
        /// For normal single-account statements, returns a single-element list.
        /// </summary>
        /// <param name="statementFile">The statement file.</param>
        /// <returns>The result.</returns>
        public override IReadOnlyList<StatementParseResult>? Parse(IStatementFile statementFile)
        {
            if (!CanParse(statementFile)) return null;

            var allLines = statementFile.ReadContent().ToList();

            // Detect block starts: each ING sub-account block begins with a line starting with "IBAN;"
            // (e.g. "IBAN;DE50700500000007882990"). Multiple such lines indicate a Sammelkonto CSV.
            var blockStartIndices = allLines
                .Select((line, idx) => (line, idx))
                .Where(x => x.line.StartsWith("IBAN;", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.idx)
                .ToList();

            if (blockStartIndices.Count <= 1)
            {
                // Single block or no IBAN marker: delegate to base template parsing
                return base.Parse(statementFile);
            }

            // Multiple blocks: parse each one separately.
            // Each block starts at its "IBAN;" line. A leading empty line is prepended so that the
            // template's first section ("Title", type=ignore) ends immediately without consuming the
            // IBAN line — this allows the "AccountInfo" section to read it correctly.
            var results = new List<StatementParseResult>();
            for (int i = 0; i < blockStartIndices.Count; i++)
            {
                var start = blockStartIndices[i];
                var end = i + 1 < blockStartIndices.Count ? blockStartIndices[i + 1] : allLines.Count;

                // Prepend one empty line to satisfy the "Title" (ignore) section of the template.
                var blockLines = new List<string>(end - start + 1) { string.Empty };
                blockLines.AddRange(allLines.GetRange(start, end - start));

                var blockFile = new InlineING_CsvStatementFile(statementFile.FileName, blockLines);
                var blockResults = base.Parse(blockFile);
                if (blockResults != null)
                    results.AddRange(blockResults);
            }

            return results.Count > 0 ? results : null;
        }

        /// <summary>
        /// Parses detailed information from the statement file. Delegates to <see cref="Parse"/>.
        /// </summary>
        /// <param name="statementFile">The statement file.</param>
        /// <returns>The result.</returns>
        public override IReadOnlyList<StatementParseResult>? ParseDetails(IStatementFile statementFile)
            => Parse(statementFile);

        /// <summary>
        /// Lightweight <see cref="IStatementFile"/> wrapper used to parse a pre-split block of lines
        /// as if it were an ING CSV file, bypassing the byte-level load and encoding detection.
        /// </summary>
        private sealed class InlineING_CsvStatementFile : IStatementFile
        {
            private readonly string _fileName;
            private readonly IReadOnlyList<string> _lines;

            public InlineING_CsvStatementFile(string fileName, IReadOnlyList<string> lines)
            {
                _fileName = fileName;
                _lines = lines;
            }

            public string FileName => _fileName;

            public bool Load(string fileName, byte[] fileBytes) => true;

            public IEnumerable<string> ReadContent() => _lines;
        }
    }
}
