using FinanceManager.Application.Statements;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace FinanceManager.Infrastructure.Statements.Reader
{
    public class Barclays_StatementFileReader : TemplateStatementFileReader, IStatementFileReader
    {
        private string[] _Templates = new string[] {
            @"
<template>
  <section name='Block1' type='ignore' endKeyword='Allgemeine Umsätze '/>
  <section name='table' type='table' containsheader='false' fieldSeparator='#None#' endKeyword='Wie mit Ihnen vereinbart,'>
    <regExp pattern='^(?:\s*(?&lt;Buchungsart&gt;\w+):)?\s*(?&lt;SourceName&gt;.*?)\s+(?&lt;PostingDate&gt;\d{2}\.\d{2}\.\d{2})\s+(?&lt;Gesamtbetrag&gt;\d{1,3},\d{2})-\s+(?&lt;Description&gt;.+?)\s+(?&lt;Amount&gt;\d{1,3},\d{2})' multiplier='-1'/>
  </section>
  <section name='Block2' type='ignore' endKeyword='Hauptkarte/n|Umsatzübersicht'/>
  <section name='table' type='table' containsheader='false' fieldSeparator='#None#' endKeyword='Umsätze |Per Lastschrift dankend erhalten' stopOnError='true'>
    <regExp pattern='^(?&lt;PostingDate&gt;\d{2}\.\d{2}\.\d{4})\s+(?&lt;ValutaDate&gt;\d{2}\.\d{2}\.\d{4})\s+(?&lt;SourceName&gt;.+?)\s+(?&lt;Card&gt;Visa)\s+(?&lt;Amount&gt;\d{1,3},\d{2}[-+])' />
  </section>
  <section name='Block2' type='ignore' endKeyword='Hauptkarte/n|Umsatzübersicht'/>
  <section name='table' type='table' containsheader='false' fieldSeparator='#None#' endKeyword='Umsätze |Per Lastschrift dankend erhalten' stopOnError='true'>
    <ignore keyword='Allgemeine Umsätze'/>
    <ignore keyword='Alter Saldo'/>
    <ignore keyword='Hauptkarte/n'/>
    <field name='Buchung' variable='PostingDate' length='11'/>
    <field name='Valuta' variable='ValutaDate' length='11'/>
    <field name='Auftraggeber/Empfänger' variable='SourceName' length='23'/>
    <field name='Ort' variable='' length='14'/>
    <field name='Land' variable='' length='3'/>
    <field name='Karte' variable='' length='15'/>
    <field name='Betrag' variable='Amount'/>    
  </section>
  <section name='Block2' type='ignore' endKeyword='Hauptkarte/n|Umsatzübersicht'/>
  <section name='table' type='table' containsheader='false' fieldSeparator='#None#' endKeyword='Umsätze |Per Lastschrift dankend erhalten' stopOnError='true'>
    <ignore keyword='Allgemeine Umsätze'/>
    <ignore keyword='Alter Saldo'/>
    <ignore keyword='Hauptkarte/n'/>
    <field name='Buchung' variable='PostingDate' length='11'/>
    <field name='Valuta' variable='ValutaDate' length='11'/>
    <field name='Auftraggeber/Empfänger' variable='SourceName' length='23'/>
    <field name='Ort' variable='' length='14'/>
    <field name='Land' variable='' length='3'/>
    <field name='Karte' variable='' length='15'/>
    <field name='Betrag' variable='Amount'/>    
  </section>
  <section name='Block2' type='ignore' endKeyword='Hauptkarte/n|Umsatzübersicht'/>
  <section name='table' type='table' containsheader='false' fieldSeparator='#None#' endKeyword='Umsätze |Per Lastschrift dankend erhalten' stopOnError='true'>
    <ignore keyword='Allgemeine Umsätze'/>
    <ignore keyword='Alter Saldo'/>
    <ignore keyword='Hauptkarte/n'/>
    <field name='Buchung' variable='PostingDate' length='11'/>
    <field name='Valuta' variable='ValutaDate' length='11'/>
    <field name='Auftraggeber/Empfänger' variable='SourceName' length='23'/>
    <field name='Ort' variable='' length='14'/>
    <field name='Land' variable='' length='3'/>
    <field name='Karte' variable='' length='15'/>
    <field name='Betrag' variable='Amount'/>    
  </section>
  <section name='Block2' type='ignore' endKeyword='Hauptkarte/n|Umsatzübersicht'/>
  <section name='table' type='table' containsheader='false' fieldSeparator='#None#' endKeyword='Umsätze |Per Lastschrift dankend erhalten' stopOnError='true'>
    <ignore keyword='Allgemeine Umsätze'/>
    <ignore keyword='Alter Saldo'/>
    <ignore keyword='Hauptkarte/n'/>
    <field name='Buchung' variable='PostingDate' length='11'/>
    <field name='Valuta' variable='ValutaDate' length='11'/>
    <field name='Auftraggeber/Empfänger' variable='SourceName' length='23'/>
    <field name='Ort' variable='' length='14'/>
    <field name='Land' variable='' length='3'/>
    <field name='Karte' variable='' length='15'/>
    <field name='Betrag' variable='Amount'/>    
  </section>
  <section name='BlockEnd' type='ignore'/>
</template>
",
        @"
<template>
  <section name='Block1' type='ignore' endKeyword='Hauptkarte/n'/>
  <section name='table' type='table' containsheader='false' fieldSeparator='#None#' endKeyword='Umsätze |Per Lastschrift dankend erhalten'>
    <ignore keyword='Allgemeine Umsätze'/>
    <field name='Buchung' variable='PostingDate' length='11'/>
    <field name='Valuta' variable='ValutaDate' length='11'/>
    <field name='Auftraggeber/Empfänger' variable='SourceName' length='23'/>
    <field name='Ort' variable='' length='14'/>
    <field name='Land' variable='' length='3'/>
    <field name='Karte' variable='' length='15'/>
    <field name='Betrag' variable='Amount'/>    
  </section>
  <section name='BlockEnd' type='ignore'/>
</template>
"};
        protected override string[] Templates => _Templates;
        protected override IEnumerable<string> ReadContent(byte[] fileBytes)
        {
            using var ms = new MemoryStream(fileBytes, false);
            PdfReader iTextReader = new PdfReader(ms);
            try
            {
                PdfDocument pdfDoc = new PdfDocument(iTextReader);
                int numberofpages = pdfDoc.GetNumberOfPages();
                ITextExtractionStrategy strategy = new SimpleTextExtractionStrategy();
                var totalContent = "";
                var lastContent = "";
                for (int pageNo = 1; pageNo <= numberofpages; pageNo++)
                {
                    var page = pdfDoc.GetPage(pageNo);
                    var pageContent = PdfTextExtractor.GetTextFromPage(page, strategy).Replace("\r\n", "\n").Replace("\r", "\n");
                    var currentContent = pageContent;
                    if (!string.IsNullOrWhiteSpace(lastContent) && pageContent.StartsWith(lastContent))
                        pageContent = pageContent.Remove(0, lastContent.Length).TrimStart('\n');
                    lastContent = currentContent;
                    totalContent += pageContent;
                }

                var pageLines = totalContent.TrimEnd('\n').Split('\n');
                foreach (var line in pageLines)
                    yield return line;
            }
            finally
            {
                iTextReader.Close();
            }
        }
    }
}
