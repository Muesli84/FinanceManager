using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.ViewModels;

public sealed class PostingsContactViewModel : ViewModelBase
{
    private readonly HttpClient _http;

    public PostingsContactViewModel(IServiceProvider sp, IHttpClientFactory httpFactory) : base(sp)
    {
        _http = httpFactory.CreateClient("Api");
    }

    public Guid ContactId { get; private set; }
    public bool Loaded { get; private set; }

    public string Search { get; private set; } = string.Empty;

    public bool Loading { get; private set; }
    public bool CanLoadMore { get; private set; } = true;
    public int Skip { get; private set; }

    public Guid? SelectedPostingId { get; private set; }
    public Guid? LinkedAccountId { get; private set; }
    public Guid? LinkedContactId { get; private set; }
    public Guid? LinkedPlanId { get; private set; }
    public Guid? LinkedSecurityId { get; private set; }

    public List<PostingItem> Items { get; } = new();

    public void Configure(Guid contactId)
    {
        ContactId = contactId;
    }

    public override async ValueTask InitializeAsync(CancellationToken ct = default)
    {
        if (!IsAuthenticated)
        {
            RequireAuthentication(null);
            return;
        }
        await LoadMoreAsync(ct);
        Loaded = true;
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
        Skip = 0; CanLoadMore = true; SelectedPostingId = null;
        LinkedAccountId = LinkedContactId = LinkedPlanId = LinkedSecurityId = null;
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
            var url = $"/api/postings/contact/{ContactId}?{string.Join('&', parts)}";
            var chunk = await _http.GetFromJsonAsync<List<PostingServiceDto>>(url, ct) ?? new();
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

    public void Select(Guid id)
    {
        if (SelectedPostingId == id)
        {
            SelectedPostingId = null;
            LinkedAccountId = LinkedContactId = LinkedPlanId = LinkedSecurityId = null;
            RaiseStateChanged();
            return;
        }
        SelectedPostingId = id;
        _ = ResolveSelectedLinksAsync();
    }

    private async Task ResolveSelectedLinksAsync()
    {
        LinkedAccountId = LinkedContactId = LinkedPlanId = LinkedSecurityId = null;
        var sel = Items.FirstOrDefault(i => i.Id == SelectedPostingId);
        if (sel == null) { return; }
        if (sel.GroupId == Guid.Empty)
        {
            LinkedAccountId = sel.AccountId; LinkedContactId = sel.ContactId; LinkedPlanId = sel.SavingsPlanId; LinkedSecurityId = sel.SecurityId; RaiseStateChanged(); return;
        }
        try
        {
            var dto = await _http.GetFromJsonAsync<GroupLinksResponse>($"/api/postings/group/{sel.GroupId}", CancellationToken);
            LinkedAccountId = dto?.AccountId; LinkedContactId = dto?.ContactId; LinkedPlanId = dto?.SavingsPlanId; LinkedSecurityId = dto?.SecurityId;
        }
        catch { }
        finally { RaiseStateChanged(); }
    }

    public string GetExportUrl(string format)
    {
        var parts = new List<string> { $"format={Uri.EscapeDataString(format)}" };
        if (!string.IsNullOrWhiteSpace(Search)) { parts.Add($"q={Uri.EscapeDataString(Search)}"); }
        var qs = parts.Count > 0 ? ("?" + string.Join('&', parts)) : string.Empty;
        return $"/api/postings/contact/{ContactId}/export{qs}";
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

    private static PostingItem Map(PostingServiceDto p) => new()
    {
        Id = p.Id,
        BookingDate = p.BookingDate,
        ValutaDate = p.ValutaDate,
        Amount = p.Amount,
        // convert numeric kind back to domain enum
        Kind = (PostingKind)p.Kind,
        AccountId = p.AccountId,
        ContactId = p.ContactId,
        SavingsPlanId = p.SavingsPlanId,
        SecurityId = p.SecurityId,
        GroupId = p.GroupId,
        SourceId = p.SourceId,
        Subject = p.Subject,
        RecipientName = p.RecipientName,
        Description = p.Description,
        SecuritySubType = p.SecuritySubType.HasValue ? (SecurityPostingSubType?)p.SecuritySubType.Value : null,
        Quantity = p.Quantity,
        // linked posting fields
        LinkedPostingId = p.LinkedPostingId,
        LinkedPostingKind = p.LinkedPostingKind.HasValue ? (PostingKind?)p.LinkedPostingKind.Value : null,
        LinkedPostingAccountId = p.LinkedPostingAccountId,
        LinkedPostingAccountSymbolAttachmentId = p.LinkedPostingAccountSymbolAttachmentId,
        LinkedPostingAccountName = p.LinkedPostingAccountName,
        // bank posting for this posting
        BankPostingAccountId = p.BankPostingAccountId,
        BankPostingAccountSymbolAttachmentId = p.BankPostingAccountSymbolAttachmentId,
        BankPostingAccountName = p.BankPostingAccountName
    };

    public sealed record GroupLinksResponse(Guid? AccountId, Guid? ContactId, Guid? SavingsPlanId, Guid? SecurityId);

    public sealed class PostingItem
    {
        public Guid Id { get; set; }
        public DateTime BookingDate { get; set; }
        public DateTime ValutaDate { get; set; }
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
        public decimal? Quantity { get; set; }

        // linked posting metadata
        public Guid? LinkedPostingId { get; set; }
        public PostingKind? LinkedPostingKind { get; set; }
        public Guid? LinkedPostingAccountId { get; set; }
        public Guid? LinkedPostingAccountSymbolAttachmentId { get; set; }
        public string? LinkedPostingAccountName { get; set; }

        // bank posting for this posting
        public Guid? BankPostingAccountId { get; set; }
        public Guid? BankPostingAccountSymbolAttachmentId { get; set; }
        public string? BankPostingAccountName { get; set; }
    }
}
