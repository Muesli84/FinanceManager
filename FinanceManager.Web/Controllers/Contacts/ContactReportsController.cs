using FinanceManager.Application;
using FinanceManager.Application.Reports;
using FinanceManager.Domain;
using FinanceManager.Domain.Postings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers.Contacts;

/// <summary>
/// Provides aggregated time series for a single contact (postings related to a contact).
/// Delegates the actual work to <see cref="PostingReportsControllerBase"/> which implements common logic.
/// </summary>
[ApiController]
[Route("api/contacts/{contactId:guid}/aggregates")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ContactReportsController : PostingReportsControllerBase
{
    /// <summary>
    /// The posting kind exposed by this controller (Contact postings).
    /// </summary>
    protected override PostingKind Kind => PostingKind.Contact;

    /// <summary>
    /// Creates a new instance of <see cref="ContactReportsController"/>.
    /// </summary>
    /// <param name="current">Current user service used to determine ownership.</param>
    /// <param name="series">Posting time series service that provides aggregate data.</param>
    public ContactReportsController(ICurrentUserService current, IPostingTimeSeriesService series) : base(current, series) { }

    /// <summary>
    /// Returns an ordered list of aggregate time series points for the specified contact.
    /// </summary>
    /// <param name="contactId">The contact identifier to retrieve aggregates for.</param>
    /// <param name="period">Aggregation period (e.g. "Month").</param>
    /// <param name="take">Maximum number of points to return.</param>
    /// <param name="maxYearsBack">Optional limit for how many years back to consider (1..10).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>ActionResult with a read-only list of <see cref="TimeSeriesPointDto"/> or NotFound when the entity does not belong to the user.</returns>
    [HttpGet]
    public Task<ActionResult<IReadOnlyList<TimeSeriesPointDto>>> GetAsync(
        Guid contactId,
        [FromQuery] string period = "Month",
        [FromQuery] int take = 36,
        [FromQuery] int? maxYearsBack = null,
        CancellationToken ct = default)
        => GetInternalAsync(contactId, period, take, maxYearsBack, ct);
}

