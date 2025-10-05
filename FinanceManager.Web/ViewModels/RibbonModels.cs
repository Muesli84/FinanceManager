using System.Collections.Generic;

namespace FinanceManager.Web.ViewModels;

public enum UiRibbonItemSize
{
    Small,
    Large
}

public sealed record UiRibbonItem(string Label, string IconSvg, UiRibbonItemSize Size, bool Disabled, string Action);

public sealed record UiRibbonGroup(string Title, List<UiRibbonItem> Items);