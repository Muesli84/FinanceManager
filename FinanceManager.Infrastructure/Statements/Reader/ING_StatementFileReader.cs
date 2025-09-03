using FinanceManager.Application.Statements;
using iText.Kernel.Geom;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace FinanceManager.Infrastructure.Statements.Reader
{
    public class ING_StatementFileReader : TemplateStatementFileReader, IStatementFileReader
    {
        private string[] OldTemplates = new string[]
        {
            @"
<template>
  <section name='recordSet' type='dynTable' recordLength='84' fieldSeparator='#None#' removeDuplicates='true'>
    <field name='Buchung' variable='PostingDate' type='date' length='11'/>
    <field name='Valuta' variable='ValutaDate' type='date' length='11'/>
    <field name='Auftraggeber/Empfänger' variable='SourceName' length='23'/>
    <field name='Ort' variable='' length='14'/>
    <field name='Land' variable='' length='3'/>
    <field name='Karte' variable='' length='15'/>
    <field name='Betrag' variable='Amount' type='decimal'/>    
  </section>
</template>",
        };
        private string[] _Templates = new string[]
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
    <field name='Kategorie' variable='Category'/>
    <field name='Verwendungszweck' variable='Description'/>
    <field name='Saldo' variable=''/>
    <field name='Währung' variable='CurrencyCode'/>
    <field name='Betrag' variable='Amount'/>
    <field name='Währung' variable='CurrencyCode'/>
  </section>
</template>",
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

        protected override string[] Templates => _Templates;

        protected override IEnumerable<string> ReadContent(byte[] fileBytes)
        {
            return Encoding.UTF8.GetString(fileBytes)
                .Replace("\r\n", "\n") // Windows zu Unix
                .Replace("\r", "\n")   // Mac zu Unix
                .Split('\n')
                .AsEnumerable();
        }
    }
}
