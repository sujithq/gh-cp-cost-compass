using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Engine.Reference;

public enum ModelEligibilityStatus
{
    Available,
    Unavailable,
    Unsupported
}

public sealed record ModelEligibilityResult
{
    public required string ModelId { get; init; }
    public required ModelEligibilityStatus Status { get; init; }
    public required string ReasonCode { get; init; }
    public required string Message { get; init; }
    public string? ReferenceSnapshotId { get; init; }
    public IReadOnlyList<ReferenceSourceDefinition> EvidenceSources { get; init; } = [];
    public IReadOnlyList<string> Assumptions { get; init; } = [];
    public ModelPricePeriod? PricePeriod { get; init; }
    public TokenPriceTier? PriceTier { get; init; }
}

public static class ModelEligibilityReasonCodes
{
    public const string Available = "model-available";
    public const string Unverified = "model-eligibility-unverified";
    public const string NotYetAvailable = "model-not-yet-available";
    public const string Retired = "model-retired";
    public const string PlanIneligible = "model-plan-ineligible";
    public const string PricingAvailable = "pricing-available";
    public const string PricingNotEffective = "pricing-not-effective";
    public const string PricingTierNotFound = "pricing-tier-not-found";
    public const string TokenComponentUnpriced = "token-component-unpriced";
    public const string MultiplierNotApplicable = "multiplier-not-applicable";
    public const string MultiplierUnsupported = "multiplier-reference-unsupported";
    public const string MultiplierUnverified = "multiplier-eligibility-unverified";
    public const string AutoModelUnsupported = "auto-model-not-supported";
    public const string AutoModelUnverified = "auto-model-eligibility-unverified";
}

public sealed class ModelEligibilityEvaluator
{
    private readonly EngineConfiguration _configuration;

    public ModelEligibilityEvaluator(EngineConfiguration configuration)
    {
        EngineConfigurationValidator.Validate(configuration);
        _configuration = configuration;
    }

    public ModelEligibilityResult Evaluate(
        string modelId,
        string planId,
        string operationId,
        DateTimeOffset timestamp,
        ModelCallInput? call = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ValidateCall(modelId, call);

        var model = FindModel(modelId);
        if (model is null)
        {
            return UnknownModel(modelId);
        }

        if (!_configuration.Plans.Any(x => SameId(x.Id, planId)))
        {
            return Result(model, ModelEligibilityStatus.Unsupported, "unknown-plan",
                $"Plan '{planId}' is not in the catalog.");
        }

        if (!_configuration.Operations.Any(x => SameId(x.Id, operationId)))
        {
            return Result(model, ModelEligibilityStatus.Unsupported, "unknown-operation",
                $"Operation '{operationId}' is not in the catalog.");
        }

        var availability = EvaluateAvailability(modelId, timestamp);
        if (availability.Status != ModelEligibilityStatus.Available)
        {
            return availability;
        }

        var plans = model.Availability!.EligiblePlanIds;
        if (plans is null || model.SupportedTokenComponents is null)
        {
            return Result(model, ModelEligibilityStatus.Unsupported, ModelEligibilityReasonCodes.Unverified,
                $"Model '{model.Id}' does not have verified plan eligibility and token-component coverage.");
        }

        if (!plans.Contains(planId, StringComparer.OrdinalIgnoreCase))
        {
            return model.Availability.EligiblePlansAreExhaustive
                ? Result(model, ModelEligibilityStatus.Unavailable, ModelEligibilityReasonCodes.PlanIneligible,
                    $"Model '{model.Id}' is not eligible for plan '{planId}' in this reference.")
                : Result(model, ModelEligibilityStatus.Unsupported, ModelEligibilityReasonCodes.Unverified,
                    $"The non-exhaustive eligibility evidence for '{model.Id}' does not establish access for '{planId}'.");
        }

        var pricing = EvaluatePricing(modelId, timestamp, call);
        if (pricing.Status != ModelEligibilityStatus.Available)
        {
            return pricing;
        }

        var sourceIds = new List<string>(pricing.EvidenceSources.Select(x => x.Id));
        foreach (var multiplierId in call?.EnabledMultiplierIds ?? [])
        {
            var multiplier = _configuration.Multipliers.SingleOrDefault(x => SameId(x.Id, multiplierId));
            if (multiplier is null)
            {
                return Result(model, ModelEligibilityStatus.Unsupported, "unknown-multiplier",
                    $"Multiplier '{multiplierId}' is not in the catalog.");
            }

            sourceIds.AddRange(multiplier.SourceIds ?? []);
            if (!Applies(multiplier.ApplicableOperationIds, operationId) ||
                !Applies(multiplier.ApplicableModelIds, model.Id) ||
                (multiplier.ApplicablePlanIds is { } applicablePlans &&
                 !applicablePlans.Contains(planId, StringComparer.OrdinalIgnoreCase)))
            {
                return Result(model, ModelEligibilityStatus.Unsupported, ModelEligibilityReasonCodes.MultiplierNotApplicable,
                    $"Multiplier '{multiplier.Id}' does not apply to the selected plan, operation, and model.",
                    sourceIds);
            }

            if (multiplier.IsReferenceSupported != true)
            {
                var explicitlyUnsupported = multiplier.IsReferenceSupported == false;
                return Result(model, ModelEligibilityStatus.Unsupported,
                    explicitlyUnsupported
                        ? ModelEligibilityReasonCodes.MultiplierUnsupported
                        : ModelEligibilityReasonCodes.MultiplierUnverified,
                    explicitlyUnsupported
                        ? $"Multiplier '{multiplier.Id}' and its policy/model restrictions or stacking are explicitly unsupported by this reference."
                        : $"Multiplier '{multiplier.Id}' has no verified applicability evidence.",
                    sourceIds);
            }

            if (SameId(multiplier.Id, "auto-model-selection"))
            {
                var autoOperations = model.Availability.AutoModelSelectionOperationIds;
                if (autoOperations is null)
                {
                    return Result(model, ModelEligibilityStatus.Unsupported, ModelEligibilityReasonCodes.AutoModelUnverified,
                        $"Auto routing to '{model.Id}' is not verified by this reference.", sourceIds);
                }

                if (!autoOperations.Contains(operationId, StringComparer.OrdinalIgnoreCase))
                {
                    return Result(model, ModelEligibilityStatus.Unsupported, ModelEligibilityReasonCodes.AutoModelUnsupported,
                        $"The reference does not support Auto routing to '{model.Id}' in '{operationId}'.", sourceIds);
                }
            }
        }

        return Result(model, ModelEligibilityStatus.Available, ModelEligibilityReasonCodes.Available,
            $"The reference supports pricing and plan eligibility for '{model.Id}'; tenant rollout and policy remain independent.",
            sourceIds) with
        {
            PricePeriod = pricing.PricePeriod,
            PriceTier = pricing.PriceTier
        };
    }

