using Microsoft.Extensions.Localization;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using FinanceManager.Shared.Dtos.Contacts; // use shared ContactDto, ContactCategoryDto, AliasNameDto, ContactCreateRequest, ContactUpdateRequest

namespace FinanceManager.Web.ViewModels;

public sealed class ContactDetailViewModel : ViewModelBase
{
    private readonly HttpClient _http;

    public ContactDetailViewModel(IServiceProvider sp, IHttpClientFactory httpFactory) : base(sp)
    {
        _http = httpFactory.CreateClient("Api");
    }

    // Identity / status
    public Guid? ContactId { get; private set; }
    public bool IsNew => !ContactId.HasValue;
    public bool ShowCharts => !IsNew;

    public bool Loaded { get; private set; }
    public bool Busy { get; private set; }

    public string? Error { get; private set; }

    // Form fields
    [Required, MinLength(2)]
    public string Name { get => _name; set { if (_name != value) { _name = value; RaiseStateChanged(); } } }
    private string _name = string.Empty;

    public ContactType Type { get => _type; set { if (_type != value) { _type = value; RaiseStateChanged(); } } }
    private ContactType _type = ContactType.Person;

    public string CategoryId { get => _categoryId; set { if (_categoryId != value) { _categoryId = value; RaiseStateChanged(); } } }
    private string _categoryId = string.Empty;

    public string? Description { get => _description; set { if (_description != value) { _description = value; RaiseStateChanged(); } } }
    private string? _description;

    public bool IsPaymentIntermediary { get => _isPaymentIntermediary; set { if (_isPaymentIntermediary != value) { _isPaymentIntermediary = value; RaiseStateChanged(); } } }
    private bool _isPaymentIntermediary;

    public bool IsSelfContact => Type == ContactType.Self;

    // Symbol attachment id
    public Guid? SymbolAttachmentId { get => _symbolAttachmentId; set { if (_symbolAttachmentId != value) { _symbolAttachmentId = value; RaiseStateChanged(); } } }
    private Guid? _symbolAttachmentId;

    // Related state
    private bool _showAttachments;
    public bool ShowAttachments { get => _showAttachments; set { if (_showAttachments != value) { _showAttachments = value; RaiseStateChanged(); } } }

    private bool _showMergeDialog;
    public bool ShowMergeDialog { get => _showMergeDialog; set { if (_showMergeDialog != value) { _showMergeDialog = value; RaiseStateChanged(); } } }

    public List<CategoryItem> Categories { get; } = new();

    // Aliases
    public List<AliasItem> Aliases { get; private set; } = new();
    public string NewAlias { get => _newAlias; set { if (_newAlias != value) { _newAlias = value; RaiseStateChanged(); } } }
    private string _newAlias = string.Empty;
    public string? AliasError { get; private set; }

    public void ForContact(Guid? id)
    {
        ContactId = id;
    }

    public override async ValueTask InitializeAsync(CancellationToken ct = default)
    {
        if (!IsAuthenticated)
        {
            RequireAuthentication(null);
            return;
        }

        await LoadCategoriesAsync(ct);
        if (!IsNew)
        {
            await LoadAsync(ct);
            await LoadAliasesAsync(ct);
        }
        Loaded = true;
        RaiseStateChanged();
    }

