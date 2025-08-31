namespace FinanceManager.Domain;

internal static class Guards
{
    public static string NotNullOrWhiteSpace(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value cannot be null or whitespace.", paramName);
        return value;
    }

    public static Guid NotEmpty(Guid value, string paramName)
    {
        if (value == Guid.Empty) throw new ArgumentException("Value cannot be empty GUID.", paramName);
        return value;
    }

    public static decimal NotZero(decimal value, string paramName)
    {
        if (value == 0m) throw new ArgumentException("Value cannot be zero.", paramName);
        return value;
    }
}