    public IReadOnlyList<ModelDefinition> GetSelectableModels(
        string planId,
        string operationId,
        DateTimeOffset timestamp) =>
        _configuration.Models
            .Where(model => Evaluate(model.Id, planId, operationId, timestamp).Status == ModelEligibilityStatus.Available)
            .ToArray();

    public ModelEligibilityResult EvaluateAvailability(string modelId, DateTimeOffset timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        var model = FindModel(modelId);
        if (model is null)
        {
            return UnknownModel(modelId);
        }

        var availability = model.Availability;
        if (availability?.AvailableFrom is { } availableFrom && timestamp < availableFrom)
        {
            return Result(model, ModelEligibilityStatus.Unavailable, ModelEligibilityReasonCodes.NotYetAvailable,
                $"Model '{model.Id}' is not available before the modeled inclusive boundary {availableFrom:O}.");
        }

        if (availability?.RetiredAt is { } retiredAt && timestamp >= retiredAt)
        {
            return Result(model, ModelEligibilityStatus.Unavailable, ModelEligibilityReasonCodes.Retired,
                $"Model '{model.Id}' is retired at the modeled exclusive boundary {retiredAt:O}, independently of any remaining tariff.");
        }

        var verifiedFrom = availability?.VerifiedFrom ?? availability?.AvailableFrom;
        if (verifiedFrom is null || timestamp < verifiedFrom)
        {
            return Result(model, ModelEligibilityStatus.Unsupported, ModelEligibilityReasonCodes.Unverified,
                $"Model '{model.Id}' has no positive availability evidence covering the selected timestamp; tariff coverage alone is insufficient.");
        }

        return Result(model, ModelEligibilityStatus.Available, ModelEligibilityReasonCodes.Available,
            $"Model '{model.Id}' is within its evidenced availability window; pricing, plans, rollout, and policy are separate checks.");
    }

