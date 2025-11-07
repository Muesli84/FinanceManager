using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using FinanceManager.Domain.Attachments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.ViewModels;

public sealed class AccountDetailViewModel : ViewModelBase
{
    private readonly HttpClient _http;

    public AccountDetailViewModel(IServiceProvider sp, IHttpClientFactory httpFactory) : base(sp)
    {
        _http = httpFactory.CreateClient("Api");
    }

    // Identity / status
    public Guid? AccountId { get; private set; }
    public bool IsNew => !AccountId.HasValue;
    public bool ShowCharts => !IsNew; // derived visibility

    private bool _loaded;
    public bool Loaded { get => _loaded; private set { if (_loaded != value) { _loaded = value; RaiseStateChanged(); } } }

    private bool _busy;
    public bool Busy { get => _busy; private set { if (_busy != value) { _busy = value; RaiseStateChanged(); } } }

    private string? _error;
    public string? Error { get => _error; private set { if (_error != value) { _error = value; RaiseStateChanged(); } } }

    // Form fields
    [Required, MinLength(2)]
    public string Name { get => _name; set { if (_name != value) { _name = value; RaiseStateChanged(); } } }
    private string _name = string.Empty;

    [Required]
    public AccountType Type { get => _type; set { if (_type != value) { _type = value; RaiseStateChanged(); } } }
    private AccountType _type = AccountType.Giro;

    public string? Iban { get => _iban; set { if (_iban != value) { _iban = value; RaiseStateChanged(); } } }
    private string? _iban;

    public Guid? BankContactId { get => _bankContactId; set { if (_bankContactId != value) { _bankContactId = value; if (_bankContactId.HasValue) { NewBankContactName = null; } RaiseStateChanged(); } } }
    private Guid? _bankContactId;

    public string? NewBankContactName { get => _newBankContactName; set { if (_newBankContactName != value) { _newBankContactName = value; RaiseStateChanged(); } } }
    private string? _newBankContactName;

    // New: SymbolAttachmentId field
    public Guid? SymbolAttachmentId { get => _symbolAttachmentId; set { if (_symbolAttachmentId != value) { _symbolAttachmentId = value; RaiseStateChanged(); } } }
    private Guid? _symbolAttachmentId;

    // New: SavingsPlanExpectation
    public FinanceManager.Domain.Accounts.SavingsPlanExpectation SavingsPlanExpectation { get => _savingsPlanExpectation; set { if (_savingsPlanExpectation != value) { _savingsPlanExpectation = value; RaiseStateChanged(); } } }
    private FinanceManager.Domain.Accounts.SavingsPlanExpectation _savingsPlanExpectation = FinanceManager.Domain.Accounts.SavingsPlanExpectation.Optional;

    // Related state
    private bool _showAttachments;
    public bool ShowAttachments { get => _showAttachments; set { if (_showAttachments != value) { _showAttachments = value; RaiseStateChanged(); } } }

    public List<BankContactVm> BankContacts { get; } = new();

    public void ForAccount(Guid? accountId)
    {
        AccountId = accountId;
    }

    public override async ValueTask InitializeAsync(CancellationToken ct = default)
    {
        await LoadBankContactsAsync(ct);
        if (!IsNew)
        {
            await LoadAsync(ct);
        }
        Loaded = true;
    }

