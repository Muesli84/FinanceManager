namespace FinanceManager.Application.Accounts;

using System.Globalization;

/// <summary>
/// Normalizes and validates the account list/statistics search text.
/// </summary>
public sealed record AccountSearchText(string Text, string NameSearchText, string IbanSearchText)
{
    /// <summary>Maximum allowed search text length after trimming.</summary>
    public const int MaxLength = 200;

    /// <summary>
    /// Returns a normalized search object, or <c>null</c> when no filter should be applied.
    /// </summary>
    /// <param name="q">Raw query text.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the trimmed query exceeds <see cref="MaxLength"/>.</exception>
    public static AccountSearchText? Normalize(string? q)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return null;
        }

        var trimmed = q.Trim();
        if (trimmed.Length > MaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(q), $"Search text must be at most {MaxLength} characters.");
        }

        return new AccountSearchText(trimmed, trimmed.ToLowerInvariant(), NormalizeIban(trimmed));
    }

    /// <summary>
    /// Applies the account search contract independent from database provider collation or SQL functions.
    /// </summary>
    public bool Matches(string? name, string? iban)
    {
        var nameMatches = !string.IsNullOrEmpty(name)
            && CultureInfo.InvariantCulture.CompareInfo.IndexOf(name, Text, CompareOptions.IgnoreCase) >= 0;
        if (IbanSearchText.Length == 0)
        {
            return nameMatches;
        }

        return nameMatches
            || (!string.IsNullOrEmpty(iban)
                && NormalizeIban(iban).Contains(IbanSearchText, StringComparison.Ordinal));
    }

    /// <summary>
    /// Normalizes IBAN fragments by ignoring casing, ASCII spaces, non-breaking spaces and hyphens.
    /// </summary>
    public static string NormalizeIban(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        Span<char> buffer = value.Length <= 256 ? stackalloc char[value.Length] : new char[value.Length];
        var length = 0;
        foreach (var ch in value)
        {
            if (ch is ' ' or '\u00A0' or '-')
            {
                continue;
            }

            buffer[length++] = char.ToUpperInvariant(ch);
        }

        return new string(buffer[..length]);
    }
}
