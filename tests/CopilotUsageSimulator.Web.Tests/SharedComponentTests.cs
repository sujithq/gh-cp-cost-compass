using Bunit;
using CopilotUsageSimulator.Web.Layout;
using CopilotUsageSimulator.Web.Pages;
using CopilotUsageSimulator.Web.Shared;

namespace CopilotUsageSimulator.Web.Tests;

public sealed class SharedComponentTests : BunitContext
{
    [Fact]
    public void LegacyLayoutAndGuideDisableLegacyDestinationsButKeepCompassLinks()
    {
        var cut = Render<MainLayout>(parameters => parameters.Add(layout => layout.Body, builder =>
        {
            builder.OpenComponent<Guide>(0);
            builder.CloseComponent();
        }));

        Assert.Empty(cut.FindAll("a[href='advanced'], a[href='guide']"));
        var disabledLinks = cut.FindAll("a[role='link'][aria-disabled='true']");
        Assert.Equal(3, disabledLinks.Count);
        Assert.All(disabledLinks, link =>
        {
            Assert.False(link.HasAttribute("href"));
            Assert.False(link.HasAttribute("tabindex"));
            Assert.False(link.HasAttribute("onclick"));
            Assert.NotEmpty(link.GetAttribute("title")!);
        });
        Assert.Equal("", cut.Find(".topnav a[href]").GetAttribute("href"));
        Assert.Equal("", cut.Find(".guide-page a[href]").GetAttribute("href"));
        Assert.NotEmpty(cut.FindAll("a[target='_blank']"));
    }

    [Fact]
    public void DocsLinkProvidesAccessibleSafeExternalNavigation()
    {
        var cut = Render<DocsLink>(parameters => parameters
            .Add(component => component.Url, "https://docs.github.com/en/copilot/concepts/billing")
            .Add(component => component.Topic, "Copilot billing"));

        var link = cut.Find("a");
        Assert.Equal("Open GitHub documentation for Copilot billing", link.GetAttribute("aria-label"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
    }

    [Fact]
    public void BudgetCardToggleNotifiesParentAndRevealsFields()
    {
        var enabled = false;
        var cut = Render<BudgetCard>(parameters => parameters
            .Add(component => component.Title, "Enterprise budget")
            .Add(component => component.Help, "Enterprise spending limit.")
            .Add(component => component.Enabled, enabled)
            .Add(component => component.EnabledChanged, value => enabled = value));

        Assert.Empty(cut.FindAll(".budget-fields"));

        cut.Find("input[type=checkbox]").Change(true);
        cut.Render(parameters => parameters
            .Add(component => component.Title, "Enterprise budget")
            .Add(component => component.Help, "Enterprise spending limit.")
            .Add(component => component.Enabled, enabled)
            .Add(component => component.EnabledChanged, value => enabled = value));

        Assert.True(enabled);
        Assert.NotEmpty(cut.FindAll(".budget-fields"));
        Assert.Contains("Limit (USD)", cut.Markup);
    }
}
