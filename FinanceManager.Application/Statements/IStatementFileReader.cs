using System;
using System.Collections.Generic;

namespace FinanceManager.Application.Statements;

/// <summary>
/// Interface for reading and parsing bank statement files.
/// </summary>
public interface IStatementFileReader
{
    /// <summary>
    /// Tries to parse the given file and returns header info and movements if successful.
    /// </summary>
    /// <param name="fileName">Original file name (for format detection).</param>
    /// <param name="fileBytes">File content as byte array.</param>
    /// <returns>ParseResult with header and movements, or null if format not supported.</returns>
    StatementParseResult? Parse(string fileName, byte[] fileBytes);
}

/// <summary>
/// Result of parsing a statement file.
/// </summary>
public sealed record StatementParseResult(
    StatementHeader Header,
    IReadOnlyList<StatementMovement> Movements
);

/// <summary>
/// Header data extracted from the statement file.
/// </summary>
public record StatementHeader()
{
    public string AccountNumber { get; set; }
    public string? IBAN { get; set; }
    public string? BankCode { get; set; }
    public string? AccountHolder { get; set; }
    public DateTime? PeriodStart { get; set; }
    public DateTime? PeriodEnd { get; set; }
};

/// <summary>
/// Single movement/transaction from the statement file.
/// </summary>
public sealed record StatementMovement()
{
    public DateTime BookingDate { get; set; }
    public decimal Amount { get; set; }
    public string? Subject { get; set; }
    public string? Counterparty { get; set; }
    public DateTime ValutaDate { get; set; }
    public string? PostingDescription { get; set; }
    public string? CurrencyCode { get; set; }
    public bool IsPreview { get; set; }
};