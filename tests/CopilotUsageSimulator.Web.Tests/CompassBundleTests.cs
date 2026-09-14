using System.Text.Json;
using System.Text.Json.Nodes;
using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Simulation;
using CopilotUsageSimulator.Web.Services;

namespace CopilotUsageSimulator.Web.Tests;

public sealed class CompassBundleTests
{
    private readonly ScenarioJson _json = new();
    private readonly EngineConfiguration _catalog = EngineConfigurationLoader.LoadDefault();

    [Fact]
    public void RoundTripPreservesScenarioAndMatchingCatalog()
    {
        var codec = new CompassBundleCodec(_json);
        var scenario = CompassPresetFactory.Create(_catalog, "cloud-agent");
        var exported = codec.Export(scenario, _catalog);

        var restored = codec.Import(exported, _catalog with { Version = "different-active-catalog" });

        Assert.Equal(_json.Serialize(scenario), _json.Serialize(restored.Scenario));
        Assert.Equivalent(_catalog, restored.Catalog, strict: true);
        Assert.Null(restored.Warning);
        Assert.Contains("\"engineContract\"", exported);
        Assert.Contains("\"catalogSha256\"", exported);
    }

    [Theory]
    [InlineData("schema", "another-product")]
    [InlineData("engineContract", "future-engine")]
    public void UnsupportedIdentityIsRejected(string key, string value)
    {
        var codec = new CompassBundleCodec(_json);
        var node = JsonNode.Parse(codec.Export(CompassPresetFactory.Create(_catalog, "chat"), _catalog))!;
        node[key] = value;

        Assert.Throws<JsonException>(() => codec.Import(node.ToJsonString(), _catalog));
    }

    [Fact]
    public void UnsupportedSchemaVersionIsRejected()
    {
        var codec = new CompassBundleCodec(_json);
        var node = JsonNode.Parse(codec.Export(CompassPresetFactory.Create(_catalog, "chat"), _catalog))!;
        node["schemaVersion"] = 99;

        Assert.Throws<JsonException>(() => codec.Import(node.ToJsonString(), _catalog));
    }

    [Fact]
    public void ChangedCatalogWithoutMatchingEvidenceFingerprintIsRejected()
    {
        var codec = new CompassBundleCodec(_json);
        var node = JsonNode.Parse(codec.Export(CompassPresetFactory.Create(_catalog, "chat"), _catalog))!;
        node["catalog"]!["usdPerCredit"] = .02m;

        var exception = Assert.Throws<JsonException>(() => codec.Import(node.ToJsonString(), _catalog));

        Assert.Contains("fingerprint", exception.Message);
    }

    [Fact]
    public void CatalogFingerprintIgnoresEquivalentDecimalScale()
    {
        var sameCatalog = _catalog with { UsdPerCredit = .010000m };

        Assert.Equal(CompassBundleCodec.CatalogHash(_catalog), CompassBundleCodec.CatalogHash(sameCatalog));
    }

    [Fact]
    public void ScenarioEditingAfterNumericFormattingKeepsMatchingCatalogEvidence()
    {
        var codec = new CompassBundleCodec(_json);
        var node = JsonNode.Parse(codec.Export(CompassPresetFactory.Create(_catalog, "chat"), _catalog))!;
        node["catalog"]!["usdPerCredit"] = .010000m;
        node["scenario"]!["calls"] = new JsonArray();

        var imported = codec.Import(node.ToJsonString(), _catalog);

        Assert.Empty(imported.Scenario.Calls);
        Assert.Equal(.01m, imported.Catalog.UsdPerCredit);
    }

    [Fact]
    public void DecimalStringsAreRejectedAsMalformedBundleValues()
    {
        var codec = new CompassBundleCodec(_json);
        var node = JsonNode.Parse(codec.Export(CompassPresetFactory.Create(_catalog, "chat"), _catalog))!;
        node["catalog"]!["usdPerCredit"] = "0.01";

        Assert.Throws<JsonException>(() => codec.Import(node.ToJsonString(), _catalog));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"schema\":\"github-copilot-cost-compass\",\"schemaVersion\":1}")]
    public void MalformedBundlesAreRejected(string json)
    {
        Assert.ThrowsAny<JsonException>(() => new CompassBundleCodec(_json).Import(json, _catalog));
    }

    [Fact]
    public void LegacyScenarioUsesActiveCatalogWithExplicitWarning()
    {
        var scenario = CompassPresetFactory.Create(_catalog, "cli");
        var result = new CompassBundleCodec(_json).Import(_json.Serialize(scenario), _catalog);

        Assert.Same(_catalog, result.Catalog);
        Assert.Contains("Legacy", result.Warning);
        Assert.Contains("active catalog", result.Warning);
        Assert.Equal(_json.Serialize(scenario), _json.Serialize(result.Scenario));
    }

    [Fact]
    public void ScenarioEditsInBundleKeepCatalogEvidence()
    {
        var codec = new CompassBundleCodec(_json);
        var node = JsonNode.Parse(codec.Export(CompassPresetFactory.Create(_catalog, "chat"), _catalog))!;
        node["scenario"]!["calls"]![0]!["outputTokens"] = 123;

        var result = codec.Import(node.ToJsonString(), _catalog);

        Assert.Equal(123, result.Scenario.Calls[0].OutputTokens);
        Assert.Equal(_catalog.Version, result.Catalog.Version);
    }

    [Theory]
    [InlineData("chat")]
    [InlineData("cloud-agent")]
    [InlineData("cli")]
    public void PresetsHavePinnedDateExplicitCallsAndNoImplicitConsumption(string operation)
    {
        var first = CompassPresetFactory.Create(_catalog, operation);
        var second = CompassPresetFactory.Create(_catalog, operation);

        Assert.Equal(_json.Serialize(first), _json.Serialize(second));
        Assert.Equal(CompassPresetFactory.SimulationDate, first.Timestamp);
        Assert.Equal(SimulationCheckScope.All, first.CheckScope);
        Assert.NotEmpty(first.Calls);
        Assert.Equal(operation == "cloud-agent", first.ActionsUsage is not null);
    }
}