    private async Task LoadCategoriesAsync(CancellationToken ct)
    {
        try
        {
            var resp = await _http.GetAsync("/api/contact-categories", ct);
            if (resp.IsSuccessStatusCode)
            {
                var list = await resp.Content.ReadFromJsonAsync<List<ContactCategoryDto>>(cancellationToken: ct) ?? new();
                Categories.Clear();
                Categories.AddRange(list.Select(c => new CategoryItem { Id = c.Id, Name = c.Name }).OrderBy(c => c.Name));
                RaiseStateChanged();
            }
        }
        catch { }
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        if (!ContactId.HasValue) { return; }
        try
        {
            var resp = await _http.GetAsync($"/api/contacts/{ContactId}", ct);
            if (resp.IsSuccessStatusCode)
            {
                var dto = await resp.Content.ReadFromJsonAsync<ContactDto>(cancellationToken: ct);
                if (dto != null)
                {
                    Name = dto.Name;
                    Type = dto.Type;
                    CategoryId = dto.CategoryId?.ToString() ?? string.Empty;
                    IsPaymentIntermediary = dto.IsPaymentIntermediary;
                    Description = dto.Description;
                    SymbolAttachmentId = dto.SymbolAttachmentId; // new
                }
            }
            else if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Error = "ErrorNotFound";
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

    public async Task<Guid?> SaveAsync(CancellationToken ct = default)
    {
        Busy = true; Error = null; RaiseStateChanged();
        try
        {
            Guid? catId = Guid.TryParse(CategoryId, out var parsed) ? parsed : null;
            if (IsNew)
            {
                var create = new ContactCreateRequest(Name.Trim(), Type, catId, Description, IsPaymentIntermediary);
                var resp = await _http.PostAsJsonAsync("/api/contacts", create, ct);
                if (resp.IsSuccessStatusCode)
                {
                    var dto = await resp.Content.ReadFromJsonAsync<ContactDto>(cancellationToken: ct);
                    if (dto != null)
                    {
                        ContactId = dto.Id; RaiseStateChanged();
                        return dto.Id;
                    }
                }
                else
                {
                    Error = "ErrorCreateFailed";
                }
            }
            else
            {
                var update = new ContactUpdateRequest(Name.Trim(), Type, catId, Description, IsPaymentIntermediary);
                var resp = await _http.PutAsJsonAsync($"/api/contacts/{ContactId}", update, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    Error = "ErrorSaveFailed";
                }
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Busy = false; RaiseStateChanged();
        }

        return null;
    }

    public async Task DeleteAsync(CancellationToken ct = default)
    {
        if (IsNew || !ContactId.HasValue) { return; }
        Busy = true; Error = null; RaiseStateChanged();
        try
        {
            var resp = await _http.DeleteAsync($"/api/contacts/{ContactId}", ct);
            if (!resp.IsSuccessStatusCode)
            {
                Error = "ErrorDeleteFailed";
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Busy = false; RaiseStateChanged();
        }
    }

    // Aliases
    public async Task LoadAliasesAsync(CancellationToken ct = default)
    {
        if (IsNew || !ContactId.HasValue) { Aliases = new(); RaiseStateChanged(); return; }
        try
        {
            var resp = await _http.GetAsync($"/api/contacts/{ContactId}/aliases", ct);
            if (resp.IsSuccessStatusCode)
            {
                var list = await resp.Content.ReadFromJsonAsync<List<AliasNameDto>>(cancellationToken: ct) ?? new();
                Aliases = list.Select(a => new AliasItem { Id = a.Id, Pattern = a.Pattern }).ToList();
                RaiseStateChanged();
            }
        }
        catch { }
    }

    public async Task AddAliasAsync(CancellationToken ct = default)
    {
        AliasError = null; RaiseStateChanged();
        var pattern = (NewAlias ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(pattern)) { AliasError = "ErrorAliasEmpty"; RaiseStateChanged(); return; }
        try
        {
            var resp = await _http.PostAsJsonAsync($"/api/contacts/{ContactId}/aliases", new AliasCreateRequest(pattern), ct);
            if (resp.IsSuccessStatusCode)
            {
                NewAlias = string.Empty;
                await LoadAliasesAsync(ct);
            }
            else
            {
                AliasError = await resp.Content.ReadAsStringAsync(ct);
            }
        }
        catch (Exception ex)
        {
            AliasError = ex.Message;
        }
        finally { RaiseStateChanged(); }
    }

    public async Task DeleteAliasAsync(Guid aliasId, CancellationToken ct = default)
    {
        AliasError = null; RaiseStateChanged();
        try
        {
            var resp = await _http.DeleteAsync($"/api/contacts/{ContactId}/aliases/{aliasId}", ct);
            if (resp.IsSuccessStatusCode)
            {
                await LoadAliasesAsync(ct);
            }
            else
            {
                AliasError = await resp.Content.ReadAsStringAsync(ct);
            }
        }
        catch (Exception ex)
        {
            AliasError = ex.Message;
        }
        finally { RaiseStateChanged(); }
    }

    // Merge
    public void OpenMergeDialog()
    {
        if (IsNew || IsSelfContact) { return; }
        ShowMergeDialog = true;
    }

    public void CloseMergeDialog()
    {
        ShowMergeDialog = false;
    }

    public async Task<bool> PerformMergeAsync(Guid targetId, CancellationToken ct = default)
    {
        if (IsNew || !ContactId.HasValue || targetId == Guid.Empty) { throw new InvalidOperationException("Invalid merge target."); }
        try
        {
            var resp = await _http.PostAsJsonAsync($"/api/contacts/{ContactId}/merge", new ContactMergeRequest(targetId), ct);
            if (resp.IsSuccessStatusCode)
            {
                ShowMergeDialog = false;
                return true;
            }
            else
            {
                var raw = await resp.Content.ReadAsStringAsync(ct);
                string? msg = TryExtractProblemMessage(raw) ?? "Merge failed.";
                throw new InvalidOperationException(msg);
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    private static string? TryExtractProblemMessage(string raw)
    {
        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var doc = JsonSerializer.Deserialize<ProblemLike>(raw, opts);
            if (doc is null) { return null; }
            return $"{doc.title} {doc.detail ?? doc.message ?? doc.error}".Trim();
        }
        catch { return null; }
    }

    private sealed class ProblemLike
    {
        public string? title { get; set; }
        public string? detail { get; set; }
        public string? message { get; set; }
        public string? error { get; set; }
    }

    // Ribbon
    public override IReadOnlyList<UiRibbonGroup> GetRibbon(IStringLocalizer localizer)
    {
        if (!Loaded)
        {
            return Array.Empty<UiRibbonGroup>();
        }

        var groups = new List<UiRibbonGroup>();
        groups.Add(new UiRibbonGroup(localizer["Ribbon_Group_Navigation"], new List<UiRibbonItem>
        {
            new UiRibbonItem(localizer["Ribbon_Back"], "<svg><use href='/icons/sprite.svg#back'/></svg>", UiRibbonItemSize.Large, false, "Back")
        }));

        var canSave = !Busy && !string.IsNullOrWhiteSpace(Name) && Name.Trim().Length >= 2;
        var editItems = new List<UiRibbonItem>
        {
            new UiRibbonItem(localizer["Ribbon_Save"], "<svg><use href='/icons/sprite.svg#save'/></svg>", UiRibbonItemSize.Large, !canSave, "Save")
        };
        if (!IsNew)
        {
            editItems.Add(new UiRibbonItem(localizer["Ribbon_Delete"], "<svg><use href='/icons/sprite.svg#delete'/></svg>", UiRibbonItemSize.Small, Busy || IsSelfContact, "Delete"));
        }
        groups.Add(new UiRibbonGroup(localizer["Ribbon_Group_Edit"], editItems));

        if (!IsNew)
        {
            var related = new List<UiRibbonItem>();
            if (Type == ContactType.Bank)
            {
                related.Add(new UiRibbonItem(localizer["Ribbon_Accounts"], "<svg><use href='/icons/sprite.svg#accounts'/></svg>", UiRibbonItemSize.Small, Busy, "OpenBankAccounts"));
            }
            if (!IsSelfContact)
            {
                related.Add(new UiRibbonItem(localizer["Ribbon_Merge"], "<svg><use href='/icons/sprite.svg#merge'/></svg>", UiRibbonItemSize.Small, Busy, "OpenMerge"));
            }
            related.Add(new UiRibbonItem(localizer["Ribbon_Postings"], "<svg><use href='/icons/sprite.svg#postings'/></svg>", UiRibbonItemSize.Small, Busy, "OpenPostings"));
            related.Add(new UiRibbonItem(localizer["Ribbon_Attachments"], "<svg><use href='/icons/sprite.svg#attachment'/></svg>", UiRibbonItemSize.Small, Busy, "OpenAttachments"));
            groups.Add(new UiRibbonGroup(localizer["Ribbon_Group_Related"], related));
        }

        var merged = base.GetRibbon(localizer);
        if (merged.Count > 0) { groups.AddRange(merged); }
        return groups;
    }

    public sealed class CategoryItem { public Guid Id { get; set; } public string Name { get; set; } = string.Empty; }
    public sealed class AliasItem { public Guid Id { get; set; } public string Pattern { get; set; } = string.Empty; }
}
