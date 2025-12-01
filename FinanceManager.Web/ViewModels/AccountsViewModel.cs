using Microsoft.Extensions.Localization;
using FinanceManager.Shared.Dtos.Accounts; // use shared AccountDto
using FinanceManager.Shared.Dtos.Contacts; // use shared ContactDto and ContactCategoryDto

namespace FinanceManager.Web.ViewModels;

public sealed class AccountsViewModel : ViewModelBase
{
    private readonly HttpClient _http;

    public AccountsViewModel(IServiceProvider sp, IHttpClientFactory httpFactory) : base(sp)
    {
        _http = httpFactory.CreateClient("Api");
    }

    public bool Loaded { get; private set; }
    public Guid? FilterBankContactId { get; private set; }

    public List<AccountItem> Accounts { get; } = new();

    public void SetFilter(Guid? bankContactId)
    {
        if (FilterBankContactId != bankContactId)
        {
            FilterBankContactId = bankContactId;
            RaiseStateChanged();
        }
    }

    public override async ValueTask InitializeAsync(CancellationToken ct = default)
    {
        if (!IsAuthenticated)
        {
            RequireAuthentication(null);
            return;
        }
        await LoadAsync(ct);
        Loaded = true;
        RaiseStateChanged();
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (!IsAuthenticated) { return; }
        var url = "/api/accounts";
        if (FilterBankContactId.HasValue) { url += $"?bankContactId={FilterBankContactId}"; }
        try
        {
            var resp = await _http.GetAsync(url, ct);
            if (resp.IsSuccessStatusCode)
            {
                var list = await resp.Content.ReadFromJsonAsync<List<AccountDto>>(cancellationToken: ct) ?? new();
                Accounts.Clear();
                Accounts.AddRange(list.Select(d => new AccountItem
                {
                    Id = d.Id,
                    Name = d.Name,
                    Type = d.Type.ToString(),
                    Iban = d.Iban,
                    CurrentBalance = d.CurrentBalance,
                    SymbolAttachmentId = d.SymbolAttachmentId,
                    BankContactId = d.BankContactId
                }));

                // For accounts without a symbol, try to fetch the symbol from the associated bank contact
                var needContactIds = Accounts
                    .Where(a => a.SymbolAttachmentId == null && a.BankContactId != Guid.Empty)
                    .Select(a => a.BankContactId)
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToList();

                if (needContactIds.Count > 0)
                {
                    var tasks = needContactIds.Select(async cid =>
                    {
                        try
                        {
                            var contact = await _http.GetFromJsonAsync<ContactDto>($"/api/contacts/{cid}", ct);
                            return (cid, contact?.SymbolAttachmentId, contact?.CategoryId);
                        }
                        catch
                        {
                            return (cid, (Guid?)null, (Guid?)null);
                        }
                    });

                    var results = await Task.WhenAll(tasks);

                    // Map contactId -> contactSymbol and contactId -> categoryId
                    var contactSymbolMap = results.Where(r => r.Item2.HasValue).ToDictionary(r => r.cid, r => r.Item2.Value);
                    var contactCategoryMap = results.Where(r => r.Item3.HasValue).ToDictionary(r => r.cid, r => r.Item3.Value);

                    // collect category ids we need to query
                    var needCategoryIds = contactCategoryMap.Values
                        .Where(id => id != Guid.Empty)
                        .Distinct()
                        .ToList();

                    Dictionary<Guid, Guid?> categorySymbolMap = new();

                    if (needCategoryIds.Count > 0)
                    {
                        var catTasks = needCategoryIds.Select(async catId =>
                        {
                            try
                            {
                                var cat = await _http.GetFromJsonAsync<ContactCategoryDto>($"/api/contact-categories/{catId}", ct);
                                return (catId, cat?.SymbolAttachmentId);
                            }
                            catch
                            {
                                return (catId, (Guid?)null);
                            }
                        });

                        var catResults = await Task.WhenAll(catTasks);
                        categorySymbolMap = catResults.ToDictionary(r => r.catId, r => r.Item2);
                    }

                    foreach (var acc in Accounts)
                    {
                        if (acc.SymbolAttachmentId == null && acc.BankContactId != Guid.Empty)
                        {
                            if (contactSymbolMap.TryGetValue(acc.BankContactId, out var csid))
                            {
                                acc.ContactSymbolAttachmentId = csid;
                            }
                            else if (contactCategoryMap.TryGetValue(acc.BankContactId, out var catId) && catId != Guid.Empty)
                            {
                                if (categorySymbolMap.TryGetValue(catId, out var catSym) && catSym.HasValue)
                                {
                                    acc.CategorySymbolAttachmentId = catSym.Value;
                                }
                            }
                        }
                    }
                }

                RaiseStateChanged();
            }
        }
        catch { }
    }

    public override IReadOnlyList<UiRibbonGroup> GetRibbon(IStringLocalizer localizer)
    {
        var items = new List<UiRibbonItem>
        {
            new UiRibbonItem(localizer["Ribbon_New"], "<svg><use href='/icons/sprite.svg#plus'/></svg>", UiRibbonItemSize.Large, false, "New")
        };
        if (FilterBankContactId.HasValue)
        {
            items.Add(new UiRibbonItem(localizer["Ribbon_ClearFilter"], "<svg><use href='/icons/sprite.svg#clear'/></svg>", UiRibbonItemSize.Small, false, "ClearFilter"));
        }
        return new List<UiRibbonGroup>
        {
            new UiRibbonGroup(localizer["Ribbon_Group_Actions"], items)
        };
    }

    public sealed class AccountItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string? Iban { get; set; }
        public decimal CurrentBalance { get; set; }
        public Guid? SymbolAttachmentId { get; set; }
        public Guid BankContactId { get; set; }

        // symbol from associated contact (used when account has no own symbol)
        public Guid? ContactSymbolAttachmentId { get; set; }

        // symbol from associated contact category (used when neither account nor contact has a symbol)
        public Guid? CategorySymbolAttachmentId { get; set; }

        public Guid? DisplaySymbolAttachmentId => SymbolAttachmentId ?? ContactSymbolAttachmentId ?? CategorySymbolAttachmentId;
    }
}
