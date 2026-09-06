namespace FinanceManager.Domain;

/// <summary>
/// Represents the outcome of an operation that can either succeed or fail.
/// Contains a boolean <see cref="Success"/> flag and an optional error message.
/// </summary>
/// <param name="Success">Indicates whether the operation succeeded.</param>
/// <param name="Error">Error message when the operation failed; <c>null</c> when <see cref="Success"/> is <c>true</c>.</param>
/// <returns>The result.</returns>
public sealed record Result(bool Success, string? Error)
{
    /// <summary>
    /// Returns a successful <see cref="Result"/> instance.
    /// </summary>
    /// <returns>A <see cref="Result"/> with <see cref="Success"/> set to <c>true</c> and <see cref="Error"/> set to <c>null</c>.</returns>
    public static Result Ok()
        => new(true, null);

    /// <summary>
    /// Returns a failed <see cref="Result"/> instance with the specified error message.
    /// </summary>
    /// <param name="error">A description of the failure. It is recommended to provide a non-empty message for diagnostics.</param>
    /// <returns>A <see cref="Result"/> with <see cref="Success"/> set to <c>false</c> and <see cref="Error"/> set to the provided message.</returns>
    public static Result Fail(string error)
        => new(false, error);
}

/// <summary>
/// Represents the outcome of an operation that can either succeed with a value of type <typeparamref name="T"/>
/// or fail with an error message.
/// </summary>
/// <typeparam name="T">Type of the value returned on success.</typeparam>
/// <param name="Success">Indicates whether the operation succeeded.</param>
/// <param name="Value">Value produced by a successful operation; may be <c>null</c> for reference types or when default is intended.</param>
/// <param name="Error">Error message when the operation failed; <c>null</c> when <see cref="Success"/> is <c>true</c>.</param>
/// <returns>The result.</returns>
public sealed record Result<T>(bool Success, T? Value, string? Error)
{
    /// <summary>
    /// Returns a successful <see cref="Result{T}"/> containing the specified value.
    /// </summary>
    /// <param name="value">The value produced by a successful operation. May be <c>null</c> for reference types.</param>
    /// <returns>A <see cref="Result{T}"/> with <see cref="Success"/> set to <c>true</c>, <see cref="Value"/> set to <paramref name="value"/>, and <see cref="Error"/> set to <c>null</c>.</returns>
    public static Result<T> Ok(T value)
        => new(true, value, null);

    /// <summary>
    /// Returns a failed <see cref="Result{T}"/> with the specified error message.
    /// </summary>
    /// <param name="error">A description of the failure. It is recommended to provide a non-empty message for diagnostics.</param>
    /// <returns>A <see cref="Result{T}"/> with <see cref="Success"/> set to <c>false</c>, <see cref="Value"/> set to default, and <see cref="Error"/> set to the provided message.</returns>
    public static Result<T> Fail(string error)
        => new(false, default, error);
}
