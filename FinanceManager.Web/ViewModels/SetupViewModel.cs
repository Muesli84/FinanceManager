using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.ViewModels;

public sealed class SetupViewModel : ViewModelBase
{
    public const string TabBackup = "backup";
    public const string TabImportSplit = "import-split";
    public const string TabNotifications = "notifications";
    public const string TabProfile = "profile";
    public const string TabIpBlocks = "ip-blocks";
    public const string TabAttachmentCategories = "attachment-categories";

    public SetupViewModel(IServiceProvider sp) : base(sp) { }

    public string ActiveSection { get; private set; } = TabBackup;

    public void ApplyQueryFromUri(string uri)
    {
        try
        {
            var parsed = new Uri(uri);
            var query = QueryHelpers.ParseQuery(parsed.Query);
            if (query.TryGetValue("section", out var sectionVals))
            {
                var section = sectionVals.ToString();
                if (IsValidSection(section))
                {
                    ActiveSection = section;
                    RaiseStateChanged();
                    return;
                }
            }
            if (query.ContainsKey("focus"))
            {
                ActiveSection = TabIpBlocks;
                RaiseStateChanged();
            }
        }
        catch
        {
            // ignore malformed query
        }
    }

    public void Activate(string section)
    {
        if (IsValidSection(section) && section != ActiveSection)
        {
            ActiveSection = section;
            RaiseStateChanged();
        }
    }

    public bool IsActiveSection(string section) => ActiveSection == section;

    public static bool IsValidSection(string section)
        => section == TabBackup
        || section == TabImportSplit
        || section == TabNotifications
        || section == TabProfile
        || section == TabIpBlocks
        || section == TabAttachmentCategories;

    public override IReadOnlyList<UiRibbonGroup> GetRibbon(IStringLocalizer localizer)
    {
        return new List<UiRibbonGroup>
        {
            new UiRibbonGroup(localizer["Ribbon_Group_Navigation"], new List<UiRibbonItem>
            {
                new UiRibbonItem(localizer["Ribbon_Back"], "<svg><use href='/icons/sprite.svg#back'/></svg>", UiRibbonItemSize.Large, false, "Back")
            })
        };
    }
}
