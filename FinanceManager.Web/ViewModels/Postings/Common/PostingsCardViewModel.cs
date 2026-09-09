using FinanceManager.Shared;
using Microsoft.Extensions.Localization;
using FinanceManager.Domain.Attachments;

namespace FinanceManager.Web.ViewModels.Postings.Common;


/// <summary>
/// View model for a posting card. Responsible for loading a single posting by id and exposing card fields and ribbon actions.
/// </summary>
[FinanceManager.Web.ViewModels.Common.CardRoute("postings")]
public sealed class PostingsCardViewModel : BaseCardViewModel<(string Key, string Value)>
{
    /// <summary>
    /// Initializes a new instance of <see cref="PostingsCardViewModel"/>.
    /// </summary>
    /// <param name="sp">Service provider used to resolve required services such as the API client and localizer.</param>
    public PostingsCardViewModel(IServiceProvider sp) : base(sp) { }

    /// <summary>
    /// Identifier of the currently loaded posting.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// The loaded posting DTO or <c>null</c> when no posting is loaded.
    /// </summary>
    public PostingServiceDto? Posting { get; private set; }

    /// <summary>
    /// Title shown in the card header; falls back to the base title when no posting is loaded.
    /// </summary>
    public override string Title => Posting?.Subject ?? base.Title;

    /// <summary>
    /// Configure the view model with an identifier prior to initialization.
    /// </summary>
    /// <param name="id">Posting identifier to configure.</param>
    public void Configure(Guid id) => Id = id;

    /// <summary>
    /// Loads posting data for the specified identifier and builds the card record used by the UI.
    /// When <paramref name="id"/> is <see cref="Guid.Empty"/> an empty record is prepared.
    /// </summary>
    /// <param name="id">Identifier of the posting to load.</param>
    /// <returns>A task that completes when loading and record preparation has finished.</returns>
    /// <exception cref="OperationCanceledException">May be thrown if the ambient cancellation token is triggered by callers of underlying API calls (not directly used here).</exception>
    public override async Task LoadAsync(Guid id)
    {
        Id = id;
        Loading = true; SetError(null, null); RaiseStateChanged();
        try
        {
            if (id == Guid.Empty)
            {
                Posting = null;
                CardRecord = new CardRecord(new List<CardField>());
                return;
            }

            var api = ServiceProvider.GetRequiredService<IApiClient>();
            Posting = await api.Postings_GetByIdAsync(id);
            if (Posting == null)
            {
                SetError(api.LastErrorCode ?? null, api.LastError ?? "Posting not found");
                CardRecord = new CardRecord(new List<CardField>());
                return;
            }

            CardRecord = await BuildCardRecordsAsync(Posting);
        }
        catch (Exception ex)
        {
            SetError(null, ex.Message);
            CardRecord = new CardRecord(new List<CardField>());
        }
        finally
        {
            Loading = false; RaiseStateChanged();
        }
    }

    /// <summary>
    /// Builds the <see cref="CardRecord"/> representing the posting for rendering in the card UI.
    /// </summary>
    /// <param name="p">Posting DTO to build the record from.</param>
    /// <returns>A task that resolves to the constructed <see cref="CardRecord"/>.</returns>
    private Task<CardRecord> BuildCardRecordsAsync(PostingServiceDto p)
    {
        var fields = new List<CardField>
        {
            new CardField("Card_Caption_Posting_Date", CardFieldKind.Text, text: p.BookingDate.ToString("d")),
            new CardField("Card_Caption_Posting_Valuta", CardFieldKind.Text, text: p.ValutaDate.ToString("d")),
            new CardField("Card_Caption_Posting_Amount", CardFieldKind.Currency, amount: p.Amount),
            new CardField("Card_Caption_Posting_Kind", CardFieldKind.Text, text: (p.Kind == PostingKind.Security && p.SecuritySubType.HasValue) ? $"Security-{p.SecuritySubType}" : p.Kind.ToString()),
            new CardField("Card_Caption_Posting_Recipient", CardFieldKind.Text, text: p.RecipientName ?? string.Empty),
            new CardField("Card_Caption_Posting_Subject", CardFieldKind.Text, text: p.Subject ?? string.Empty),
            new CardField("Card_Caption_Posting_Description", CardFieldKind.Text, text: p.Description ?? string.Empty)
        };

        var record = new CardRecord(fields, p);
        return Task.FromResult(record);
    }

