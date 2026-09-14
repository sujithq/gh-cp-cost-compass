using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Bundles;

public sealed class CompassBundleCodec(ScenarioJson scenarioJson)
{
    public const string Schema = "github-copilot-cost-compass";
    public const int SchemaVersion = 1;
    public const string EngineContract = "copilot-usage-simulator/compass-v1";
    public const int MaximumFileBytes = 2 * 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
            new CanonicalDecimalConverter()
        }
    };

    public string Export(SimulationScenario scenario, EngineConfiguration catalog)
    {
        SimulationScenarioValidator.Validate(scenario);
        EngineConfigurationValidator.Validate(catalog);
        return JsonSerializer.Serialize(new CompassScenarioBundle
        {
            Schema = Schema,
            SchemaVersion = SchemaVersion,
            EngineContract = EngineContract,
            Scenario = scenario,
            Catalog = catalog,
            CatalogSha256 = CatalogHash(catalog)
        }, Options);
    }

    public CompassImportedScenario Import(string json, EngineConfiguration activeCatalog)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Expected a Cost Compass bundle or legacy scenario object.");
        }

        if (!document.RootElement.TryGetProperty("schema", out _))
        {
            if (!document.RootElement.TryGetProperty("OperationId", out _) &&
                !document.RootElement.TryGetProperty("operationId", out _))
            {
                throw new JsonException("The document is not a Cost Compass bundle or a legacy scenario.");
            }

            return new CompassImportedScenario(scenarioJson.Deserialize(json), activeCatalog,
                "Legacy scenario imported with the active catalog, not its original pricing evidence. Review the reference before simulating.");
        }

        var bundle = JsonSerializer.Deserialize<CompassScenarioBundle>(json, Options)
            ?? throw new JsonException("The bundle is empty.");
        if (bundle.Schema != Schema || bundle.SchemaVersion != SchemaVersion ||
            bundle.EngineContract != EngineContract)
        {
            throw new JsonException("Unsupported bundle schema, version, or engine contract. Expected Cost Compass v1.");
        }

        if (bundle.Scenario is null || bundle.Catalog is null || string.IsNullOrWhiteSpace(bundle.CatalogSha256))
        {
            throw new JsonException("The bundle must contain a scenario, catalog, and catalog fingerprint.");
        }

        SimulationScenarioValidator.Validate(bundle.Scenario);
        EngineConfigurationValidator.Validate(bundle.Catalog);
        if (!string.Equals(bundle.CatalogSha256, CatalogHash(bundle.Catalog), StringComparison.OrdinalIgnoreCase))
        {
            throw new JsonException("The catalog fingerprint does not match the saved reference. The current scenario was not replaced.");
        }

        return new CompassImportedScenario(bundle.Scenario, bundle.Catalog, null);
    }

    public static string CatalogHash(EngineConfiguration catalog) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(catalog, Options))));

    private sealed class CanonicalDecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.Number || !reader.TryGetDecimal(out var value))
            {
                throw new JsonException("Numeric bundle values must be finite JSON decimals.");
            }
            return value;
        }

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
            writer.WriteRawValue(value.ToString("G29", CultureInfo.InvariantCulture));
    }
}

public sealed record CompassScenarioBundle
{
    public required string Schema { get; init; }
    public required int SchemaVersion { get; init; }
    public required string EngineContract { get; init; }
    public required SimulationScenario Scenario { get; init; }
    public required EngineConfiguration Catalog { get; init; }
    public required string CatalogSha256 { get; init; }
}

public sealed record CompassImportedScenario(
    SimulationScenario Scenario,
    EngineConfiguration Catalog,
    string? Warning);