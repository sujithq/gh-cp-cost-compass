namespace CopilotUsageSimulator.Engine.Configuration;

public static partial class EngineConfigurationValidator
{
    public static void Validate(EngineConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.UsdPerCredit <= 0)
        {
            throw new ConfigurationException("usdPerCredit must be greater than zero.");
        }

        RequireDefined(configuration.PoolOverflowBehavior, "poolOverflowBehavior");
        Require(configuration.Plans, "plans");
        Require(configuration.Models, "models");
        Require(configuration.Operations, "operations");
        Require(configuration.Gates, "gates");
        Require(configuration.Multipliers, "multipliers");
        Require(configuration.ActionsRunners, "actionsRunners");
        Require(configuration.ExampleScenario, "exampleScenario");
        RequireItems(configuration.Plans, "plans");
        RequireItems(configuration.Models, "models");
        RequireItems(configuration.Operations, "operations");
        RequireItems(configuration.Gates, "gates");
        RequireItems(configuration.Multipliers, "multipliers");
        RequireItems(configuration.ActionsRunners, "actionsRunners");
        RequireAny(configuration.Plans, "plan");
        RequireAny(configuration.Operations, "operation");

        RequireIdentifiers(configuration.Plans.Select(x => x.Id), "plan");
        RequireIdentifiers(configuration.Models.Select(x => x.Id), "model");
        RequireIdentifiers(configuration.Operations.Select(x => x.Id), "operation");
        RequireIdentifiers(configuration.Gates.Select(x => x.Id), "gate");
        RequireIdentifiers(configuration.Multipliers.Select(x => x.Id), "multiplier");
        RequireIdentifiers(configuration.ActionsRunners.Select(x => x.Id), "Actions runner");
        EnsureUnique(configuration.Plans.Select(x => x.Id), "plan");
        EnsureUnique(configuration.Models.Select(x => x.Id), "model");
        EnsureUnique(configuration.Operations.Select(x => x.Id), "operation");
        EnsureUnique(configuration.Gates.Select(x => x.Id), "gate");
        EnsureUnique(configuration.Multipliers.Select(x => x.Id), "multiplier");
        EnsureUnique(configuration.ActionsRunners.Select(x => x.Id), "Actions runner");

        foreach (var operation in configuration.Operations)
        {
            RequireDefined(operation.ActionsMetering, $"operations['{operation.Id}'].actionsMetering");
        }

        foreach (var plan in configuration.Plans)
        {
            Require(plan.AllowancePeriods, $"plans['{plan.Id}'].allowancePeriods");
            RequireItems(plan.AllowancePeriods, $"plans['{plan.Id}'].allowancePeriods");
            if (plan.AllowancePeriods.Count == 0)
            {
                throw new ConfigurationException($"Plan '{plan.Id}' must define at least one allowance period.");
            }

            var orderedPeriods = plan.AllowancePeriods.OrderBy(x => x.EffectiveFrom).ToArray();
            for (var index = 1; index < orderedPeriods.Length; index++)
            {
                if (orderedPeriods[index - 1].EffectiveTo is null ||
                    orderedPeriods[index].EffectiveFrom < orderedPeriods[index - 1].EffectiveTo)
                {
                    throw new ConfigurationException($"Plan '{plan.Id}' has overlapping allowance periods.");
                }
            }

            foreach (var period in plan.AllowancePeriods)
            {
                if (period.EffectiveTo <= period.EffectiveFrom)
                {
                    throw new ConfigurationException($"Plan '{plan.Id}' has an invalid allowance effective date range.");
                }

                if (period.IncludedCreditsPerUser < 0)
                {
                    throw new ConfigurationException($"Plan '{plan.Id}' has a negative included-credit allowance.");
                }
            }
        }

        var operationIds = configuration.Operations.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var modelIds = configuration.Models.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var planIds = configuration.Plans.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var runnerIds = configuration.ActionsRunners.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        ValidateReferenceMetadata(configuration, planIds, operationIds);

        if (string.IsNullOrWhiteSpace(configuration.ExampleScenario.ProductId) ||
            string.IsNullOrWhiteSpace(configuration.ExampleScenario.SkuId))
        {
            throw new ConfigurationException("Example scenario productId and skuId cannot be empty.");
        }

        EnsureOptionalReference(
            configuration.ExampleScenario.PreferredOperationId,
            operationIds,
            "example scenario operation");
        EnsureOptionalReference(
            configuration.ExampleScenario.PreferredPlanId,
            planIds,
            "example scenario plan");
        EnsureOptionalReference(
            configuration.ExampleScenario.PreferredModelId,
            modelIds,
            "example scenario model");
        EnsureOptionalReference(
            configuration.ExampleScenario.PreferredActionsRunnerId,
            runnerIds,
            "example scenario Actions runner");

        foreach (var gate in configuration.Gates)
        {
            Require(gate.ApplicableOperationIds, $"gates['{gate.Id}'].applicableOperationIds");
            EnsureReferences(gate.ApplicableOperationIds, operationIds, $"gate '{gate.Id}' operation");
        }

        foreach (var multiplier in configuration.Multipliers)
        {
            Require(
                multiplier.ApplicableOperationIds,
                $"multipliers['{multiplier.Id}'].applicableOperationIds");
            Require(
                multiplier.ApplicableModelIds,
                $"multipliers['{multiplier.Id}'].applicableModelIds");
            if (multiplier.Factor < 0)
            {
                throw new ConfigurationException($"Multiplier '{multiplier.Id}' cannot have a negative factor.");
            }

            EnsureReferences(multiplier.ApplicableOperationIds, operationIds, $"multiplier '{multiplier.Id}' operation");
            EnsureReferences(multiplier.ApplicableModelIds, modelIds, $"multiplier '{multiplier.Id}' model");
        }

