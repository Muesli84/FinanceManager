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
    <ignore keyword='Allgemeine Umsätze'/>    
    <field name='Auftraggeber/Empfänger' variable='SourceName' length='29'/>
    <field name='Buchung' variable='PostingDate' length='9'/>
    <field name='OrigBetrag' variable='' length='13'/>    
    <field name='Rate' variable='' length='2'/>
    <field name='Reference' variable='Description' length='13'/>    
    <field name='Betrag' variable='Amount' multiplier='-1'/>      
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
    }
}
