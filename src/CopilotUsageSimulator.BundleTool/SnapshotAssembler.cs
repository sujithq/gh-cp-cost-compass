using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CopilotUsageSimulator.Bundles;
using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.BundleTool;

public sealed record BundleCreation(string Json, ImportReport Report);

public sealed class SnapshotAssembler
{
    public BundleCreation Create(EnterpriseSnapshot snapshot, WorkloadInput workload, ImportOverrides overrides, EngineConfiguration catalog)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(workload);
        ArgumentNullException.ThrowIfNull(overrides);
        EngineConfigurationValidator.Validate(catalog);
        ImportChecks.Require(workload.SchemaVersion == 1, "workload-version-unsupported", "Expected workload schema version 1.");
        ImportChecks.Identifier(workload.OperationId, "workload.operationId");
        ImportChecks.Items(workload.Calls, "workload.calls");
        ImportChecks.Require(workload.Calls.Count > 0 && workload.Calls.Any(call =>
            call.FreshInputTokens > 0 || call.CachedInputTokens > 0 || call.CacheWriteTokens > 0 || call.OutputTokens > 0),
            "workload-missing", "Supply at least one explicitly sized model call; collection does not invent future workload demand.");
        ValidateScope(workload, catalog);
        var inventory = new SnapshotIdentityResolver().Resolve(snapshot, overrides);
        var diagnostics = new List<ImportDiagnostic>
        {
            new("warning", "observed-not-atomic", "capturedAt",
                "Configuration was observed over the capture interval; usage reports may lag. This is not an atomic live balance or a reconstruction of historical settings."),
            new("info", "confirmed-financial-values", "overrides",
                "Pool entitlement/consumption, exclusions and supplied field overrides are operator confirmations, not values inferred from API discounts.")
        };
        if (workload.CheckScope == SimulationCheckScope.CostRelatedOnly)
        {
            diagnostics.Add(new("warning", "cost-only", "workload.checkScope", "Access and runtime checks are explicitly excluded; no live permission or execution claim is made."));
        }
        diagnostics.AddRange(snapshot.Sources.Where(source => !source.Complete).Select(source =>
            new ImportDiagnostic("warning", "source-incomplete", source.Dataset, source.Problem ?? "This optional source was incomplete; no missing value was inferred from it.")));
        var mapper = new SnapshotFinancialMapper(snapshot, overrides, catalog, inventory, diagnostics);
        var economic = mapper.Map();
        var cloud = ImportChecks.Same(workload.OperationId, "cloud-agent");
        ImportChecks.Require(cloud == (workload.Actions is not null), "actions-context-required",
            "Supply an Actions account, whole minutes, allowance and confirmed budget IDs for cloud-agent only.");
        var actions = workload.Actions is null ? null : mapper.MapActions(workload.Actions);
        mapper.ReportUnusedConfirmations();
        var metadata = new Dictionary<string, string>
        {
            ["import.enterprise"] = snapshot.Enterprise,
            ["import.selectedUser"] = inventory.Attribution.UserId,
            ["import.captureStartedAt"] = snapshot.CapturedAt.ToString("O", CultureInfo.InvariantCulture),
            ["import.captureCompletedAt"] = snapshot.CaptureCompletedAt.ToString("O", CultureInfo.InvariantCulture),
            ["import.snapshotSha256"] = Hash(ImportJson.Write(snapshot)),
            ["import.overridesSha256"] = Hash(ImportJson.Write(overrides)),
            ["import.scope"] = "One selected-user what-if; export again for another user. Reported usage is not guaranteed current."
        };
        if (workload.Actions is not null) metadata["actionsAccount"] = workload.Actions.Account;
        var scenario = new SimulationScenario
        {
            OperationId = workload.OperationId,
            PlanId = inventory.PlanId,
            Timestamp = snapshot.CaptureCompletedAt.ToUniversalTime(),
            CheckScope = workload.CheckScope,
            RepositoryVisibility = RepositoryVisibility.Private,
            Calls = workload.Calls,
            AccessGates = workload.AccessGates,
            RuntimeGuardrails = workload.RuntimeGuardrails,
            Attribution = inventory.Attribution,
            BillingContext = inventory.Billing,
            EconomicGuardrails = economic,
            ActionsGuardrails = actions,
            ActionsUsage = workload.Actions is null ? null : new ActionsUsageInput
            {
                RunnerId = "linux-2-core", Minutes = workload.Actions.Minutes,
                IncludedMinutesRemaining = workload.Actions.IncludedMinutesRemaining
            },
            Metadata = metadata
        };
        SimulationScenarioValidator.Validate(scenario);
        var json = new CompassBundleCodec(new ScenarioJson()).Export(scenario, catalog);
        var inspected = BundleInspector.Validate(json);
        var report = inspected with
        {
            Command = "create", Enterprise = snapshot.Enterprise, User = inventory.Attribution.UserId,
            CapturedAt = snapshot.CaptureCompletedAt,
            Confirmations = overrides,
            Diagnostics = [.. diagnostics, .. inspected.Diagnostics]
        };
        return new BundleCreation(json, report);
    }

    private static void ValidateScope(WorkloadInput workload, EngineConfiguration catalog)
    {
        ImportChecks.Require(Enum.IsDefined(workload.CheckScope), "invalid-check-scope", "Unknown workload check scope.");
        ImportChecks.Require(workload.AccessGates is not null && workload.AccessGates.Values.All(gate => gate is not null),
            "invalid-access-snapshot", "Access gates must not be null.");
        if (workload.CheckScope != SimulationCheckScope.All) return;
        ImportChecks.Require(workload.RuntimeGuardrails is not null, "runtime-unconfirmed",
            "Full scope requires an explicit runtime snapshot, including an empty limits snapshot when that is the confirmed assumption.");
        foreach (var gate in catalog.Gates.Where(gate => gate.ApplicableOperationIds.Count == 0 ||
            gate.ApplicableOperationIds.Contains(workload.OperationId, StringComparer.OrdinalIgnoreCase)))
        {
            ImportChecks.Require(workload.AccessGates!.Keys.Contains(gate.Id, StringComparer.OrdinalIgnoreCase),
                "access-unconfirmed", $"Full scope requires an explicit value for access gate '{gate.Id}'; PassWhenUnspecified is not import evidence.");
        }
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public static class BundleInspector
{
    public static ImportReport Validate(string json)
    {
        ImportChecks.Require(Encoding.UTF8.GetByteCount(json) <= CompassBundleCodec.MaximumFileBytes,
            "bundle-too-large", "The bundle exceeds the app's 2 MiB file-import limit. No seat inventory was pruned; retain the snapshot and reconcile the app capacity before export.");
        using var document = JsonDocument.Parse(json);
        ImportChecks.Require(document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("schema", out _),
            "bundle-required", "Expected a versioned Compass bundle, not legacy scenario-only JSON.");
        CompassImportedScenario imported;
        try
        {
            imported = new CompassBundleCodec(new ScenarioJson()).Import(json, EngineConfigurationLoader.LoadDefault());
        }
        catch (JsonException exception)
        {
            throw new ImportException("bundle-incompatible", exception.Message);
        }
        var preview = new SimulationPreviewService(imported.Catalog).Preview(imported.Scenario);
        if (preview.ErrorCode is not null)
        {
            throw new ImportException(preview.ErrorCode, preview.ErrorMessage ?? "The bundle cannot be simulated by this Compass profile.");
        }
        ImportChecks.Require(preview.Result is not null, "preview-unavailable", "No simulated result was produced.");
        return new ImportReport
        {
            Command = "validate", Enterprise = imported.Scenario.BillingContext?.BillingEntityId,
            User = imported.Scenario.Attribution?.UserId,
            CapturedAt = imported.Scenario.Timestamp,
            CatalogSha256 = CompassBundleCodec.CatalogHash(imported.Catalog),
            PreviewDecision = preview.Result!.Decision.ToString(), FirstFailingGate = preview.Result.FirstFailingGate,
            Diagnostics = preview.Assumptions.Select(message => new ImportDiagnostic("info", "preview-assumption", "scenario", message)).ToArray()
        };
    }
}