        foreach (var runner in configuration.ActionsRunners.Where(x => x.UsdPerMinute < 0))
        {
            throw new ConfigurationException($"Actions runner '{runner.Id}' cannot have a negative price.");
        }

        foreach (var model in configuration.Models)
        {
            Require(model.PricePeriods, $"models['{model.Id}'].pricePeriods");
            RequireItems(model.PricePeriods, $"models['{model.Id}'].pricePeriods");
            if (model.PricePeriods.Count == 0)
            {
                throw new ConfigurationException($"Model '{model.Id}' must define at least one price period.");
            }

            var orderedPeriods = model.PricePeriods.OrderBy(x => x.EffectiveFrom).ToArray();
            for (var index = 1; index < orderedPeriods.Length; index++)
            {
                if (orderedPeriods[index - 1].EffectiveTo is null ||
                    orderedPeriods[index].EffectiveFrom < orderedPeriods[index - 1].EffectiveTo)
                {
                    throw new ConfigurationException($"Model '{model.Id}' has overlapping price periods.");
                }
            }

            foreach (var period in model.PricePeriods)
            {
                Require(period.Tiers, $"models['{model.Id}'].pricePeriods.tiers");
                RequireItems(period.Tiers, $"models['{model.Id}'].pricePeriods.tiers");
                if (period.EffectiveTo <= period.EffectiveFrom)
                {
                    throw new ConfigurationException($"Model '{model.Id}' has an invalid effective date range.");
                }

                if (period.Tiers.Count == 0)
                {
                    throw new ConfigurationException($"Model '{model.Id}' has a price period without tiers.");
                }

                RequireIdentifiers(period.Tiers.Select(x => x.Id), $"tier for model '{model.Id}'");
                EnsureUnique(period.Tiers.Select(x => x.Id), $"tier for model '{model.Id}'");
                var orderedTiers = period.Tiers
                    .OrderBy(x => x.MinimumContextTokensExclusive ?? long.MinValue)
                    .ToArray();
                for (var index = 1; index < orderedTiers.Length; index++)
                {
                    var previousMaximum = orderedTiers[index - 1].MaximumContextTokensInclusive;
                    var currentMinimum = orderedTiers[index].MinimumContextTokensExclusive;
                    if (previousMaximum is null || currentMinimum is null || currentMinimum < previousMaximum)
                    {
                        throw new ConfigurationException(
                            $"Model '{model.Id}' has overlapping context tiers '{orderedTiers[index - 1].Id}' and '{orderedTiers[index].Id}'.");
                    }
                }

                foreach (var tier in period.Tiers)
                {
                    if (tier.MinimumContextTokensExclusive < 0 || tier.MaximumContextTokensInclusive < 0)
                    {
                        throw new ConfigurationException($"Model '{model.Id}' tier '{tier.Id}' has a negative context boundary.");
                    }

                    if (tier.MinimumContextTokensExclusive >= tier.MaximumContextTokensInclusive)
                    {
                        throw new ConfigurationException($"Model '{model.Id}' tier '{tier.Id}' has an invalid context range.");
                    }

                    if (tier.InputUsdPerMillion < 0 || tier.CachedInputUsdPerMillion < 0 ||
                        tier.CacheWriteUsdPerMillion < 0 || tier.OutputUsdPerMillion < 0)
                    {
                        throw new ConfigurationException($"Model '{model.Id}' tier '{tier.Id}' has a negative token price.");
                    }
                }
            }
        }
    }

    private static T Require<T>(T? value, string path) where T : class =>
        value ?? throw new ConfigurationException(
            $"Configuration property '{path}' cannot be null.",
            ConfigurationException.InvalidContractCode);

    private static void RequireItems<T>(IReadOnlyList<T> values, string path) where T : class
    {
        for (var index = 0; index < values.Count; index++)
        {
            Require(values[index], $"{path}[{index}]");
        }
    }

    private static void RequireAny<T>(IReadOnlyList<T> values, string label)
    {
        if (values.Count == 0)
        {
            throw new ConfigurationException($"Configuration must define at least one {label}.");
        }
    }

    private static void RequireIdentifiers(IEnumerable<string> values, string label)
    {
        if (values.Any(string.IsNullOrWhiteSpace))
        {
            throw new ConfigurationException($"{label} id cannot be empty.");
        }
    }

    private static void EnsureUnique(IEnumerable<string> values, string label)
    {
        var duplicate = values
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new ConfigurationException($"Duplicate {label} id '{duplicate.Key}'.");
        }
    }

    private static void EnsureReferences(
        IEnumerable<string> references,
        IReadOnlySet<string> knownValues,
        string label)
    {
        var unknown = references.FirstOrDefault(reference => !knownValues.Contains(reference));
        if (unknown is not null)
        {
            throw new ConfigurationException($"Unknown {label} id '{unknown}'.");
        }
    }

    private static void EnsureOptionalReference(
        string? reference,
        IReadOnlySet<string> knownValues,
        string label)
    {
        if (!string.IsNullOrWhiteSpace(reference) && !knownValues.Contains(reference))
        {
            throw new ConfigurationException($"Unknown {label} id '{reference}'.");
        }
    }

    private static void RequireDefined<TEnum>(TEnum value, string path)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ConfigurationException($"Configuration property '{path}' has an unsupported value.");
        }
    }
}

public sealed class ConfigurationException(
    string message,
    string code = ConfigurationException.InvalidConfigurationCode) : Exception(message)
{
    public const string InvalidConfigurationCode = "configuration-invalid";
    public const string InvalidContractCode = "configuration-contract-invalid";

    public string Code { get; } = code;
}
