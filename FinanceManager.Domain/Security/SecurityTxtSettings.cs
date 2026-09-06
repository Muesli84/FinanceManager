namespace FinanceManager.Domain.Security;

/// <summary>
/// Stores the configurable security.txt directives.
/// </summary>
public sealed class SecurityTxtSettings : Entity, IAggregateRoot
{
    private SecurityTxtSettings()
    {
    }

    /// <summary>
    /// Creates a placeholder settings row before the admin has configured security.txt.
    /// </summary>
    /// <returns>An unconfigured settings instance.</returns>
    public static SecurityTxtSettings CreateUnconfigured()
    {
        var settings = new SecurityTxtSettings
        {
            Contact = string.Empty,
            Expires = DateTimeOffset.MaxValue
        };
        return settings;
    }

    /// <summary>
    /// Creates a new settings instance.
    /// </summary>
    /// <param name="contact">The contact.</param>
    /// <param name="expires">The expires.</param>
    public SecurityTxtSettings(string contact, DateTimeOffset expires)
    {
        Contact = Guards.NotNullOrWhiteSpace(contact, nameof(contact)).Trim();
        EnsureFutureExpires(expires);
        Expires = expires;
    }

    /// <summary>Contact directive.</summary>
    public string Contact { get; private set; } = string.Empty;
    /// <summary>Expires directive.</summary>
    public DateTimeOffset Expires { get; private set; }
    /// <summary>Encryption directive.</summary>
    public string? Encryption { get; private set; }
    /// <summary>Acknowledgments directive.</summary>
    public string? Acknowledgments { get; private set; }
    /// <summary>Preferred-Languages directive.</summary>
    public string? PreferredLanguages { get; private set; }
    /// <summary>Policy directive.</summary>
    public string? Policy { get; private set; }
    /// <summary>Hiring directive.</summary>
    public string? Hiring { get; private set; }
    /// <summary>Canonical directive.</summary>
    public string? Canonical { get; private set; }

    /// <summary>Updates all directives.</summary>
    /// <param name="directives">The directives.</param>
    public void Update(SecurityTxtDirectives directives)
    {
        ArgumentNullException.ThrowIfNull(directives);
        Contact = Guards.NotNullOrWhiteSpace(directives.Contact, nameof(directives.Contact)).Trim();
        EnsureFutureExpires(directives.Expires);
        Expires = directives.Expires;
        Encryption = Normalize(directives.Encryption);
        Acknowledgments = Normalize(directives.Acknowledgments);
        PreferredLanguages = Normalize(directives.PreferredLanguages);
        Policy = Normalize(directives.Policy);
        Hiring = Normalize(directives.Hiring);
        Canonical = Normalize(directives.Canonical);
        Touch();
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void EnsureFutureExpires(DateTimeOffset expires)
    {
        if (expires <= DateTimeOffset.UtcNow)
        {
            throw new ArgumentOutOfRangeException(nameof(expires), "Expires must be in the future.");
        }
    }
}
