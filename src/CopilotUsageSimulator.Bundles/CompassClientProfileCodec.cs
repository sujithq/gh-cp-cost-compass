using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Bundles;

public sealed class CompassClientProfileCodec(CompassBundleCodec bundles)
{
    public const string Schema = "github-copilot-cost-compass-client-profiles";
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public CompassClientProfile Create(string bundle, string sourceFileName, EngineConfiguration activeCatalog)
    {
        if (string.IsNullOrWhiteSpace(sourceFileName))
        {
            throw new JsonException("A client profile requires its source file name.");
        }

        var imported = bundles.Import(bundle, activeCatalog);
        if (imported.Warning is not null)
        {
            throw new JsonException("A client profile must be a complete Compass bundle, not a legacy scenario.");
        }

        var metadata = imported.Scenario.Metadata;
        if (!string.Equals(Required(metadata, CompassClientProfileMetadata.ContractKey),
            CompassClientProfileMetadata.Contract, StringComparison.Ordinal))
        {
            throw new JsonException("The bundle does not declare the supported client profile contract.");
        }

        var enterprise = Required(metadata, CompassClientProfileMetadata.EnterpriseKey);
        var selectedUser = Required(metadata, CompassClientProfileMetadata.SelectedUserKey);
        var captureStartedAtText = Required(metadata, CompassClientProfileMetadata.CaptureStartedAtKey);
        var capturedAtText = Required(metadata, CompassClientProfileMetadata.CaptureCompletedAtKey);
        if (!DateTimeOffset.TryParseExact(captureStartedAtText, "O", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var captureStartedAt) ||
            !DateTimeOffset.TryParseExact(capturedAtText, "O", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var capturedAt) || captureStartedAt > capturedAt)
        {
            throw new JsonException("Client profile capture metadata is invalid.");
        }
        Sha256(metadata, CompassClientProfileMetadata.SnapshotSha256Key);
        Sha256(metadata, CompassClientProfileMetadata.OverridesSha256Key);
        Sha256(metadata, CompassClientProfileMetadata.IntegritySha256Key);
        if (!string.Equals(Required(metadata, CompassClientProfileMetadata.ScopeKey),
            CompassClientProfileMetadata.Scope, StringComparison.Ordinal))
        {
            throw new JsonException("The bundle does not declare the supported selected-user profile scope.");
        }
        if (!CompassClientProfileMetadata.HasValidIntegrityDigest(
            imported.Scenario, imported.Catalog, bundles))
        {
            throw new JsonException("The client profile integrity digest does not match its reconciled scenario and catalog.");
        }

        if (!string.Equals(imported.Scenario.BillingContext?.BillingEntityId, enterprise, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(imported.Scenario.Attribution?.UserId, selectedUser, StringComparison.OrdinalIgnoreCase) ||
            imported.Scenario.Timestamp != capturedAt)
        {
            throw new JsonException("Client profile identity or capture metadata does not match its scenario.");
        }
        if (imported.Scenario.Attribution is null || imported.Scenario.BillingContext is null)
        {
            throw new JsonException("A client profile requires billing and attribution snapshots.");
        }
        var attribution = new AttributionResolver().Resolve(imported.Scenario.Attribution, imported.Scenario.Timestamp);
        var selectedSeat = new EconomicBalanceCalculator(imported.Catalog).ResolveSelectedPlanSeat(imported.Scenario, attribution);
        if (attribution.Outcome != GuardrailOutcome.Passed || selectedSeat.Status != SelectedPlanSeatStatus.Matched)
        {
            throw new JsonException("The client profile must resolve one matching effective seat for its selected user.");
        }

        var safeSourceFileName = Path.GetFileName(sourceFileName.Trim());
        if (string.IsNullOrWhiteSpace(safeSourceFileName))
        {
            throw new JsonException("A client profile requires a valid source file name.");
        }

        return new CompassClientProfile
        {
            Id = ProfileId(enterprise, selectedUser),
            DisplayName = enterprise,
            SelectedUser = selectedUser,
            CapturedAt = capturedAt,
            IntegritySha256 = Required(metadata, CompassClientProfileMetadata.IntegritySha256Key),
            SourceFileName = safeSourceFileName,
            Bundle = bundle
        };
    }

    public string Export(IEnumerable<CompassClientProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var ordered = profiles.OrderBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.SelectedUser, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (ordered.GroupBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            throw new JsonException("Client profile identities must be unique.");
        }

        return JsonSerializer.Serialize(new StoredClientProfiles
        {
            Schema = Schema,
            SchemaVersion = SchemaVersion,
            Profiles = ordered.Select(profile => new StoredClientProfile
            {
                SourceFileName = profile.SourceFileName,
                Bundle = profile.Bundle
            }).ToArray()
        }, Options);
    }

    public IReadOnlyList<CompassClientProfile> Import(string json, EngineConfiguration activeCatalog)
    {
        var document = JsonSerializer.Deserialize<StoredClientProfiles>(json, Options)
            ?? throw new JsonException("The saved client profile collection is empty.");
        if (document.Schema != Schema || document.SchemaVersion != SchemaVersion || document.Profiles is null)
        {
            throw new JsonException("Unsupported or incomplete client profile collection.");
        }

        var profiles = document.Profiles.Select(profile =>
        {
            if (profile is null || string.IsNullOrWhiteSpace(profile.Bundle) || string.IsNullOrWhiteSpace(profile.SourceFileName))
            {
                throw new JsonException("A saved client profile is incomplete.");
            }
            return Create(profile.Bundle, profile.SourceFileName, activeCatalog);
        }).OrderBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.SelectedUser, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (profiles.GroupBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            throw new JsonException("The saved client profile collection contains duplicate identities.");
        }
        return profiles;
    }

    private static string Required(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new JsonException($"Client profile metadata '{key}' is required.");

    private static void Sha256(IReadOnlyDictionary<string, string> metadata, string key)
    {
        var value = Required(metadata, key);
        try
        {
            if (Convert.FromHexString(value).Length != SHA256.HashSizeInBytes)
            {
                throw new JsonException($"Client profile metadata '{key}' must be a SHA-256 hash.");
            }
        }
        catch (FormatException exception)
        {
            throw new JsonException($"Client profile metadata '{key}' must be a SHA-256 hash.", exception);
        }
    }

    private static string ProfileId(string enterprise, string selectedUser) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{enterprise.Trim().ToUpperInvariant()}\n{selectedUser.Trim().ToUpperInvariant()}")));

    private sealed record StoredClientProfiles
    {
        public required string Schema { get; init; }
        public required int SchemaVersion { get; init; }
        public required IReadOnlyList<StoredClientProfile?> Profiles { get; init; }
    }

    private sealed record StoredClientProfile
    {
        public required string SourceFileName { get; init; }
        public required string Bundle { get; init; }
    }
}

public sealed record CompassClientProfile
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string SelectedUser { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }
    public required string IntegritySha256 { get; init; }
    public required string SourceFileName { get; init; }
    public required string Bundle { get; init; }
}

public static class CompassClientProfileMetadata
{
    public const string ContractKey = "import.profileContract";
    public const string Contract = "cost-compass-client-profile/v1";
    public const string EnterpriseKey = "import.enterprise";
    public const string SelectedUserKey = "import.selectedUser";
    public const string CaptureStartedAtKey = "import.captureStartedAt";
    public const string CaptureCompletedAtKey = "import.captureCompletedAt";
    public const string SnapshotSha256Key = "import.snapshotSha256";
    public const string OverridesSha256Key = "import.overridesSha256";
    public const string IntegritySha256Key = "import.profileIntegritySha256";
    public const string ScopeKey = "import.scope";
    public const string Scope = "One selected-user what-if; export again for another user. Reported usage is not guaranteed current.";

    public static SimulationScenario AddIntegrityDigest(
        SimulationScenario scenario,
        EngineConfiguration catalog,
        CompassBundleCodec bundles)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(bundles);
        var metadata = UnsignedMetadata(scenario.Metadata);
        var unsigned = scenario with { Metadata = metadata };
        metadata[IntegritySha256Key] = bundles.BundleContentHash(unsigned, catalog);
        return scenario with { Metadata = metadata };
    }

    public static bool HasValidIntegrityDigest(
        SimulationScenario scenario,
        EngineConfiguration catalog,
        CompassBundleCodec bundles)
    {
        if (!scenario.Metadata.TryGetValue(IntegritySha256Key, out var actual)) return false;
        var unsigned = scenario with { Metadata = UnsignedMetadata(scenario.Metadata) };
        return bundles.MatchesBundleContentHash(unsigned, catalog, actual);
    }

    private static Dictionary<string, string> UnsignedMetadata(IReadOnlyDictionary<string, string> source) =>
        source.Where(item => !string.Equals(item.Key, IntegritySha256Key, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

}