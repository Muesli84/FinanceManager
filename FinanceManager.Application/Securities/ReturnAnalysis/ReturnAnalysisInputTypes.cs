namespace FinanceManager.Application.Securities.ReturnAnalysis;

/// <summary>
/// Single period input for TWR calculation (Modified Dietz).
/// </summary>
/// <param name="Start">Start of the period.</param>
/// <param name="End">End of the period.</param>
/// <param name="StartValue">Portfolio value at period start.</param>
/// <param name="EndValue">Portfolio value at period end.</param>
/// <param name="ExternalCashflow">External cashflow during the period (positive = inflow, negative = outflow).</param>
/// <returns>The result.</returns>
public sealed record TwrPeriodInput(DateTime Start, DateTime End, decimal StartValue, decimal EndValue, decimal ExternalCashflow);

/// <summary>
/// A single cashflow point for IRR calculation.
/// </summary>
/// <param name="Date">Date of the cashflow.</param>
/// <param name="Amount">Amount (negative = outflow/investment, positive = inflow/return).</param>
/// <returns>The result.</returns>
public sealed record CashflowPoint(DateTime Date, decimal Amount);

/// <summary>
/// Security transaction for FIFO cost basis calculation.
/// </summary>
/// <param name="Id">Unique posting id (used as tiebreaker for same-date transactions).</param>
/// <param name="Date">Booking date.</param>
/// <param name="Type">Transaction sub-type (Buy, Sell, Dividend, Tax, Fee).</param>
/// <param name="Amount">Monetary amount (negative for outflows, positive for inflows).</param>
/// <param name="Quantity">Share quantity (positive for Buy, positive for Sell).</param>
/// <param name="GroupId">Group identifier linking related transactions (e.g., dividend + tax).</param>
/// <returns>The result.</returns>
public sealed record SecurityTransaction(Guid Id, DateTime Date, SecurityPostingSubType Type, decimal Amount, decimal? Quantity, Guid GroupId);
