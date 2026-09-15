using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CopilotUsageSimulator.Engine;
using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Guardrails;
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
    public void CatalogFingerprintUsesCanonicalNewlines()
    {
        var canonicalCatalog = JsonSerializer.Serialize(_catalog, LegacyBundleOptions).ReplaceLineEndings("\n");

        Assert.Equal(Hash(canonicalCatalog), CompassBundleCodec.CatalogHash(_catalog));
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

    [Fact]
    public void ClientProfileCollectionRoundTripsMultipleSnapshotDerivedBundles()
    {
        var bundles = new CompassBundleCodec(_json);
        var profiles = new CompassClientProfileCodec(bundles);
        var contoso = profiles.Create(ClientBundle("contoso", "user-1"), "contoso.compass.json", _catalog);
        var fabrikam = profiles.Create(ClientBundle("fabrikam", "user-1"), "fabrikam.compass.json", _catalog);

        var json = profiles.Export([fabrikam, contoso]);
        var restored = profiles.Import(json, _catalog);

        Assert.Equal(["contoso", "fabrikam"], restored.Select(profile => profile.DisplayName));
        Assert.Equal(2, restored.Select(profile => profile.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain("selectedProfile", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClientProfileRejectsLegacyOrIdentityMismatchedScenarios()
    {
        var bundles = new CompassBundleCodec(_json);
        var profiles = new CompassClientProfileCodec(bundles);
        var legacy = _json.Serialize(CompassPresetFactory.Create(_catalog, "chat"));
        var mismatched = ClientBundle("contoso", "another-user");

        Assert.Throws<JsonException>(() => profiles.Create(legacy, "legacy.json", _catalog));
        Assert.Throws<JsonException>(() => profiles.Create(mismatched, "contoso.compass.json", _catalog));
    }

    [Fact]
    public void ClientProfileRejectsEditsAfterBundleToolIntegrityDigest()
    {
        var bundles = new CompassBundleCodec(_json);
        var profiles = new CompassClientProfileCodec(bundles);
        var imported = bundles.Import(ClientBundle("contoso", "user-1"), _catalog);
        var edited = bundles.Export(imported.Scenario with
        {
            Calls = [imported.Scenario.Calls[0] with { OutputTokens = 123 }]
        }, imported.Catalog);

        var exception = Assert.Throws<JsonException>(() =>
            profiles.Create(edited, "edited.compass.json", _catalog));

        Assert.Contains("integrity digest", exception.Message);
    }

    [Fact]
    public void ClientProfileAcceptsLegacyWindowsNewlineFingerprints()
    {
        var bundles = new CompassBundleCodec(_json);
        var profiles = new CompassClientProfileCodec(bundles);
        var scenario = bundles.Import(ClientBundle("contoso", "user-1"), _catalog).Scenario;
        var metadata = scenario.Metadata
            .Where(item => item.Key != CompassClientProfileMetadata.IntegritySha256Key)
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var unsigned = scenario with { Metadata = metadata };
        metadata[CompassClientProfileMetadata.IntegritySha256Key] = Hash(LegacyWindowsBundle(unsigned, _catalog));
        var legacyBundle = LegacyWindowsBundle(scenario with { Metadata = metadata }, _catalog);

        var profile = profiles.Create(legacyBundle, "contoso.compass.json", _catalog);

        Assert.Equal("contoso", profile.DisplayName);
        Assert.Equal("user-1", profile.SelectedUser);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("expired")]
    [InlineData("ambiguous")]
    [InlineData("unknown-plan")]
    [InlineData("conflicting-plan")]
    public void ClientProfileRequiresOneMatchedEffectiveSelectedUserSeat(string state)
    {
        var bundles = new CompassBundleCodec(_json);
        var profiles = new CompassClientProfileCodec(bundles);
        var imported = bundles.Import(ClientBundle("contoso", "user-1"), _catalog);
        var scenario = imported.Scenario;
        var selectedSeat = scenario.BillingContext!.SeatAssignments.Single(seat => seat.UserId == "user-1");
        var otherSeats = scenario.BillingContext.SeatAssignments.Where(seat => seat.UserId != "user-1");
        IReadOnlyList<EffectiveSeatAssignment> selectedSeats = state switch
        {
            "missing" => [],
            "expired" => [selectedSeat with { EffectiveTo = scenario.Timestamp }],
            "ambiguous" => [selectedSeat, selectedSeat with { CostCenterId = "cc-platform" }],
            "unknown-plan" => [selectedSeat with { PlanId = "unknown-plan" }],
            "conflicting-plan" => [selectedSeat with { PlanId = "enterprise" }],
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
        scenario = scenario with
        {
            BillingContext = scenario.BillingContext with
            {
                SeatAssignments = otherSeats.Concat(selectedSeats).ToArray()
            }
        };
        if (state == "ambiguous")
        {
            Assert.Throws<SimulationException>(() =>
                CompassClientProfileMetadata.AddIntegrityDigest(scenario, imported.Catalog, bundles));
            return;
        }
        scenario = CompassClientProfileMetadata.AddIntegrityDigest(scenario, imported.Catalog, bundles);

        var exception = Assert.Throws<JsonException>(() => profiles.Create(
            bundles.Export(scenario, imported.Catalog), "invalid-seat.compass.json", _catalog));

        Assert.Contains("matching effective seat", exception.Message);
    }

    [Fact]
    public void SameEnterpriseCanStoreSeparateSelectedUserProfiles()
    {
        var codec = new CompassClientProfileCodec(new CompassBundleCodec(_json));
        var first = codec.Create(ClientBundle("contoso", "user-1"), "contoso-user-1.compass.json", _catalog);
        var second = codec.Create(ClientBundle("contoso", "user-2", "user-2"), "contoso-user-2.compass.json", _catalog);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(["user-1", "user-2"], codec.Import(codec.Export([first, second]), _catalog)
            .Select(profile => profile.SelectedUser));
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

    private string ClientBundle(string enterprise, string metadataUser, string selectedUser = "user-1")
    {
        var scenario = CompassPresetFactory.Create(_catalog, "chat");
        var metadata = new Dictionary<string, string>(scenario.Metadata)
        {
            [CompassClientProfileMetadata.ContractKey] = CompassClientProfileMetadata.Contract,
            [CompassClientProfileMetadata.EnterpriseKey] = enterprise,
            [CompassClientProfileMetadata.SelectedUserKey] = metadataUser,
            [CompassClientProfileMetadata.CaptureStartedAtKey] = scenario.Timestamp.AddMinutes(-1).ToString("O"),
            [CompassClientProfileMetadata.CaptureCompletedAtKey] = scenario.Timestamp.ToString("O"),
            [CompassClientProfileMetadata.SnapshotSha256Key] = new string('a', 64),
            [CompassClientProfileMetadata.OverridesSha256Key] = new string('b', 64),
            [CompassClientProfileMetadata.ScopeKey] = CompassClientProfileMetadata.Scope
        };
        var bundles = new CompassBundleCodec(_json);
        var profileScenario = scenario with
        {
            PlanId = selectedUser == "user-2" ? "enterprise" : scenario.PlanId,
            BillingContext = scenario.BillingContext! with { BillingEntityId = enterprise },
            Attribution = scenario.Attribution! with { UserId = selectedUser },
            Metadata = metadata
        };
        return bundles.Export(
            CompassClientProfileMetadata.AddIntegrityDigest(profileScenario, _catalog, bundles), _catalog);
    }

    private static readonly JsonSerializerOptions LegacyBundleOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
            new CanonicalDecimalConverter()
        }
    };

    private static string LegacyWindowsBundle(SimulationScenario scenario, EngineConfiguration catalog)
    {
        var catalogJson = JsonSerializer.Serialize(catalog, LegacyBundleOptions).ReplaceLineEndings("\r\n");
        return JsonSerializer.Serialize(new CompassScenarioBundle
        {
            Schema = CompassBundleCodec.Schema,
            SchemaVersion = CompassBundleCodec.SchemaVersion,
            EngineContract = CompassBundleCodec.EngineContract,
            Scenario = scenario,
            Catalog = catalog,
            CatalogSha256 = Hash(catalogJson)
        }, LegacyBundleOptions).ReplaceLineEndings("\r\n");
    }

    private static string Hash(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private sealed class CanonicalDecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetDecimal();

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
            writer.WriteRawValue(value.ToString("G29", CultureInfo.InvariantCulture));
    }
}
