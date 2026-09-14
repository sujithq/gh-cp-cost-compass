using System.Globalization;
using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Engine.Tests;

public sealed class CompassPricingEvidenceTests
{
    [Theory]
    [InlineData(272_000, 4, "0.4", 5, 20)]
    [InlineData(272_001, 8, "0.8", 10, 30)]
    public void PreviewCarriesTheActualSelectedSolTierAndEffectiveRates(
        long context, decimal input, string cached, decimal write, decimal output)
    {
        var scenario = SolScenario(context);
        var configuration = EngineConfigurationLoader.LoadDefault();

        var preview = new SimulationPreviewService(configuration).Preview(scenario);

        Assert.Null(preview.ErrorCode);
        var charge = Assert.Single(preview.Result!.Calls);
        var pricing = Assert.IsType<ModelCallPricingEvidence>(charge.Pricing);
        Assert.Equal(charge.PriceTierId, pricing.PriceTierId);
        Assert.Equal(input, pricing.InputUsdPerMillion);
        Assert.Equal(decimal.Parse(cached, CultureInfo.InvariantCulture), pricing.CachedInputUsdPerMillion);
        Assert.Equal(write, pricing.CacheWriteUsdPerMillion);
        Assert.Equal(output, pricing.OutputUsdPerMillion);
        Assert.Equal(new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero), pricing.EffectiveFrom);
        Assert.Null(pricing.EffectiveTo);
        Assert.Equal(context == 272_000 ? 272_000L : null, pricing.MaximumContextTokensInclusive);
        Assert.Equal(context == 272_001 ? 272_000L : null, pricing.MinimumContextTokensExclusive);
        Assert.NotEmpty(pricing.SourceIds);
        Assert.All(pricing.SourceIds, id =>
            Assert.Contains(configuration.ReferenceSnapshot!.Sources, source => source.Id == id));
        Assert.Equal("paid-usage", preview.Result.FirstFailingGate);
        Assert.Equal(0m, preview.AcceptedTotalUsd);
    }

    [Fact]
    public void HistoricalDirectPricingCarriesItsSelectedPromotionalPeriod()
    {
        var scenario = SolScenario(272_000) with
        {
            Timestamp = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero)
        };

        var result = new CopilotUsageSimulationEngine(EngineConfigurationLoader.LoadDefault()).Simulate(scenario);

        var pricing = Assert.IsType<ModelCallPricingEvidence>(Assert.Single(result.Calls).Pricing);
        Assert.Equal(2m, pricing.InputUsdPerMillion);
        Assert.Equal(.20m, pricing.CachedInputUsdPerMillion);
        Assert.Equal(2.50m, pricing.CacheWriteUsdPerMillion);
        Assert.Equal(10m, pricing.OutputUsdPerMillion);
        Assert.Equal(new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero), pricing.EffectiveTo);
        Assert.Contains("S2", pricing.SourceIds);
    }

    [Fact]
    public void UnpublishedComponentIsNullAndAutoDoesNotRewriteTheBaseTariff()
    {
        var scenario = CompassFixture.Scenario(600_000);
        scenario = scenario with
        {
            Calls = [scenario.Calls[0] with { EnabledMultiplierIds = ["auto-model-selection"] }]
        };

        var preview = new SimulationPreviewService(EngineConfigurationLoader.LoadDefault()).Preview(scenario);

        Assert.Null(preview.ErrorCode);
        var charge = Assert.Single(preview.Result!.Calls);
        var pricing = Assert.IsType<ModelCallPricingEvidence>(charge.Pricing);
        Assert.Equal(.20m, pricing.InputUsdPerMillion);
        Assert.Equal(.02m, pricing.CachedInputUsdPerMillion);
        Assert.Null(pricing.CacheWriteUsdPerMillion);
        Assert.Equal(1.20m, pricing.OutputUsdPerMillion);
        Assert.Equal(.12m, charge.RawUsd);
        Assert.Equal(.108m, charge.AdjustedUsd);
        Assert.Equal(.90m, Assert.Single(charge.AppliedMultipliers).Factor);
    }

    [Fact]
    public void PriceEvidenceDoesNotShareMutableSourceCollectionsWithCatalogOrFuturePreviews()
    {
        var configuration = EngineConfigurationLoader.LoadDefault();
        var service = new SimulationPreviewService(configuration);
        var first = service.Preview(SolScenario(272_000));
        var originalIds = first.Result!.Calls[0].Pricing!.SourceIds.ToArray();

        ((string[])first.Result.Calls[0].Pricing!.SourceIds)[0] = "changed";
        var second = service.Preview(SolScenario(272_000));

        Assert.Equal(originalIds, second.Result!.Calls[0].Pricing!.SourceIds);
        Assert.DoesNotContain(configuration.Models.SelectMany(model => model.PricePeriods)
            .SelectMany(period => period.SourceIds ?? []), id => id == "changed");
    }

    [Fact]
    public void EarlierTerminalGateDoesNotInventPricedCallEvidence()
    {
        var scenario = SolScenario(272_000) with
        {
            AccessGates = new Dictionary<string, AccessGateState> { ["policy"] = new() { Passed = false } }
        };

        var preview = new SimulationPreviewService(EngineConfigurationLoader.LoadDefault()).Preview(scenario);

        Assert.Equal("policy", preview.Result!.FirstFailingGate);
        Assert.Empty(preview.Result.Calls);
        Assert.Null(preview.RequiredModelUsd);
        Assert.Contains(preview.Result.Trace, entry => entry.Stage == "pricing" &&
            entry.State == SimulationTraceState.NotEvaluated);
    }

    private static SimulationScenario SolScenario(long context)
    {
        var scenario = CompassFixture.Scenario();
        return scenario with
        {
            Calls =
            [
                new()
                {
                    ModelId = "gpt-5.6-sol",
                    ContextTokens = context,
                    FreshInputTokens = 50_000,
                    OutputTokens = 40_000
                }
            ]
        };
    }
}
