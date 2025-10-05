using System.Net.Http.Json;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.ViewModels;

public sealed class PostingsSavingsPlanViewModel : ViewModelBase
{
    private readonly HttpClient _http;

    public PostingsSavingsPlanViewModel(IServiceProvider sp, IHttpClientFactory httpFactory) : base(sp)
    {
        _http = httpFactory.CreateClient("Api");
    }

    public Guid PlanId { get; private set; }

    public string Search { get; private set; } = string.Empty;

    public bool Loading { get; private set; }
    public bool CanLoadMore { get; private set; } = true;
    public int Skip { get; private set; }

    public List<PostingItem> Items { get; } = new();

    public void Configure(Guid planId)
    {
        PlanId = planId;
    }

    public override async ValueTask InitializeAsync(CancellationToken ct = default)
    {
        if (!IsAuthenticated)
        {
            RequireAuthentication(null);
            return;
        }
        await LoadMoreAsync(ct);
        RaiseStateChanged();
    }

    public void SetSearch(string search)
    {
        if (Search != search)
        {
            Search = search ?? string.Empty;
        }
    }

    public void ResetAndSearch()
    {
        Items.Clear();
        Skip = 0; CanLoadMore = true;
        RaiseStateChanged();
    }

    public async Task LoadMoreAsync(CancellationToken ct = default)
    {
        if (Loading || !CanLoadMore) { return; }
        Loading = true; RaiseStateChanged();
        try
        {
            var firstPage = Skip == 0;
            var parts = new List<string> { $"skip={Skip}", "take=50" };
            if (!string.IsNullOrWhiteSpace(Search)) { parts.Add($"q={Uri.EscapeDataString(Search)}"); }
            var url = $"/api/postings/savings-plan/{PlanId}?{string.Join('&', parts)}";
            var chunk = await _http.GetFromJsonAsync<List<PostingDto>>(url, ct) ?? new();
            Items.AddRange(chunk.Select(Map));
            Skip += chunk.Count;
            if (chunk.Count == 0 || (!firstPage && chunk.Count < 50)) { CanLoadMore = false; }
        }
        catch { }
        finally { Loading = false; RaiseStateChanged(); }
    }

    public void ClearSearch()
    {
        Search = string.Empty;
        ResetAndSearch();
    }

    public string GetExportUrl(string format)
    {
        var parts = new List<string> { $"format={Uri.EscapeDataString(format)}" };
        if (!string.IsNullOrWhiteSpace(Search)) { parts.Add($"q={Uri.EscapeDataString(Search)}"); }
        var qs = parts.Count > 0 ? ("?" + string.Join('&', parts)) : string.Empty;
        return $"/api/postings/savings-plan/{PlanId}/export{qs}";
    }

    public override IReadOnlyList<UiRibbonGroup> GetRibbon(IStringLocalizer localizer)
    {
        var nav = new UiRibbonGroup(localizer["Ribbon_Group_Navigation"], new()
        {
            new UiRibbonItem(localizer["Ribbon_Back"], "<svg><use href='/icons/sprite.svg#back'/></svg>", UiRibbonItemSize.Large, false, "Back")
        });
        var filter = new UiRibbonGroup(localizer["Ribbon_Group_Filter"], new()
        {
            new UiRibbonItem(localizer["Ribbon_ClearSearch"], "<svg><use href='/icons/sprite.svg#clear'/></svg>", UiRibbonItemSize.Small, string.IsNullOrWhiteSpace(Search), "ClearSearch")
        });
        var export = new UiRibbonGroup(localizer["Ribbon_Group_Export"], new()
        {
            new UiRibbonItem(localizer["Ribbon_ExportCsv"], "<svg><use href='/icons/sprite.svg#download'/></svg>", UiRibbonItemSize.Small, Loading, "ExportCsv"),
            new UiRibbonItem(localizer["Ribbon_ExportExcel"], "<svg><use href='/icons/sprite.svg#download'/></svg>", UiRibbonItemSize.Small, Loading, "ExportXlsx")
        });
        return new List<UiRibbonGroup> { nav, filter, export };
    }

    private static PostingItem Map(PostingDto p) => new()
    {
        Id = p.Id,
        BookingDate = p.BookingDate,
        Amount = p.Amount,
        Kind = p.Kind,
        AccountId = p.AccountId,
        ContactId = p.ContactId,
        SavingsPlanId = p.SavingsPlanId,
        SecurityId = p.SecurityId,
        GroupId = p.GroupId ?? Guid.Empty,
        SourceId = p.SourceId,
        Subject = p.Subject,
        RecipientName = p.RecipientName,
        Description = p.Description,
        SecuritySubType = p.SecuritySubType
    };

    public sealed record PostingDto(Guid Id, DateTime BookingDate, decimal Amount, PostingKind Kind, Guid? AccountId, Guid? ContactId, Guid? SavingsPlanId, Guid? SecurityId, Guid? GroupId, Guid SourceId, string? Subject, string? RecipientName, string? Description, SecurityPostingSubType? SecuritySubType);

    public enum PostingKind { Bank=0, Contact=1, SavingsPlan=2, Security=3 }
    public enum SecurityPostingSubType { Buy=0, Sell=1, Dividend=2, Fee=3, Tax=4 }

    public sealed class PostingItem
    {
        public Guid Id { get; set; }
        public DateTime BookingDate { get; set; }
        public decimal Amount { get; set; }
        public PostingKind Kind { get; set; }
        public Guid? AccountId { get; set; }
        public Guid? ContactId { get; set; }
        public Guid? SavingsPlanId { get; set; }
        public Guid? SecurityId { get; set; }
        public Guid GroupId { get; set; }
        public Guid SourceId { get; set; }
        public string? Subject { get; set; }
        public string? RecipientName { get; set; }
        public string? Description { get; set; }
        public SecurityPostingSubType? SecuritySubType { get; set; }
    }
}
