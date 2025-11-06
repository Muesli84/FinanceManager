using FinanceManager.Application.Statements;

namespace FinanceManager.Infrastructure.Statements.Reader
{
    public class Barclays_StatementFileReader : PDFStatementFilereader, IStatementFileReader
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
        
    }
}
