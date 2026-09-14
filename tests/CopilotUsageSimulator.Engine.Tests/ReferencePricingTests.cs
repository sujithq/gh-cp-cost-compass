using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Reference;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Engine.Tests;

public sealed class ReferencePricingTests
{
    private static readonly DateTimeOffset SolBoundary = new(2026, 9, 4, 0, 0, 0, TimeSpan.Zero);
    private readonly EngineConfiguration _configuration = EngineConfigurationLoader.LoadDefault();

    [Theory]
    [InlineData(272_000, "promotional-default", "standard-default")]
    [InlineData(272_001, "promotional-long-context", "standard-long-context")]
    public void SolSuccessorCostsExactlyTwiceThePromotionAtExclusiveBoundary(
        long contextTokens, string promotionalTier, string successorTier)
    {
        var engine = new CopilotUsageSimulationEngine(_configuration);
        var call = Call("gpt-5.6-sol", contextTokens);
        var before = engine.Simulate(Scenario(call, SolBoundary.AddTicks(-1)));
        var at = engine.Simulate(Scenario(call, SolBoundary));
        var after = engine.Simulate(Scenario(call, SolBoundary.AddTicks(1)));
        var promotional = Assert.Single(before.Calls);
        var successor = Assert.Single(at.Calls);

        Assert.Equal(SimulationDecision.Allowed, before.Decision);
        Assert.Equal(SimulationDecision.Allowed, at.Decision);
        Assert.Equal(promotionalTier, promotional.PriceTierId);
        Assert.Equal(successorTier, successor.PriceTierId);
        Assert.Equal(promotional.RawUsd * 2m, successor.RawUsd);
        Assert.Equal(promotional.FreshInputUsd * 2m, successor.FreshInputUsd);
        Assert.Equal(promotional.CachedInputUsd * 2m, successor.CachedInputUsd);
        Assert.Equal(promotional.CacheWriteUsd * 2m, successor.CacheWriteUsd);
        Assert.Equal(promotional.OutputUsd * 2m, successor.OutputUsd);
        Assert.Equal(successor.RawUsd, Assert.Single(after.Calls).RawUsd);
    }

