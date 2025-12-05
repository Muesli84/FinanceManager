using FinanceManager.Application.Statements;
using FinanceManager.Shared.Extensions;
using System.Text.RegularExpressions;
using System.Xml;

namespace FinanceManager.Infrastructure.Statements.Reader
{
    public class Wuestenrot_StatementFileReader : PDFStatementFilereader, IStatementFileReader
    {
        private string[] _Templates = new string[]
        {
            @"
<template>
  <section name='Title' type='ignore' endKeyword='Nr.'></section>
  <section name='AccountInfo' type='keyvalue' separator=':' endKeyword='Anfangssaldo'>
    <key name='Kontonummer' variable='BankAccountNo' mode='always'/>
    <key name='IBAN' variable='BankAccountNo' mode='onlywhenempty'/>
  </section>
  <section name='table' type='table' containsheader='false' fieldSeparator='#None#' endKeyword='Endsaldo'>
    <regExp pattern='(?&lt;PostingDate&gt;\d{2}\.\d{2}\.\d{4})\s+(?&lt;PostingDescription&gt;.+?)\s+(?&lt;ValutaDate&gt;\d{2}\.\d{2}\.\d{4})\s+(?&lt;Amount&gt;-?\s?\d{1,3}(?:\.\d{3})*,\d{2})' multiplier='1'/>
    <regExp type='additional' maxoccur='2' pattern='(?&lt;SourceName&gt;[\x20-\x7E]+)' />
  </section>
  <section name='BlockEnd' type='ignore'/>
</template>"
        };
        protected override string[] Templates => _Templates;
        private StatementMovement _RecordDelay = null;
        private int _additionalRecordInformationCount = 0;
        protected override StatementMovement ParseTableRecord(string line)
        {
            if (_RecordDelay is null)
            {
                var record = base.ParseTableRecord(line);
                if (record is null || record.BookingDate == DateTime.MinValue)
                    return record;
                _RecordDelay = record;
                return null;
            }
            else
            {
                return ParseWuestenrotRecord(line);
            }
        }
        protected override StatementMovement OnTableFinished()
        {
            return _RecordDelay ?? base.OnTableFinished();
        }
        private StatementMovement ParseWuestenrotRecord(string line)
        {
            var isNextRecord = false;
            foreach (XmlNode Field in CurrentSection.ChildNodes)
            {
                switch (Field.Name)
                {
                    case "regExp":
                        isNextRecord = isNextRecord || OwnParseRegularExpression(line, Field);
                        break;
                }
            }
            if (!isNextRecord) return null;
            var outputRecord = ReturnCurrentDelayedRecord();
            _ = ParseTableRecord(line);
            return outputRecord;

        }

        private StatementMovement ReturnCurrentDelayedRecord()
        {
            var outputRecord = _RecordDelay;
            _RecordDelay = null;
            _additionalRecordInformationCount = 0;
            return outputRecord;
        }
        protected override void ParseRegularExpression(string input, XmlNode field)
        {
            var type = field.Attributes.GetNamedItem("type")?.Value;
            if (type != "additional")
                base.ParseRegularExpression(input, field);
        }
        private bool OwnParseRegularExpression(string input, XmlNode field)
        {
            var pattern = field.Attributes["pattern"].Value;
            var type = field.Attributes.GetNamedItem("type")?.Value;
            var maxoccur = (field.Attributes.GetNamedItem("maxoccur")?.Value ?? "-").ToInt32();
            if (type != "additional")
            {
                var record = base.ParseTableRecord(input);
                if (record is not null)
                    return true;
                return false;
            }
            var regex = new Regex(pattern, RegexOptions.IgnorePatternWhitespace);
            var match = regex.Match(input);
            if (!int.TryParse(field.Attributes["multiplier"]?.Value, out int multiplier))
                multiplier = 1;
            if (match.Success)
            {
                foreach (var groupName in regex.GetGroupNames())
                {
                    if (int.TryParse(groupName, out _))
                        continue;

                    var value = match.Groups[groupName].Value;
                    if (string.IsNullOrEmpty(value))
                        continue;
                    ParseVariable(_RecordDelay, groupName, value, VariableMode.Always, multiplier);
                }
                _additionalRecordInformationCount++;
                if (maxoccur > 0 && _additionalRecordInformationCount >= maxoccur)
                    return true;
            }
            return false;
        }

    }
}
