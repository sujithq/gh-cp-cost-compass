using System.Text.Json;

namespace CopilotUsageSimulator.BundleTool.Tests;

public sealed class ImportJsonTests
{
    [Fact]
    public void MissingFinancialValuesAreNotZero()
    {
        var overrides = ImportJson.Read<ImportOverrides>("""{"schemaVersion":1}""");

        Assert.Null(overrides.EnterprisePoolConsumedCredits);
        Assert.Null(overrides.EnterpriseBudgetExcludedCostCenterIds);
        Assert.Null(overrides.PaidUsage);
    }

    [Fact]
    public void ExplicitZeroAndEmptyConfirmationsSurviveRoundTrip()
    {
        var overrides = ImportJson.Read<ImportOverrides>("""
            {"schemaVersion":1,"enterprisePoolConsumedCredits":0,"enterpriseBudgetExcludedCostCenterIds":[]}
            """);

        var restored = ImportJson.Read<ImportOverrides>(ImportJson.Write(overrides));

        Assert.Equal(0m, restored.EnterprisePoolConsumedCredits);
        Assert.Empty(restored.EnterpriseBudgetExcludedCostCenterIds!);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"schemaVersion\":1,\"unrecognized\":true}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":2}")]
    [InlineData("{\"schemaVersion\":1,\"seats\":null}")]
    [InlineData("{\"schemaVersion\":1,\"paidUsage\":{\"state\":1}}")]
    public void MalformedDocumentsAreRejected(string json)
    {
        Assert.Throws<JsonException>(() => ImportJson.Read<ImportOverrides>(json));
    }
}