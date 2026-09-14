using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CopilotUsageSimulator.Engine.Configuration;

namespace CopilotUsageSimulator.Engine.Tests;

public sealed class EngineConfigurationValidationTests
{
    [Theory]
    [InlineData("42")]
    [InlineData("\"unsupported\"")]
    public void LoaderRejectsUnsupportedEnumValues(string value)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
        var json = JsonSerializer.Serialize(EngineConfigurationLoader.LoadDefault(), options)
            .Replace(
                "\"poolOverflowBehavior\":\"split\"",
                $"\"poolOverflowBehavior\":{value}",
                StringComparison.Ordinal);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        Assert.Throws<JsonException>(() => EngineConfigurationLoader.Load(stream));
    }

    [Fact]
    public void ValidatorRejectsUndefinedPoolOverflowBehavior()
    {
        var configuration = EngineConfigurationLoader.LoadDefault() with
        {
            PoolOverflowBehavior = (PoolOverflowBehavior)42
        };

        var exception = Assert.Throws<ConfigurationException>(
            () => EngineConfigurationValidator.Validate(configuration));

        Assert.Contains("poolOverflowBehavior", exception.Message);
    }

    [Fact]
    public void ValidatorRejectsUndefinedActionsMeteringMode()
    {
        var configuration = EngineConfigurationLoader.LoadDefault();
        configuration = configuration with
        {
            Operations =
            [
                configuration.Operations[0] with
                {
                    ActionsMetering = (ActionsMeteringMode)42
                },
                .. configuration.Operations.Skip(1)
            ]
        };

        var exception = Assert.Throws<ConfigurationException>(
            () => EngineConfigurationValidator.Validate(configuration));

        Assert.Contains("actionsMetering", exception.Message);
    }

    [Theory]
    [InlineData("plans", "plan")]
    [InlineData("operations", "operation")]
    public void ValidatorRejectsEmptyRequiredCatalogs(string collection, string expectedLabel)
    {
        var configuration = WithEmptyRequiredCollection(collection);

        var exception = Assert.Throws<ConfigurationException>(
            () => EngineConfigurationValidator.Validate(configuration));

        Assert.Contains($"at least one {expectedLabel}", exception.Message);
    }

    [Theory]
    [InlineData("plans", "plan")]
    [InlineData("operations", "operation")]
    public void LoaderRejectsEmptyRequiredCatalogs(string collection, string expectedLabel)
    {
        var json = JsonSerializer.Serialize(
            WithEmptyRequiredCollection(collection),
            SerializerOptions());
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var exception = Assert.Throws<ConfigurationException>(
            () => EngineConfigurationLoader.Load(stream));

        Assert.Contains($"at least one {expectedLabel}", exception.Message);
    }

    [Fact]
    public void ValidatorAllowsEmptyOptionalCatalogsWithoutReferences()
    {
        var configuration = EngineConfigurationLoader.LoadDefault() with
        {
            Models = [],
            Gates = [],
            Multipliers = [],
            ActionsRunners = [],
            ExampleScenario = new ExampleScenarioDefinition()
        };

        EngineConfigurationValidator.Validate(configuration);
    }

    [Fact]
    public void ValidatorRejectsPlanWithoutAllowancePeriods()
    {
        var configuration = WithPlanAllowancePeriods([]);

        var exception = Assert.Throws<ConfigurationException>(
            () => EngineConfigurationValidator.Validate(configuration));

        Assert.Contains("at least one allowance period", exception.Message);
    }

    [Fact]
    public void ValidatorRejectsOverlappingPlanAllowancePeriods()
    {
        var boundary = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var configuration = WithPlanAllowancePeriods(
        [
            new PlanAllowancePeriod
            {
                EffectiveFrom = boundary.AddMonths(-1),
                EffectiveTo = boundary.AddDays(1),
                IncludedCreditsPerUser = 1_000m
            },
            new PlanAllowancePeriod
            {
                EffectiveFrom = boundary,
                IncludedCreditsPerUser = 2_000m
            }
        ]);

        var exception = Assert.Throws<ConfigurationException>(
            () => EngineConfigurationValidator.Validate(configuration));

        Assert.Contains("overlapping allowance periods", exception.Message);
    }

    [Fact]
    public void ValidatorRejectsInvalidPlanAllowanceDateRange()
    {
        var boundary = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var configuration = WithPlanAllowancePeriods(
        [
            new PlanAllowancePeriod
            {
                EffectiveFrom = boundary,
                EffectiveTo = boundary,
                IncludedCreditsPerUser = 1_000m
            }
        ]);

        var exception = Assert.Throws<ConfigurationException>(
            () => EngineConfigurationValidator.Validate(configuration));

        Assert.Contains("invalid allowance effective date range", exception.Message);
    }

    [Fact]
    public void ValidatorRejectsNegativePlanAllowance()
    {
        var configuration = WithPlanAllowancePeriods(
        [
            new PlanAllowancePeriod
            {
                EffectiveFrom = DateTimeOffset.MinValue,
                IncludedCreditsPerUser = -1m
            }
        ]);

        var exception = Assert.Throws<ConfigurationException>(
            () => EngineConfigurationValidator.Validate(configuration));

        Assert.Contains("negative included-credit allowance", exception.Message);
    }

    [Theory]
    [InlineData("plan", "plan")]
    [InlineData("model", "model")]
    [InlineData("operation", "operation")]
    [InlineData("gate", "gate")]
    [InlineData("multiplier", "multiplier")]
    [InlineData("actionsRunner", "Actions runner")]
    [InlineData("tier", "tier")]
    public void ValidatorRejectsBlankStableIdentifiers(string entity, string expectedLabel)
    {
        var exception = Assert.Throws<ConfigurationException>(
            () => EngineConfigurationValidator.Validate(WithBlankIdentifier(entity)));

        Assert.Equal(ConfigurationException.InvalidConfigurationCode, exception.Code);
        Assert.Contains(expectedLabel, exception.Message);
    }

    [Theory]
    [InlineData("plan", "plan")]
    [InlineData("model", "model")]
    [InlineData("operation", "operation")]
    [InlineData("gate", "gate")]
    [InlineData("multiplier", "multiplier")]
    [InlineData("actionsRunner", "Actions runner")]
    [InlineData("tier", "tier")]
    public void LoaderRejectsBlankStableIdentifiers(string entity, string expectedLabel)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
        var json = JsonSerializer.Serialize(WithBlankIdentifier(entity), options);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var exception = Assert.Throws<ConfigurationException>(
            () => EngineConfigurationLoader.Load(stream));

        Assert.Equal(ConfigurationException.InvalidConfigurationCode, exception.Code);
        Assert.Contains(expectedLabel, exception.Message);
    }

    private static EngineConfiguration WithBlankIdentifier(string entity)
    {
        var configuration = EngineConfigurationLoader.LoadDefault();
        return entity switch
        {
            "plan" => configuration with
            {
                Plans = [configuration.Plans[0] with { Id = " " }, .. configuration.Plans.Skip(1)]
            },
            "model" => configuration with
            {
                Models = [configuration.Models[0] with { Id = " " }, .. configuration.Models.Skip(1)]
            },
            "operation" => configuration with
            {
                Operations = [configuration.Operations[0] with { Id = " " }, .. configuration.Operations.Skip(1)]
            },
            "gate" => configuration with
            {
                Gates = [configuration.Gates[0] with { Id = " " }, .. configuration.Gates.Skip(1)]
            },
            "multiplier" => configuration with
            {
                Multipliers = [configuration.Multipliers[0] with { Id = " " }, .. configuration.Multipliers.Skip(1)]
            },
            "actionsRunner" => configuration with
            {
                ActionsRunners =
                [
                    configuration.ActionsRunners[0] with { Id = " " },
                    .. configuration.ActionsRunners.Skip(1)
                ]
            },
            "tier" => configuration with
            {
                Models =
                [
                    configuration.Models[0] with
                    {
                        PricePeriods =
                        [
                            configuration.Models[0].PricePeriods[0] with
                            {
                                Tiers =
                                [
                                    configuration.Models[0].PricePeriods[0].Tiers[0] with { Id = " " },
                                    .. configuration.Models[0].PricePeriods[0].Tiers.Skip(1)
                                ]
                            },
                            .. configuration.Models[0].PricePeriods.Skip(1)
                        ]
                    },
                    .. configuration.Models.Skip(1)
                ]
            },
            _ => throw new ArgumentOutOfRangeException(nameof(entity), entity, null)
        };
    }

    private static EngineConfiguration WithEmptyRequiredCollection(string collection)
    {
        var configuration = EngineConfigurationLoader.LoadDefault();
        return collection switch
        {
            "plans" => configuration with
            {
                Plans = [],
                ExampleScenario = configuration.ExampleScenario with
                {
                    PreferredPlanId = null
                }
            },
            "operations" => configuration with
            {
                Operations = [],
                ExampleScenario = configuration.ExampleScenario with
                {
                    PreferredOperationId = null
                }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(collection), collection, null)
        };
    }

    private static JsonSerializerOptions SerializerOptions() =>
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

    private static EngineConfiguration WithPlanAllowancePeriods(
        IReadOnlyList<PlanAllowancePeriod> allowancePeriods)
    {
        var configuration = EngineConfigurationLoader.LoadDefault();
        return configuration with
        {
            Plans =
            [
                configuration.Plans[0] with { AllowancePeriods = allowancePeriods },
                .. configuration.Plans.Skip(1)
            ]
        };
    }
}