    private async Task LoadBankContactsAsync(CancellationToken ct)
    {
        try
        {
            var resp = await _http.GetAsync("/api/contacts?type=Bank&all=true", ct);
            if (resp.IsSuccessStatusCode)
            {
                var list = await resp.Content.ReadFromJsonAsync<List<ContactDto>>(cancellationToken: ct) ?? new();
                BankContacts.Clear();
                BankContacts.AddRange(list.Select(c => new BankContactVm { Id = c.Id, Name = c.Name }).OrderBy(c => c.Name));
                RaiseStateChanged();
            }
        }
        catch { }
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        if (!AccountId.HasValue) { return; }
        try
        {
            var resp = await _http.GetAsync($"/api/accounts/{AccountId}", ct);
            if (resp.IsSuccessStatusCode)
            {
                var dto = await resp.Content.ReadFromJsonAsync<AccountDto>(cancellationToken: ct);
                if (dto != null)
                {
                    Name = dto.Name;
                    Type = dto.Type;
                    Iban = dto.Iban;
                    BankContactId = dto.BankContactId;
                    SymbolAttachmentId = dto.SymbolAttachmentId; // new
                    SavingsPlanExpectation = dto.SavingsPlanExpectation; // new
                }
            }
            else if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Error = "ErrorNotFound"; // Page localizes
            }
            else
            {
                Error = "ErrorLoadFailed";
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    public bool CanSave => !Busy && !string.IsNullOrWhiteSpace(Name) && Name.Trim().Length >= 2;

    public async Task<Guid?> SaveAsync(CancellationToken ct = default)
    {
        Busy = true; Error = null;
        try
        {
            var payload = new
            {
                Name,
                Type,
                Iban,
                BankContactId,
                NewBankContactName,
                SymbolAttachmentId, // include symbol attachment id
                SavingsPlanExpectation // include expectation
            };
            if (IsNew)
            {
                var resp = await _http.PostAsJsonAsync("/api/accounts", payload, ct);
                if (resp.IsSuccessStatusCode)
                {
                    var dto = await resp.Content.ReadFromJsonAsync<AccountDto>(cancellationToken: ct);
                    if (dto != null)
                    {
                        AccountId = dto.Id; // update context
                        return dto.Id;
                    }
                }
                else
                {
                    Error = await resp.Content.ReadAsStringAsync(ct);
                }
            }
            else
            {
                var resp = await _http.PutAsJsonAsync($"/api/accounts/{AccountId}", new { Name, Iban, BankContactId, NewBankContactName, SymbolAttachmentId, SavingsPlanExpectation }, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    Error = await resp.Content.ReadAsStringAsync(ct);
                }
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Busy = false;
        }
        return null;
    }

    public async Task DeleteAsync(CancellationToken ct = default)
    {
        if (IsNew || !AccountId.HasValue) { return; }
        Busy = true; Error = null;
        try
        {
            var resp = await _http.DeleteAsync($"/api/accounts/{AccountId}", ct);
            if (!resp.IsSuccessStatusCode)
            {
                Error = await resp.Content.ReadAsStringAsync(ct);
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    // Ribbon structure
    public override IReadOnlyList<UiRibbonGroup> GetRibbon(IStringLocalizer localizer)
    {
        var editItems = new List<UiRibbonItem>
        {
            new UiRibbonItem(localizer["Ribbon_Save"], "<svg><use href='/icons/sprite.svg#save'/></svg>", UiRibbonItemSize.Large, !CanSave, "Save")
        };
        if (!IsNew)
        {
            editItems.Add(new UiRibbonItem(localizer["Ribbon_Delete"], "<svg><use href='/icons/sprite.svg#delete'/></svg>", UiRibbonItemSize.Small, Busy, "Delete"));
        }

        var groups = new List<UiRibbonGroup>
        {
            new UiRibbonGroup(localizer["Ribbon_Group_Navigation"], new List<UiRibbonItem>
            {
                new UiRibbonItem(localizer["Ribbon_Back"], "<svg><use href='/icons/sprite.svg#back'/></svg>", UiRibbonItemSize.Large, false, "Back")
            }),
            new UiRibbonGroup(localizer["Ribbon_Group_Edit"], editItems)
        };

        if (!IsNew)
        {
            groups.Add(new UiRibbonGroup(localizer["Ribbon_Group_Related"], new List<UiRibbonItem>
            {
                new UiRibbonItem(localizer["Ribbon_BankContact"], "<svg><use href='/icons/sprite.svg#bank'/></svg>", UiRibbonItemSize.Small, Busy || !BankContactId.HasValue, "OpenBankContact"),
                new UiRibbonItem(localizer["Ribbon_Postings"], "<svg><use href='/icons/sprite.svg#postings'/></svg>", UiRibbonItemSize.Small, Busy, "OpenPostings"),
                new UiRibbonItem(localizer["Ribbon_Attachments"], "<svg><use href='/icons/sprite.svg#attachment'/></svg>", UiRibbonItemSize.Small, Busy, "OpenAttachments")
            }));
        }

        var merged = base.GetRibbon(localizer);
        if (merged.Count > 0) { groups.AddRange(merged); }
        return groups;
    }

    // DTOs / VMs used by VM
    public sealed record AccountDto(Guid Id, string Name, AccountType Type, string? Iban, decimal CurrentBalance, Guid? BankContactId, Guid? SymbolAttachmentId, FinanceManager.Domain.Accounts.SavingsPlanExpectation SavingsPlanExpectation);
    public sealed record ContactDto(Guid Id, string Name);
    public sealed class BankContactVm { public Guid Id { get; set; } public string Name { get; set; } = string.Empty; }
}

public enum AccountType { Giro = 0, Savings = 1 }
