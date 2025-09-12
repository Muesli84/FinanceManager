using FinanceManager.Application.Statements;
using System.Text;
using System.Text.Json;

namespace FinanceManager.Infrastructure.Statements.Reader
{
    public class BackupStatementFileReader : IStatementFileReader
    {
        private BackupData _BackupData = null;
        private StatementHeader _GlobalHeader = null;

        private sealed class BackupData
        {
            public JsonElement BankAccounts { get; set; }
            public JsonElement BankAccountLedgerEntries { get; set; }
            public JsonElement BankAccountJournalLines { get; set; }    
        }
        private void Load(byte[] fileBytes)
        {
            var fileContent = ReadContent(fileBytes);
            var offset = fileContent.IndexOf('\n');
            fileContent = fileContent.Remove(0, offset);
            _BackupData = JsonSerializer.Deserialize<BackupData>(fileContent);
            _GlobalHeader = new StatementHeader()
            {
                IBAN = _BackupData.BankAccounts[0].GetProperty("IBAN").GetString() ?? ""
            };
        }

        private string ReadContent(byte[] fileBytes)
        {
            return Encoding.UTF8.GetString(fileBytes)
                .Replace("\r\n", "\n") // Windows zu Unix
                .Replace("\r", "\n");   // Mac zu Unix;
        }
        private IEnumerable<StatementMovement> ReadData()
        {
            foreach (var entry in _BackupData.BankAccountLedgerEntries.EnumerateArray())
            {
                var movement = new StatementMovement()
                {
                    BookingDate = entry.GetProperty("PostingDate").GetDateTime(),
                    ValutaDate = entry.GetProperty("ValutaDate").GetDateTime(),
                    Amount = entry.GetProperty("Amount").GetDecimal(),
                    CurrencyCode = entry.GetProperty("CurrencyCode").GetString(),
                    Subject = entry.GetProperty("Description").GetString(),
                    Counterparty = entry.GetProperty("SourceName").GetString(),
                    PostingDescription = entry.GetProperty("PostingDescription").GetString(),
                    IsPreview = false,
                    IsError = false
                };
                if (movement.Amount != 0)
                    yield return movement;
            }

            foreach (var entry in _BackupData.BankAccountJournalLines.EnumerateArray())
            {
                var movement = new StatementMovement()
                {
                    BookingDate = entry.GetProperty("PostingDate").GetDateTime(),
                    ValutaDate = entry.GetProperty("ValutaDate").GetDateTime(),
                    Amount = entry.GetProperty("Amount").GetDecimal(),
                    CurrencyCode = entry.GetProperty("CurrencyCode").GetString(),
                    Subject = entry.GetProperty("Description").GetString(),
                    Counterparty = entry.GetProperty("SourceName").GetString(),
                    PostingDescription = entry.GetProperty("PostingDescription").GetString(),
                    IsPreview = false,
                    IsError = false
                };
                if (movement.Amount != 0)
                    yield return movement;
            }
        }
        public StatementParseResult? Parse(string fileName, byte[] fileBytes)
        {
            try
            {
                Load(fileBytes);
                return new StatementParseResult(_GlobalHeader, ReadData().ToList());
            }
            catch
            {
                return null;
            }
        }
    }
}