    [Fact]
    public void SolKeepsItsOriginalHistoricalPeriodAndTierIds()
    {
        var model = _configuration.Models.Single(x => x.Id == "gpt-5.6-sol");
        var promotion = model.PricePeriods[0];
        var successor = model.PricePeriods[1];

        Assert.Equal(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), promotion.EffectiveFrom);
        Assert.Equal(SolBoundary, promotion.EffectiveTo);
        Assert.Equal(promotion.EffectiveTo, successor.EffectiveFrom);
        Assert.Null(successor.EffectiveTo);
        Assert.Equal(2m, promotion.Tiers[0].InputUsdPerMillion);
        Assert.Equal(10m, promotion.Tiers[0].OutputUsdPerMillion);
        Assert.Equal(4m, successor.Tiers[0].InputUsdPerMillion);
        Assert.Equal(0.4m, successor.Tiers[0].CachedInputUsdPerMillion);
        Assert.Equal(5m, successor.Tiers[0].CacheWriteUsdPerMillion);
        Assert.Equal(20m, successor.Tiers[0].OutputUsdPerMillion);
        Assert.Equal(8m, successor.Tiers[1].InputUsdPerMillion);
        Assert.Equal(0.8m, successor.Tiers[1].CachedInputUsdPerMillion);
        Assert.Equal(10m, successor.Tiers[1].CacheWriteUsdPerMillion);
        Assert.Equal(30m, successor.Tiers[1].OutputUsdPerMillion);
    }

    [Theory]
    [InlineData(272_000, "default", 10, 1, 12.5, 50)]
    [InlineData(272_001, "long-context", 20, 2, 25, 75)]
    public void AstraSelectsExactSourcedContextTier(
        long contextTokens, string tierId, decimal input, decimal cached, decimal cacheWrite, decimal output)
    {
        var evaluator = new ModelEligibilityEvaluator(_configuration);
        var call = Call("gpt-6-astra", contextTokens);
        var before = evaluator.EvaluatePricing(call.ModelId, SolBoundary.AddTicks(-1), call);
        var result = evaluator.EvaluatePricing(call.ModelId, SolBoundary, call);
        var tier = Assert.IsType<TokenPriceTier>(result.PriceTier);

        Assert.Equal(ModelEligibilityReasonCodes.PricingNotEffective, before.ReasonCode);
        Assert.Equal(tierId, tier.Id);
        Assert.Equal(input, tier.InputUsdPerMillion);
        Assert.Equal(cached, tier.CachedInputUsdPerMillion);
        Assert.Equal(cacheWrite, tier.CacheWriteUsdPerMillion);
        Assert.Equal(output, tier.OutputUsdPerMillion);

        var simulation = new CopilotUsageSimulationEngine(_configuration).Simulate(Scenario(call, SolBoundary));
        Assert.Equal(SimulationDecision.Allowed, simulation.Decision);
        Assert.Equal((input + cached + cacheWrite + output) / 1_000m, simulation.Calls.Single().RawUsd);
    }

    [Fact]
    public void Gemini38HasOnlyItsPublishedPromotionAndSupportedComponents()
    {
        var model = _configuration.Models.Single(x => x.Id == "gemini-3.8-flash");
        var period = Assert.Single(model.PricePeriods);
        var tier = Assert.Single(period.Tiers);

        Assert.Equal(new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero), period.EffectiveFrom);
        Assert.Equal(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), period.EffectiveTo);
        Assert.Equal(0.75m, tier.InputUsdPerMillion);
        Assert.Equal(0.075m, tier.CachedInputUsdPerMillion);
        Assert.Equal(3.75m, tier.OutputUsdPerMillion);
        Assert.DoesNotContain(TokenComponent.CacheWrite, model.SupportedTokenComponents!);
    }

    [Fact]
    public void MaiReplacementReusesTheExistingHistoricalRate()
    {
        var model = _configuration.Models.Single(x => x.Id == "mai-code-1.1-flash");
        var period = Assert.Single(model.PricePeriods);
        var tier = Assert.Single(period.Tiers);

        Assert.Equal(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), period.EffectiveFrom);
        Assert.Equal(0.2m, tier.InputUsdPerMillion);
        Assert.Equal(0.02m, tier.CachedInputUsdPerMillion);
        Assert.Equal(1.2m, tier.OutputUsdPerMillion);
        Assert.DoesNotContain(TokenComponent.CacheWrite, model.SupportedTokenComponents!);
        Assert.Contains(_configuration.Models, x => x.Id == "mai-code-1-flash");
    }

    [Theory]
    [InlineData("business", 1_900)]
    [InlineData("enterprise", 3_900)]
    public void HistoricalStandardCohortNeverAutomaticallyReceivesThePromotion(string planId, decimal standard)
    {
        var balances = new EconomicBalanceCalculator(_configuration);
        foreach (var timestamp in new[]
        {
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 31, 23, 59, 59, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)
        })
        {
            var entitlement = balances.CalculatePoolEntitlement(Billing(planId, timestamp), timestamp);
            Assert.True(entitlement.IsKnown);
            Assert.Equal(standard, entitlement.Credits);
        }
    }

    [Theory]
    [InlineData("business", 3_000, 1_900)]
    [InlineData("enterprise", 7_000, 3_900)]
    public void QualifiedHistoricalPromotionRequiresAnExplicitSeparateSnapshot(
        string planId, decimal promotional, decimal standard)
    {
        var end = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var qualified = _configuration with
        {
            Version = "qualified-existing-customer-test",
            ReferenceSnapshot = _configuration.ReferenceSnapshot! with
            {
                Id = "qualified-existing-customer-test",
                CatalogVersion = "qualified-existing-customer-test",
                Assumptions =
                [
                    .. _configuration.ReferenceSnapshot!.Assumptions,
                    "This separate test snapshot explicitly assumes an existing customer qualified for the June-August promotion."
                ]
            },
            Plans = _configuration.Plans.Select(plan => plan.Id != planId ? plan : plan with
            {
                AllowancePeriods =
                [
                    new PlanAllowancePeriod
                    {
                        EffectiveFrom = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
                        EffectiveTo = end,
                        IncludedCreditsPerUser = promotional
                    },
                    new PlanAllowancePeriod { EffectiveFrom = end, IncludedCreditsPerUser = standard }
                ]
            }).ToArray()
        };
        EngineConfigurationValidator.Validate(qualified);
        var qualifiedBalances = new EconomicBalanceCalculator(qualified);
        var standardBalances = new EconomicBalanceCalculator(_configuration);
        var august = end.AddTicks(-1);

        Assert.Equal(promotional, qualifiedBalances.CalculatePoolEntitlement(Billing(planId, august), august).Credits);
        Assert.Equal(standard, standardBalances.CalculatePoolEntitlement(Billing(planId, august), august).Credits);
        Assert.Equal(standard, qualifiedBalances.CalculatePoolEntitlement(Billing(planId, end), end).Credits);
    }

    private static ModelCallInput Call(string modelId, long contextTokens) =>
        new()
        {
            ModelId = modelId,
            ContextTokens = contextTokens,
            FreshInputTokens = 1_000,
            CachedInputTokens = 1_000,
            CacheWriteTokens = 1_000,
            OutputTokens = 1_000
        };

    private static SimulationScenario Scenario(ModelCallInput call, DateTimeOffset timestamp) =>
        new()
        {
            OperationId = "chat",
            PlanId = "business",
            Timestamp = timestamp,
            Calls = [call],
            BillingContext = Billing("business", timestamp),
            Attribution = new AttributionInput
            {
                UserId = "user-1",
                LicensingOrganizationIds = ["org-1"],
                DirectAssignments = [new EffectiveCostCenterAssignment { CostCenterId = "cc-1" }]
            },
            EconomicGuardrails = new EconomicGuardrailSnapshot()
        };

    private static BillingContext Billing(string planId, DateTimeOffset timestamp)
    {
        var cycleStart = new DateTimeOffset(timestamp.Year, timestamp.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return new BillingContext
        {
            BillingEntityId = "enterprise-1",
            CycleStart = cycleStart,
            CycleEnd = cycleStart.AddMonths(1),
            SeatAssignments = [new EffectiveSeatAssignment { UserId = "user-1", PlanId = planId, CostCenterId = "cc-1" }]
        };
    }
}
