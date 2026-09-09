namespace FinanceManager.Shared.Dtos.Users;

/// <summary>
/// Represents a request to initiate a demo operation, specifying whether postings should be created.
/// </summary>
/// <param name="createPostings">true to create postings as part of the demo operation; otherwise, false.</param>
/// <returns>The result.</returns>
public sealed record DemoRequest(bool createPostings);