    public ModelEligibilityResult EvaluatePricing(
        string modelId,
        DateTimeOffset timestamp,
        ModelCallInput? call = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ValidateCall(modelId, call);
        var model = FindModel(modelId);
        if (model is null)
        {
            return UnknownModel(modelId);
        }

        var period = model.PricePeriods.SingleOrDefault(x =>
            timestamp >= x.EffectiveFrom && (x.EffectiveTo is null || timestamp < x.EffectiveTo));
        if (period is null)
        {
            return Result(model, ModelEligibilityStatus.Unsupported, ModelEligibilityReasonCodes.PricingNotEffective,
                $"Model '{model.Id}' has no pricing effective at {timestamp:O}; no successor rate is inferred.");
        }

        var contextTokens = call?.ContextTokens ?? 0;
        var tier = period.Tiers.SingleOrDefault(x =>
            (x.MinimumContextTokensExclusive is null || contextTokens > x.MinimumContextTokensExclusive) &&
            (x.MaximumContextTokensInclusive is null || contextTokens <= x.MaximumContextTokensInclusive));
        if (tier is null)
        {
            return Result(model, ModelEligibilityStatus.Unsupported, ModelEligibilityReasonCodes.PricingTierNotFound,
                $"Model '{model.Id}' has no tier for {contextTokens} context tokens.", period.SourceIds);
        }

        if (call is not null && model.SupportedTokenComponents is { } components)
        {
            var unsupported = UsedComponents(call).FirstOrDefault(component => !components.Contains(component));
            if (UsedComponents(call).Any(component => !components.Contains(component)))
            {
                return Result(model, ModelEligibilityStatus.Unsupported, ModelEligibilityReasonCodes.TokenComponentUnpriced,
                    $"Model '{model.Id}' has no published supported rate for {unsupported}; it cannot be priced as zero.",
                    period.SourceIds);
            }
        }

        return Result(model, ModelEligibilityStatus.Available, ModelEligibilityReasonCodes.PricingAvailable,
            $"The configured tariff '{tier.Id}' covers this input; this is not a model-availability or plan-eligibility approval.",
            period.SourceIds) with
        {
            PricePeriod = period,
            PriceTier = tier
        };
    }

    private ModelDefinition? FindModel(string modelId) =>
        _configuration.Models.SingleOrDefault(x => SameId(x.Id, modelId));

    private ModelEligibilityResult UnknownModel(string modelId) =>
        new()
        {
            ModelId = modelId,
            Status = ModelEligibilityStatus.Unsupported,
            ReasonCode = "unknown-model",
            Message = $"Model '{modelId}' is not in the catalog.",
            ReferenceSnapshotId = _configuration.ReferenceSnapshot?.Id,
            Assumptions = _configuration.ReferenceSnapshot?.Assumptions ?? []
        };

    private ModelEligibilityResult Result(
        ModelDefinition model,
        ModelEligibilityStatus status,
        string reasonCode,
        string message,
        IEnumerable<string>? additionalSourceIds = null)
    {
        var sourceIds = new HashSet<string>(model.SourceIds ?? [], StringComparer.OrdinalIgnoreCase);
        sourceIds.UnionWith(model.Availability?.SourceIds ?? []);
        sourceIds.UnionWith(additionalSourceIds ?? []);
        var snapshot = _configuration.ReferenceSnapshot;
        var assumptions = new List<string>(snapshot?.Assumptions ?? []);
        if (model.Availability?.Notes is { } notes)
        {
            assumptions.Add(notes);
        }

        return new ModelEligibilityResult
        {
            ModelId = model.Id,
            Status = status,
            ReasonCode = reasonCode,
            Message = message,
            ReferenceSnapshotId = snapshot?.Id,
            EvidenceSources = snapshot?.Sources.Where(source => sourceIds.Contains(source.Id)).ToArray() ?? [],
            Assumptions = assumptions
        };
    }

    private static IEnumerable<TokenComponent> UsedComponents(ModelCallInput call)
    {
        if (call.FreshInputTokens > 0) yield return TokenComponent.FreshInput;
        if (call.CachedInputTokens > 0) yield return TokenComponent.CachedInput;
        if (call.CacheWriteTokens > 0) yield return TokenComponent.CacheWrite;
        if (call.OutputTokens > 0) yield return TokenComponent.Output;
    }

    private static void ValidateCall(string modelId, ModelCallInput? call)
    {
        if (call is null)
        {
            return;
        }

        if (!SameId(modelId, call.ModelId) ||
            call.ContextTokens < 0 || call.FreshInputTokens < 0 ||
            call.CachedInputTokens < 0 || call.CacheWriteTokens < 0 || call.OutputTokens < 0 ||
            call.EnabledMultiplierIds is null ||
            call.EnabledMultiplierIds.Any(string.IsNullOrWhiteSpace) ||
            call.EnabledMultiplierIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != call.EnabledMultiplierIds.Count)
        {
            throw new ArgumentException("The model call must match the selected model and contain nonnegative tokens and unique multiplier IDs.", nameof(call));
        }
    }

    private static bool SameId(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool Applies(IReadOnlySet<string> ids, string id) =>
        ids.Count == 0 || ids.Contains(id, StringComparer.OrdinalIgnoreCase);
}
