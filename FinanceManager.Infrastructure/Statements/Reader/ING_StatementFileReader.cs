using FinanceManager.Application.Statements;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace FinanceManager.Infrastructure.Statements.Reader
{
    public class ING_StatementFileReader : IStatementFileReader
    {
        private string[] Templates = new string[]
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
</template>",
            @"
<template>
  <section name='Block1' type='ignore' endKeyword='Hauptkarte/n'/>
  <section name='Block2' type='ignore' endKeyword='Hauptkarte/n'/>
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
"
        };

        public StatementParseResult? Parse(string fileName, byte[] fileBytes)
        {
            var DraftId = Guid.NewGuid();
            XmlDoc = new XmlDocument();
            var fileContent = ReadFile(fileBytes);
            List<Exception> ErrorList = new List<Exception>();
            for (int idx = Templates.GetLowerBound(0); idx <= Templates.GetUpperBound(0); idx++)
            {
                XmlDoc.LoadXml(Templates[idx]);
                try
                {
                    CurrentMode = ParseMode.None;
                    CurrentSection = null;
                    GlobalDraftData = new StatementHeader();
                    GlobalLineData = new StatementMovement();
                    var resultList = new List<StatementMovement>();
                    EntryNo = 0;
                    foreach (var line in fileContent)
                    {
                        var record = ParseNextLine(line);                        
                        if (record is null) continue;
                        resultList.Add(record);
                    }
                    CurrentMode = ParseMode.None;
                    ErrorList.Clear();
                    if (resultList.Any())
                        return new StatementParseResult(GlobalDraftData, resultList);
                }
                catch (Exception ex)
                {
                    ErrorList.Add(ex);
                }
            }
            return null;
        }

        private IEnumerable<string> ReadFile(byte[] fileBytes)
        {
            return Encoding.UTF8.GetString(fileBytes)
                .Replace("\r\n", "\n") // Windows zu Unix
                .Replace("\r", "\n")   // Mac zu Unix
                .Split('\n')
                .AsEnumerable();
        }
        private enum ParseMode
        {

            None,
            Ignore,
            KeyValue,
            Table,
            TableHeader,
            DynamicTable
        }

;

        private enum VariableMode
        {

            Always,
            OnlyWhenEmpty

        }

;

        private VariableMode GetVariableMode(string text)
        {
            switch (text)
            {
                case "always":
                    return VariableMode.Always;
                case "onlywhenempty":
                    return VariableMode.OnlyWhenEmpty;
                default:
                    throw new ApplicationException("Unknown variable mode!");
            }
        }

        private ParseMode CurrentMode = ParseMode.None;
        private string[] EndKeywords = null;
        private string TableFieldSeparator = ";";
        private bool RemoveDuplicates = false;
        private int TableRecordLength = 0;
        private string[] IgnoreRecordKeywords = null;
        private XmlNode CurrentSection = null;
        private StatementHeader GlobalDraftData;
        private StatementMovement GlobalLineData;
        private StatementMovement RecordLineData = null;
        private XmlDocument XmlDoc;
        private int EntryNo = 0;

        private StatementMovement ParseNextLine(string line)
        {
            switch (CurrentMode)
            {
                case ParseMode.None:
                    if (CurrentSection == null)
                        CurrentSection = XmlDoc.DocumentElement.FirstChild;
                    else
                        CurrentSection = CurrentSection.NextSibling;
                    InitSection();
                    ParseNextLine(line);
                    break;
                case ParseMode.Ignore:
                    if (line.Length == 0)
                        CurrentMode = ParseMode.None;
                    else if (EndKeywords is null)
                        return null;
                    else if (!EndKeywords.Any(kw => line.Contains(kw)))
                        return null;
                    CurrentMode = ParseMode.None;
                    break;
                case ParseMode.KeyValue:
                    if (line.Length == 0)
                        CurrentMode = ParseMode.None;
                    else
                        ParseKeyValue(line);
                    break;
                case ParseMode.TableHeader:
                    CurrentMode = ParseMode.Table;
                    break;
                case ParseMode.Table:
                    if (line.Length == 0)
                        CurrentMode = ParseMode.None;
                    else if ((EndKeywords is not null) && EndKeywords.Any(kw => line.Contains(kw)))
                    {
                        CurrentMode = ParseMode.None;
                        return ParseNextLine(line);
                    }
                    else
                        return ParseTableRecord(line);
                    break;
                case ParseMode.DynamicTable:
                    if ((EndKeywords is not null) && EndKeywords.Any(kw => line.Contains(kw)))
                    {
                        CurrentMode = ParseMode.None;
                        return ParseNextLine(line);
                    }
                    else
                        return ParseDynamicTableRecord(line);
            }
            return null;
        }

        private void InitSection()
        {
            switch (CurrentSection.Attributes["type"].Value)
            {
                case "ignore":
                    CurrentMode = ParseMode.Ignore;
                    break;
                case "keyvalue":
                    CurrentMode = ParseMode.KeyValue;
                    break;
                case "table":
                    {
                        CurrentMode = ParseMode.Table;
                        if (CurrentSection.Attributes["containsheader"].Value == "true")
                            CurrentMode = ParseMode.TableHeader;
                        TableFieldSeparator = CurrentSection.Attributes["fieldSeparator"]?.Value;
                        if (string.IsNullOrEmpty(TableFieldSeparator))
                            TableFieldSeparator = ";";

                        var queryIgnore = from XmlNode cn in CurrentSection.ChildNodes
                                          where cn.Name == "ignore"
                                          select cn.Attributes["keyword"].Value;
                        IgnoreRecordKeywords = queryIgnore.ToArray();
                        if (!bool.TryParse(CurrentSection.Attributes["removeDuplicates"]?.Value, out RemoveDuplicates))
                            RemoveDuplicates = false;
                    }
                    break;
                case "dynTable":
                    {
                        CurrentMode = ParseMode.DynamicTable;
                        if (!int.TryParse(CurrentSection.Attributes["recordLength"]?.Value, out TableRecordLength))
                            TableRecordLength = 0;
                        TableFieldSeparator = CurrentSection.Attributes["fieldSeparator"]?.Value;
                        if (string.IsNullOrEmpty(TableFieldSeparator))
                            TableFieldSeparator = ";";
                        var queryIgnore = from XmlNode cn in CurrentSection.ChildNodes
                                          where cn.Name == "ignore"
                                          select cn.Attributes["keyword"].Value;
                        IgnoreRecordKeywords = queryIgnore.ToArray();
                        if (!bool.TryParse(CurrentSection.Attributes["removeDuplicates"]?.Value, out RemoveDuplicates))
                            RemoveDuplicates = false;
                    }
                    break;
                default:
                    throw new ApplicationException("unknown section type!");
            }
            EndKeywords = CurrentSection.Attributes["endKeyword"]?.Value?.Split('|');
        }

        private void ParseKeyValue(string line)
        {
            string[] Values = line.Split(';');
            foreach (XmlNode Key in CurrentSection.ChildNodes)
                if (Key.Attributes["name"].Value == Values[0])
                {
                    string VariableName = Key.Attributes["variable"].Value;
                    ParseVariable(VariableName, Values[1], true, GetVariableMode(Key.Attributes["mode"].Value));
                }
        }
        private void ParseVariable(string Name, string Value, bool global, VariableMode mode)
        {
            StatementMovement line = global ? GlobalLineData : RecordLineData;
            switch (Name)
            {
                case "BankAccountNo":
                    GlobalDraftData.AccountNumber = Value.Replace(" ", string.Empty);
                    break;
                case "PostingDate":
                    line.BookingDate = DateTime.Parse(Value, new CultureInfo("de-DE"));
                    break;
                case "ValutaDate":
                    line.ValutaDate = DateTime.Parse(Value, new CultureInfo("de-DE"));
                    break;
                case "SourceName":
                    line.Counterparty = Value;
                    break;
                case "PostingDescription":
                    line.PostingDescription = Value;
                    break;
                case "Description":
                    line.Subject = Value;
                    break;
                case "CurrencyCode":
                    line.CurrencyCode = Value;
                    break;
                case "Amount":
                    line.Amount = decimal.Parse(Value, new CultureInfo("de-DE"));
                    break;
            }
        }

        private StatementMovement ParseDynamicTableRecord(string line)
        {
            if (TableRecordLength > 0 && line.Length != TableRecordLength)
                return null;
            try
            {
                return ParseTableRecord(line);
            }
            catch (FormatException) { return null; }
        }

        private StatementMovement ParseTableRecord(string line)
        {
            if ((IgnoreRecordKeywords is not null) && IgnoreRecordKeywords.Any(kw => line.Contains(kw)))
                return null;
            string[] Values = line.Split(TableFieldSeparator);
            RecordLineData = new StatementMovement() {};
            int FieldIdx = Values.GetLowerBound(0);
            foreach (XmlNode Field in CurrentSection.ChildNodes)
            {
                if (Field.Name != "field")
                    continue;
                string VariableName = Field.Attributes["variable"]?.Value;
                int.TryParse(Field.Attributes["length"]?.Value, out int fieldLength);
                if (TableFieldSeparator == "#None#")
                {
                    if (fieldLength == 0)
                        fieldLength = Values[0].Length;
                    var currentValue = Values[0].Substring(0, fieldLength);
                    Values[0] = Values[0].Remove(0, fieldLength);
                    ParseVariable(VariableName, currentValue, false, VariableMode.Always);
                }
                else
                {
                    ParseVariable(VariableName, Values[FieldIdx], false, VariableMode.Always);
                    FieldIdx += 1;
                }
            }
            return FinishRecord();
        }

        private StatementMovement FinishRecord()
        {
            try
            {
                RecordLineData.IsPreview = (RecordLineData.BookingDate == DateTime.MinValue)
                    || (RecordLineData.BookingDate > DateTime.Today);
                return RecordLineData;
                
            }
            finally
            {
                RecordLineData = null;
            }
        }
    }
}
