using CopilotUsageSimulator.Common.Guardrails;

namespace CopilotUsageSimulator.Common.Tests;

public sealed class GuardrailMetadataCatalogTests
{
    [Fact]
    public void DescriptorsHaveUniqueKeysAndCompletePresentationMetadata()
    {
        var descriptors = GuardrailMetadataCatalog.All;

        Assert.NotEmpty(descriptors);
        Assert.Equal(
            descriptors.Count,
            descriptors.Select(item => item.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(descriptors, descriptor =>
        {
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Category));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Label));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.SettingsAnchor));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.BlockingExplanation));
        });
    }

    [Fact]
    public void DocumentationLinksAreOfficialGithubPages()
    {
        var links = GuardrailMetadataCatalog.All
            .Select(item => item.DocumentationUrl)
            .Where(url => url is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        Assert.All(links, link =>
        {
            var uri = new Uri(link!);
            Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);
            Assert.Equal("docs.github.com", uri.Host);
        });
    }

    [Theory]
    [InlineData(GuardrailMetadataKeys.RuntimeDuration, false, "runtime-settings")]
    [InlineData(GuardrailMetadataKeys.UlbIndividual, true, "individual-ulb-settings")]
    [InlineData(GuardrailMetadataKeys.ActionsBudget, true, "actions-budget-settings")]
    public void ResolvesCostClassificationAndAnchor(string key, bool costRelated, string anchor)
    {
        var metadata = GuardrailMetadataCatalog.Resolve(key);

        Assert.Equal(costRelated, metadata.IsCostRelated);
        Assert.Equal(anchor, metadata.SettingsAnchor);
    }

    [Fact]
    public void UnknownGuardrailUsesSafeFallback()
    {
        var metadata = GuardrailMetadataCatalog.Resolve(null, "custom.guardrail", "custom");

        Assert.Equal("custom", metadata.Category);
        Assert.Equal("custom guardrail", metadata.Label);
        Assert.Equal(GuardrailMetadataCatalog.ScenarioJsonAnchor, metadata.SettingsAnchor);
    }
}
