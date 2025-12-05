using FinanceManager.Shared; // IApiClient
using Microsoft.Extensions.Localization;
using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Web.ViewModels;

public sealed class AccountDetailViewModel : ViewModelBase
{
    private readonly IApiClient _api;

    public AccountDetailViewModel(IServiceProvider sp) : base(sp)
    {
        _api = sp.GetRequiredService<IApiClient>();
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
    public SavingsPlanExpectation SavingsPlanExpectation { get => _savingsPlanExpectation; set { if (_savingsPlanExpectation != value) { _savingsPlanExpectation = value; RaiseStateChanged(); } } }
    private SavingsPlanExpectation _savingsPlanExpectation = SavingsPlanExpectation.Optional;

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
            var list = await _api.Contacts_ListAsync(skip: 0, take: 200, type: ContactType.Bank, all: true, nameFilter: null, ct);
            BankContacts.Clear();
            BankContacts.AddRange(list.Select(c => new BankContactVm { Id = c.Id, Name = c.Name }).OrderBy(c => c.Name));
            RaiseStateChanged();
        }
        catch { }
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        if (!AccountId.HasValue) { return; }
        try
        {
            var dto = await _api.GetAccountAsync(AccountId.Value, ct);
            if (dto != null)
            {
                Name = dto.Name;
                Type = dto.Type;
                Iban = dto.Iban;
                BankContactId = dto.BankContactId;
                SymbolAttachmentId = dto.SymbolAttachmentId; // new
                SavingsPlanExpectation = dto.SavingsPlanExpectation; // new
            }
            else
            {
                Error = "ErrorNotFound"; // Page localizes
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
            if (IsNew)
            {
                var dto = await _api.CreateAccountAsync(new AccountCreateRequest(Name, Type, Iban, BankContactId, NewBankContactName, SymbolAttachmentId, SavingsPlanExpectation), ct);
                AccountId = dto.Id; // update context
                return dto.Id;
            }
            else
            {
                var updated = await _api.UpdateAccountAsync(AccountId!.Value, new AccountUpdateRequest(Name, Type, Iban, BankContactId, NewBankContactName, SymbolAttachmentId, SavingsPlanExpectation, Archived: false), ct);
                if (updated == null)
                {
                    Error = "ErrorNotFound";
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
            var ok = await _api.DeleteAccountAsync(AccountId.Value, ct);
            if (!ok)
            {
                Error = "ErrorNotFound";
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

    // VMs used by VM
    public sealed class BankContactVm { public Guid Id { get; set; } public string Name { get; set; } = string.Empty; }
}
