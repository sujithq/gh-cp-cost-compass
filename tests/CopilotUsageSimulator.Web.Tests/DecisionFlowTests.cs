using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bunit;
using CopilotUsageSimulator.Web.Pages;
using CopilotUsageSimulator.Web.Shared.Compass;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace CopilotUsageSimulator.Web.Tests;

public sealed class DecisionFlowTests : BunitContext
{
    [Fact]
    public void CompassSectionRendersDiagramWorkspaceAndInvokesMermaid()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<CompassDecisionFlow>();

        Assert.Equal("AI-credit decision flow", cut.Find("h2").TextContent);
        Assert.NotNull(cut.Find("#decision-flow-diagram"));
        Assert.Equal("Diagram controls", cut.Find("[role='group']").GetAttribute("aria-label"));
        Assert.Equal("Scrollable decision-flow diagram", cut.Find("[role='region']").GetAttribute("aria-label"));
        Assert.Equal(3, cut.FindAll("button.cp-button-icon").Count);
        Assert.Contains("Accept allocation and alerts only", cut.Find("#decision-flow-description").TextContent);
        Assert.Contains(JSInterop.Invocations, invocation =>
            invocation.Identifier == "decisionFlow.observe" &&
            Equals(invocation.Arguments[1], "content/decision-flow.mmd") &&
            Equals(invocation.Arguments[2], "decision-flow-loading") &&
            Equals(invocation.Arguments[3], "decision-flow-description") &&
            Equals(invocation.Arguments[4], "decision-flow-error"));

        cut.Find("button[aria-label='Zoom in']").Click();
        cut.Find("button[aria-label='Fit diagram to width']").Click();

        Assert.Contains(JSInterop.Invocations, invocation => invocation.Identifier == "decisionFlow.zoom");
        Assert.Contains(JSInterop.Invocations, invocation => invocation.Identifier == "decisionFlow.fit");
    }

    [Fact]
    public void FormerStandaloneRouteRedirectsIntoCostCompass()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();

        Render<DecisionFlowRedirect>();

        Assert.EndsWith("/#cc-decision-flow", navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void FormerStandaloneRoutePreservesRepositoryBasePath()
    {
        var target = DecisionFlowRedirect.BuildTarget("https://example.test/CopilotUsageSimulator/");

        Assert.Equal(
            "https://example.test/CopilotUsageSimulator/#cc-decision-flow",
            target);
    }

    [Fact]
    public void InitializationFailureTransitionsFromLoadingToError()
    {
        JSInterop.SetupVoid("decisionFlow.observe", _ => true)
            .SetException(new JSException("Observer unavailable."));
        JSInterop.SetupVoid("decisionFlow.showError", _ => true);

        Render<CompassDecisionFlow>();

        Assert.Contains(JSInterop.Invocations, invocation =>
            invocation.Identifier == "decisionFlow.showError" &&
            Equals(invocation.Arguments[0], "decision-flow-error") &&
            Equals(invocation.Arguments[2], "decision-flow-loading"));
    }

    [Fact]
    public void GeneratedDiagramMatchesMaintainedMarkdownSource()
    {
        var root = FindRepositoryRoot();
        var document = File.ReadAllText(Path.Combine(root, "docs", "decision-flow.md"));
        var generated = File.ReadAllText(Path.Combine(
            root, "src", "CopilotUsageSimulator.Web", "wwwroot", "content", "decision-flow.mmd"));
        var match = Regex.Match(document, "```mermaid\\r?\\n([\\s\\S]*?)\\r?\\n```");

        Assert.True(match.Success);
        Assert.Equal(match.Groups[1].Value.TrimEnd(), generated.TrimEnd());
    }

    [Fact]
    public void VendoredRuntimeMatchesPinnedVersionAndGeneratedHash()
    {
        var root = FindRepositoryRoot();
        using var package = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "package.json")));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "src", "CopilotUsageSimulator.Web", "wwwroot", "vendor", "mermaid", "manifest.json")));
        var runtime = File.ReadAllBytes(Path.Combine(
            root, "src", "CopilotUsageSimulator.Web", "wwwroot", "vendor", "mermaid", "mermaid.min.js"));
        var expectedVersion = package.RootElement.GetProperty("devDependencies").GetProperty("mermaid").GetString();
        var actualHash = Convert.ToHexStringLower(SHA256.HashData(runtime));

        Assert.Equal(expectedVersion, manifest.RootElement.GetProperty("version").GetString());
        Assert.Equal(actualHash, manifest.RootElement.GetProperty("sha256").GetString());
        Assert.True(File.Exists(Path.Combine(
            root, "src", "CopilotUsageSimulator.Web", "wwwroot", "vendor", "mermaid", "LICENSE")));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "CopilotUsageSimulator.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

}