using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Guardrails;

namespace CopilotUsageSimulator.Engine.Tests;

public sealed class EffectiveDatedPlanAllowanceTests
{
    private static readonly DateTimeOffset Boundary =
        new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-1, 1_000)]
    [InlineData(0, 2_000)]
    [InlineData(1, 2_000)]
    public void PoolEntitlementUsesAllowanceEffectiveAtTimestamp(
        int offsetSeconds,
        decimal expectedCredits)
    {
        var calculator = CreateCalculator(
            new PlanAllowancePeriod
            {
                EffectiveFrom = Boundary.AddMonths(-1),
                EffectiveTo = Boundary,
                IncludedCreditsPerUser = 1_000m
            },
            new PlanAllowancePeriod
            {
                EffectiveFrom = Boundary,
                IncludedCreditsPerUser = 2_000m
            });

        var entitlement = calculator.CalculatePoolEntitlement(
            Billing(Seat("business")),
            Boundary.AddSeconds(offsetSeconds));

        Assert.True(entitlement.IsKnown);
        Assert.Equal(expectedCredits, entitlement.Credits);
    }

    [Fact]
    public void CostCenterEntitlementUsesHistoricalAllowance()
    {
        var calculator = CreateCalculator(
            new PlanAllowancePeriod
            {
                EffectiveFrom = Boundary.AddYears(-1),
                EffectiveTo = Boundary,
                IncludedCreditsPerUser = 750m
            },
            new PlanAllowancePeriod
            {
                EffectiveFrom = Boundary,
                IncludedCreditsPerUser = 2_000m
            });

        var entitlement = calculator.CalculateCostCenterEntitlement(
            Billing(Seat("business")),
            "cc-1",
            Boundary.AddMonths(-1));

        Assert.True(entitlement.IsKnown);
        Assert.Equal(750m, entitlement.Credits);
    }

    [Fact]
    public void MissingAllowancePeriodMakesPooledSeatInventoryUnknown()
    {
        var calculator = CreateCalculator(
            new PlanAllowancePeriod
            {
                EffectiveFrom = Boundary,
                IncludedCreditsPerUser = 2_000m
            });

        var entitlement = calculator.CalculatePoolEntitlement(
            Billing(Seat("business")),
            Boundary.AddDays(-1));

        Assert.False(entitlement.IsKnown);
        Assert.Equal("business", entitlement.UnknownPlanId);
    }

    [Fact]
    public void NonPooledPlanAllowanceDoesNotContributeToSharedEntitlement()
    {
        var calculator = CreateCalculator(
            new PlanAllowancePeriod
            {
                EffectiveFrom = DateTimeOffset.MinValue,
                IncludedCreditsPerUser = 2_000m
            });

        var entitlement = calculator.CalculatePoolEntitlement(
            Billing(Seat("pro")),
            Boundary);

        Assert.True(entitlement.IsKnown);
        Assert.Equal(0m, entitlement.Credits);
    }

    private static EconomicBalanceCalculator CreateCalculator(
        params PlanAllowancePeriod[] allowancePeriods) =>
        new(new EngineConfiguration
        {
            Plans =
            [
                new PlanDefinition
                {
                    Id = "business",
                    IsPooled = true,
                    AllowancePeriods = allowancePeriods
                },
                new PlanDefinition
                {
                    Id = "pro",
                    IsPooled = false,
                    AllowancePeriods = allowancePeriods
                }
            ]
        });

    private static BillingContext Billing(params EffectiveSeatAssignment[] seats) =>
        new()
        {
            BillingEntityId = "enterprise-1",
            CycleStart = Boundary.AddMonths(-1),
            CycleEnd = Boundary.AddMonths(1),
            SeatAssignments = seats
        };

    private static EffectiveSeatAssignment Seat(string planId) =>
        new()
        {
            UserId = "user-1",
            PlanId = planId,
            CostCenterId = "cc-1"
        };
}