using FinanceManager.Application.Statements;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;

namespace FinanceManager.Infrastructure.Statements.Reader
{
    public abstract class TemplateStatementFileReader
    {
        public virtual StatementParseResult? Parse(string fileName, byte[] fileBytes)
        {
            var DraftId = Guid.NewGuid();
            XmlDoc = new XmlDocument();
            var fileContent = ReadContent(fileBytes);
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
                        foreach (var record in ParseNextLine(line).Where(rec => rec is not null))
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

        public virtual StatementParseResult? ParseDetails(string originalFileName, byte[] fileBytes)
        {
            return Parse(originalFileName, fileBytes);
        }

        protected abstract string[] Templates { get; }
        protected abstract IEnumerable<string> ReadContent(byte[] fileBytes);

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

        protected enum VariableMode
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
        private bool StopOnError = true;
        protected XmlNode CurrentSection = null;
        private StatementHeader GlobalDraftData;
        private StatementMovement GlobalLineData;
        private StatementMovement RecordLineData = null;
        private XmlDocument XmlDoc;
        private int EntryNo = 0;

        private IEnumerable<StatementMovement> ParseNextLine(string line)
        {
            switch (CurrentMode)
            {
                case ParseMode.None:
                    if (CurrentSection == null)
                        CurrentSection = XmlDoc.DocumentElement.FirstChild;
                    else
                        CurrentSection = CurrentSection.NextSibling;
                    InitSection();
                    foreach (var record in ParseNextLine(line))
                        yield return record;
                    break;
                case ParseMode.Ignore:
                    if (line.Length == 0)
                        CurrentMode = ParseMode.None;
                    else if (EndKeywords is null)
                        yield return null;
                    else if (!EndKeywords.Any(kw => line.Contains(kw)))
                        yield return null;
                    else
                        CurrentMode = ParseMode.None;
                    break;
                case ParseMode.KeyValue:
                    if (line.Length == 0)
                        CurrentMode = ParseMode.None;
                    else if (EndKeywords is not null && EndKeywords.Any(kw => line.Contains(kw)))
                        CurrentMode = ParseMode.None;
                    else
                        ParseKeyValue(line, CurrentSection);
                    break;
                case ParseMode.TableHeader:
                    CurrentMode = ParseMode.Table;
                    break;
                case ParseMode.Table:
                    if (line.Length == 0)
                        CurrentMode = ParseMode.None;
                    else if ((EndKeywords is not null) && EndKeywords.Where(ek => !string.IsNullOrWhiteSpace(ek)).Any(kw => line.Contains(kw)))
                    {
                        yield return OnTableFinished();
                        CurrentMode = ParseMode.None;
                        foreach (var record in ParseNextLine(line))
                            yield return record;
                    }
                    else
                    {
                        var record = ParseTableRecord(line);
                        if (record is not null && record.IsError)
                        {
                            CurrentMode = ParseMode.None;
                            foreach (var record2 in ParseNextLine(line))
                                yield return record2;
                        }
                        else
                            yield return record;
                    }
                    break;
                case ParseMode.DynamicTable:
                    if ((EndKeywords is not null) && EndKeywords.Any(kw => line.Contains(kw)))
                    {
                        CurrentMode = ParseMode.None;
                        foreach (var record in ParseNextLine(line))
                            yield return record;
                    }
                    else
                        yield return ParseDynamicTableRecord(line);
                    break;
            }
        }
        protected virtual StatementMovement OnTableFinished()
        {
            return null;
        }
        private void InitSection()
        {
            if (CurrentSection is null)
            {
                CurrentMode = ParseMode.Ignore;
                EndKeywords = null;
                return;
            }
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
                        StopOnError = CurrentSection.Attributes["stopOnError"]?.Value == "true";
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

        private void ParseKeyValue(string line, XmlNode currentSection)
        {
            var separator = currentSection.Attributes.GetNamedItem("separator")?.Value ?? ";";
            string[] Values = line.Split(separator);
            foreach (XmlNode Key in CurrentSection.ChildNodes)
            {
                var fieldCount = Key.Attributes["name"].Value.Split(separator).Length;
                var name = string.Join(separator, Values.Take(fieldCount));
                if (name.EndsWith(Key.Attributes["name"].Value))
                {
                    string VariableName = Key.Attributes["variable"].Value;
                    ParseVariable(VariableName, Values.Skip(fieldCount).FirstOrDefault(), true, GetVariableMode(Key.Attributes["mode"].Value), 1);
                }
            }
        }
        protected void ParseVariable(StatementMovement line, string Name, string Value, VariableMode mode, int multiplier)
        {
            switch (Name)
            {
                case "BankAccountNo":
                    GlobalDraftData.AccountNumber = (string.IsNullOrWhiteSpace(GlobalDraftData.AccountNumber) || mode == VariableMode.Always) ? Value.Replace(" ", string.Empty) : GlobalDraftData.AccountNumber;
                    break;
                case "PostingDate":
                    line.BookingDate = DateTime.Parse(Value, new CultureInfo("de-DE"));
                    break;
                case "ValutaDate":
                    line.ValutaDate = DateTime.Parse(Value, new CultureInfo("de-DE"));
                    break;
                case "SourceName":
                    line.Counterparty = ApplyTextReplacements($"{line.Counterparty} {Value}".Trim());
                    break;
                case "PostingDescription":
                    line.PostingDescription = ApplyTextReplacements(Value);
                    break;
                case "Description":
                    line.Subject = ApplyTextReplacements(Value);
                    break;
                case "CurrencyCode":
                    line.CurrencyCode = Value;
                    break;
                case "Amount":
                    line.Amount = decimal.Parse(Value.Replace(" ", ""), new CultureInfo("de-DE")) * multiplier;
                    break;
            }
        }

        private string? ApplyTextReplacements(string inputText)
        {
            var node = XmlDoc.DocumentElement.ChildNodes.OfType<XmlNode>().FirstOrDefault(node => node.Name == "replacements");
            if (node is null)
                return inputText;
            foreach (var subNode in node.ChildNodes.OfType<XmlNode>())
            {
                if (string.Compare(subNode.Name, "replace", true) != 0)
                    continue;
                var search = subNode.Attributes.GetNamedItem("from")?.Value;
                var replace = subNode.Attributes.GetNamedItem("to")?.Value;
                if (string.IsNullOrWhiteSpace(search))
                    continue;
                inputText = inputText.Replace(search, replace);
            }
            return inputText;
        }

        protected void ParseVariable(string Name, string Value, bool global, VariableMode mode, int multiplier)
        {
            StatementMovement line = global ? GlobalLineData : RecordLineData;
            ParseVariable(line, Name, Value, mode, multiplier);
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

        protected virtual StatementMovement ParseTableRecord(string line)
        {
            if ((IgnoreRecordKeywords is not null) && IgnoreRecordKeywords.Any(kw => line.Contains(kw)))
                return null;
            string[] Values = line.Split(TableFieldSeparator);
            RecordLineData = new StatementMovement() { };
            int FieldIdx = Values.GetLowerBound(0);
            try
            {
                foreach (XmlNode Field in CurrentSection.ChildNodes)
                {
                    switch (Field.Name)
                    {
                        case "field":
                            FieldIdx = ParseField(Values, FieldIdx, Field);
                            break;
                        case "regExp":
                            ParseRegularExpression(line, Field);
                            break;
                    }
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                if (StopOnError)
                    return new StatementMovement() { IsError = true };
                throw;
            }
            return FinishRecord();
        }

        protected virtual void ParseRegularExpression(string input, XmlNode field)
        {
            var pattern = field.Attributes["pattern"].Value;
            var type = field.Attributes.GetNamedItem("type")?.Value;
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
                    ParseVariable(groupName, value, false, VariableMode.Always, multiplier);
                }
            }
        }

        private int ParseField(string[] Values, int FieldIdx, XmlNode Field)
        {
            string VariableName = Field.Attributes["variable"]?.Value;
            int.TryParse(Field.Attributes["length"]?.Value, out int fieldLength);
            if (!int.TryParse(Field.Attributes["multiplier"]?.Value, out int multiplier))
                multiplier = 1;
            if (TableFieldSeparator == "#None#")
            {
                if (fieldLength == 0)
                    fieldLength = Values[0].Length;
                var currentValue = Values[0].Substring(0, fieldLength);
                Values[0] = Values[0].Remove(0, fieldLength);
                ParseVariable(VariableName, currentValue, false, VariableMode.Always, multiplier);
            }
            else
            {
                ParseVariable(VariableName, Values[FieldIdx], false, VariableMode.Always, multiplier);
                FieldIdx += 1;
            }

            return FieldIdx;
        }

        protected bool IsRecordSet()
        {
            if (RecordLineData.Amount == 0 && string.IsNullOrWhiteSpace(RecordLineData.Subject) && RecordLineData.BookingDate == DateTime.MinValue)
                return false;
            return true;
        }

        private StatementMovement FinishRecord()
        {
            try
            {
                if (!IsRecordSet())
                    return null;

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
