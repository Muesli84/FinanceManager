using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.ViewModels;

public sealed class PostingDetailViewModel : ViewModelBase
{
    private readonly HttpClient _http;

    public PostingDetailViewModel(IServiceProvider sp, IHttpClientFactory httpFactory) : base(sp)
    {
        _http = httpFactory.CreateClient("Api");
    }

    public Guid Id { get; private set; }

    public bool Loading { get; private set; }
    public bool LinksLoading { get; private set; }

    public PostingDto? Detail { get; private set; }

    public Guid? LinkedAccountId { get; private set; }
    public Guid? LinkedContactId { get; private set; }
    public Guid? LinkedPlanId { get; private set; }
    public Guid? LinkedSecurityId { get; private set; }

    public void Configure(Guid id)
    {
        Id = id;
    }

    public override async ValueTask InitializeAsync(CancellationToken ct = default)
    {
        if (!IsAuthenticated)
        {
            RequireAuthentication(null);
            return;
        }
        await LoadAsync(ct);
        RaiseStateChanged();
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (Loading) { return; }
        Loading = true; RaiseStateChanged();
        try
        {
            var dto = await _http.GetFromJsonAsync<PostingDto>($"/api/postings/{Id}", ct);
            Detail = dto;
            if (Detail != null)
            {
                await ResolveGroupLinksAsync(ct);
            }
        }
        catch { }
        finally { Loading = false; RaiseStateChanged(); }
    }

    private async Task ResolveGroupLinksAsync(CancellationToken ct)
    {
        LinkedAccountId = Detail?.AccountId;
        LinkedContactId = Detail?.ContactId;
        LinkedPlanId = Detail?.SavingsPlanId;
        LinkedSecurityId = Detail?.SecurityId;
        if (Detail == null || Detail.GroupId == Guid.Empty) { return; }
        try
        {
            LinksLoading = true; RaiseStateChanged();
            var dto = await _http.GetFromJsonAsync<GroupLinksResponse>($"/api/postings/group/{Detail.GroupId}", ct);
            if (dto != null)
            {
                LinkedAccountId = dto.AccountId ?? LinkedAccountId;
                LinkedContactId = dto.ContactId ?? LinkedContactId;
                LinkedPlanId = dto.SavingsPlanId ?? LinkedPlanId;
                LinkedSecurityId = dto.SecurityId ?? LinkedSecurityId;
            }
        }
        catch { }
        finally { LinksLoading = false; RaiseStateChanged(); }
    }

    public override IReadOnlyList<UiRibbonGroup> GetRibbon(IStringLocalizer localizer)
    {
        var nav = new UiRibbonGroup(localizer["Ribbon_Group_Navigation"], new()
        {
            new UiRibbonItem(localizer["Ribbon_Back"], "<svg><use href='/icons/sprite.svg#back'/></svg>", UiRibbonItemSize.Large, false, "Back")
        });
        var linked = new UiRibbonGroup(localizer["Ribbon_Group_Linked"], new()
        {
            new UiRibbonItem(localizer["Ribbon_OpenAccount"], "<svg><use href='/icons/sprite.svg#external'/></svg>", UiRibbonItemSize.Small, LinksLoading || !HasId(LinkedAccountId), "OpenAccount"),
            new UiRibbonItem(localizer["Ribbon_OpenContact"], "<svg><use href='/icons/sprite.svg#external'/></svg>", UiRibbonItemSize.Small, LinksLoading || !HasId(LinkedContactId), "OpenContact"),
            new UiRibbonItem(localizer["Ribbon_OpenSavingsPlan"], "<svg><use href='/icons/sprite.svg#external'/></svg>", UiRibbonItemSize.Small, LinksLoading || !HasId(LinkedPlanId), "OpenSavingsPlan"),
            new UiRibbonItem(localizer["Ribbon_OpenSecurity"], "<svg><use href='/icons/sprite.svg#external'/></svg>", UiRibbonItemSize.Small, LinksLoading || !HasId(LinkedSecurityId), "OpenSecurity")
        });
        return new List<UiRibbonGroup> { nav, linked };
    }

    private static bool HasId(Guid? id) => id.HasValue && id.Value != Guid.Empty;

    public sealed record PostingDto(Guid Id, DateTime BookingDate, decimal Amount, int Kind, Guid? AccountId, Guid? ContactId, Guid? SavingsPlanId, Guid? SecurityId, Guid SourceId, string? Subject, string? RecipientName, string? Description, int? SecuritySubType, decimal? Quantity, Guid GroupId);
    public sealed record GroupLinksResponse(Guid? AccountId, Guid? ContactId, Guid? SavingsPlanId, Guid? SecurityId);
}