    /// <summary>
    /// Returns ribbon register definitions used by the posting card UI including navigation and linked actions.
    /// </summary>
    /// <param name="localizer">Localizer used to resolve UI labels.</param>
    /// <returns>A list of <see cref="UiRibbonRegister"/> instances or <c>null</c> when none are provided.</returns>
    protected override IReadOnlyList<UiRibbonRegister>? GetRibbonRegisterDefinition(IStringLocalizer localizer)
    {
        var kindLower = Posting?.Kind.ToString().ToLowerInvariant() switch
        {
            "bank" => $"account/{Posting.AccountId}",
            "contact" => $"contact/{Posting.ContactId}",
            "savingsplan" => $"savings-plan/{Posting.SavingsPlanId}",
            "security" => $"security/{Posting.SecurityId}",
            _ => Posting?.Kind.ToString().ToLowerInvariant()
        };
        var nav = new UiRibbonTab(localizer["Ribbon_Group_Navigation"].Value, new List<UiRibbonAction>
        {
            new UiRibbonAction("Back", localizer["Ribbon_Back"].Value, "<svg><use href='/icons/sprite.svg#back'/></svg>", UiRibbonItemSize.Large, false, null, () => { RaiseUiActionRequested("Back", kindLower); return Task.CompletedTask; }) { MobileShortcut = true }
        });

        var linked = new UiRibbonTab(localizer["Ribbon_Group_Linked"].Value, new List<UiRibbonAction>
        {
            new UiRibbonAction("OpenAccount", localizer["Ribbon_OpenAccount"].Value, "<svg><use href='/icons/sprite.svg#external'/></svg>", UiRibbonItemSize.Small, Posting?.AccountId == null, null, () => { RaiseUiActionRequested("OpenAccount"); return Task.CompletedTask; }),
            new UiRibbonAction("OpenContact", localizer["Ribbon_OpenContact"].Value, "<svg><use href='/icons/sprite.svg#external'/></svg>", UiRibbonItemSize.Small, Posting?.ContactId == null, null, () => { RaiseUiActionRequested("OpenContact"); return Task.CompletedTask; }),
            new UiRibbonAction("OpenSavingsPlan", localizer["Ribbon_OpenSavingsPlan"].Value, "<svg><use href='/icons/sprite.svg#external'/></svg>", UiRibbonItemSize.Small, Posting?.SavingsPlanId == null, null, () => { RaiseUiActionRequested("OpenSavingsPlan"); return Task.CompletedTask; }),
            new UiRibbonAction("OpenSecurity", localizer["Ribbon_OpenSecurity"].Value, "<svg><use href='/icons/sprite.svg#external'/></svg>", UiRibbonItemSize.Small, Posting?.SecurityId == null, null, () => { RaiseUiActionRequested("OpenSecurity"); return Task.CompletedTask; }),
            new UiRibbonAction("OpenAttachments", localizer["Ribbon_Attachments"].Value, "<svg><use href='/icons/sprite.svg#attachment'/></svg>", UiRibbonItemSize.Small, false, null, () => { RaiseUiActionRequested("OpenAttachments"); return Task.CompletedTask; })
        });

        var actionsDisabled = Posting?.IsReversed == true || Posting?.IsReversal == true;
        var actions = new UiRibbonTab(localizer["Ribbon_Group_Actions"].Value, new List<UiRibbonAction>
        {
            new UiRibbonAction("Reverse", localizer["Ribbon_Reverse"].Value, "<svg><use href='/icons/sprite.svg#undo'/></svg>", UiRibbonItemSize.Large, actionsDisabled, null, async () =>
            {
                await ReverseAsync();
            })
        });
        return new List<UiRibbonRegister> { new UiRibbonRegister(UiRibbonRegisterKind.Actions, new List<UiRibbonTab> { nav, linked, actions }) };
    }

    // Postings do not support symbol assignment via the card UI; provide no-op implementations for abstract hooks
    /// <summary>
    /// Indicates whether symbol uploads are allowed for postings. Returns <c>false</c> as postings do not support symbols via the UI.
    /// </summary>
    /// <returns><c>false</c>.</returns>
    protected override bool IsSymbolUploadAllowed() => false;

    /// <summary>
    /// Returns the attachment parent kind and id for symbol uploads. For postings symbol assignment is not supported and a default value is returned.
    /// </summary>
    /// <returns>A tuple containing the <see cref="AttachmentEntityKind.Posting"/> and an empty Guid as parent id.</returns>
    protected override SymbolParentRef GetSymbolParent()
        => new(AttachmentEntityKind.Posting, Guid.Empty);

    /// <summary>
    /// No-op symbol assignment implementation for postings. Postings do not support symbol assignment via the card UI.
    /// </summary>
    /// <param name="attachmentId">Attachment id to assign or <c>null</c> to clear; ignored for postings.</param>
    /// <returns>A completed task.</returns>
    protected override Task AssignNewSymbolAsync(Guid? attachmentId) => Task.CompletedTask;

    /// <summary>
    /// Validates an uploaded symbol for the posting. Postings do not accept symbols and this returns <c>null</c>.
    /// </summary>
    /// <param name="stream">Stream containing uploaded file data.</param>
    /// <param name="fileName">Original file name.</param>
    /// <param name="contentType">MIME type of the uploaded file.</param>
    /// <returns>A task that resolves to <c>null</c> because postings do not support symbols.</returns>
    public override Task<Guid?> ValidateSymbolAsync(System.IO.Stream stream, string fileName, string contentType)
        => Task.FromResult<Guid?>(null);

    /// <summary>
    /// Reloads the current posting by reloading the card for the current <see cref="Id"/>.
    /// </summary>
    /// <returns>A task that completes when reload has finished.</returns>
    public override Task ReloadAsync()
        => LoadAsync(Id);

    /// <summary>
    /// Reverses the current posting by calling the API and navigates to the created reversal posting on success.
    /// </summary>
    private async Task ReverseAsync()
    {
        if (Posting == null) return;
        var api = ServiceProvider.GetRequiredService<IApiClient>();
        Loading = true; SetError(null, null); RaiseStateChanged();
        try
        {
            var result = await api.Postings_ReverseAsync(Id);
            if (result == null)
            {
                SetError(api.LastErrorCode, api.LastError ?? "Reversal failed");
                return;
            }
            // Navigate to the first created reversal posting
            var reversalId = result.CreatedReversalIds.FirstOrDefault();
            if (reversalId != Guid.Empty)
            {
                RaiseUiActionRequested("NavigateToPosting", reversalId.ToString());
            }
            else
            {
                await ReloadAsync();
            }
        }
        catch (Exception ex)
        {
            SetError(null, ex.Message);
        }
        finally
        {
            Loading = false; RaiseStateChanged();
        }
    }
}
