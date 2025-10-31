using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bunit;
using FinanceManager.Web.Components.Shared;
using Xunit;

namespace FinanceManager.Tests.Components;

public class RibbonTests : TestContext
{
    private enum TabId { One, Two }

    [Fact]
    public void SingleTab_RendersGroupsAndButtons()
    {
        // Arrange
        var tabs = new List<Ribbon<TabId>.RibbonTab<TabId>>
        {
            new()
            {
                Id = TabId.One,
                Title = "Tab One",
                Groups = new()
                {
                    new Ribbon<TabId>.RibbonGroup
                    {
                        Title = "Group A",
                        Items = new()
                        {
                            new Ribbon<TabId>.RibbonItem{ Label="Save", IconSvg="<svg></svg>" },
                            new Ribbon<TabId>.RibbonItem{ Label="Delete", IconSvg="<svg></svg>", Disabled=true }
                        }
                    }
                }
            }
        };

        // Act
        var cut = RenderComponent<Ribbon<TabId>>(p => p
            .Add(x => x.Tabs, tabs)
            .Add(x => x.ActiveTab, TabId.One));

        // Assert
        Assert.Equal(1, cut.FindAll(".fm-ribbon-group").Count);
        Assert.Contains("Group A", cut.Markup);
        var buttons = cut.FindAll("button.fm-ribbon-btn");
        Assert.Equal(2, buttons.Count);
        Assert.Null(buttons[0].GetAttribute("aria-disabled"));
        Assert.Equal("true", buttons[1].GetAttribute("aria-disabled"));
    }

    [Fact]
    public async Task ClickCallback_IsInvoked()
    {
        // Arrange
        var clicked = false;
        var item = new Ribbon<TabId>.RibbonItem
        {
            Label = "Run",
            IconSvg = "<svg></svg>",
            Callback = () => { clicked = true; return Task.CompletedTask; }
        };
        var tabs = new List<Ribbon<TabId>.RibbonTab<TabId>>
        {
            new()
            {
                Id = TabId.One,
                Title = "Tab One",
                Groups = new() { new Ribbon<TabId>.RibbonGroup{ Title="Main", Items = new(){ item } } }
            }
        };

        var cut = RenderComponent<Ribbon<TabId>>(p => p
            .Add(x => x.Tabs, tabs)
            .Add(x => x.ActiveTab, TabId.One));

        // Act
        cut.Find("button.fm-ribbon-btn").Click();

        // Assert
        Assert.True(clicked);
    }
}
