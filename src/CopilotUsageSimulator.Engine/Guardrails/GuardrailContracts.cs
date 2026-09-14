using CopilotUsageSimulator.Common.Guardrails;

namespace CopilotUsageSimulator.Engine.Guardrails;

public enum GuardrailValue
{
    Unknown,
    Disabled,
    Enabled
}

public enum GuardrailEnforcement
{
    ObserveOnly,
    AlertOnly,
    SoftStop,
    HardStop
}

public enum GuardrailOutcome
{
    Passed,
    Blocked,
    SoftStopped,
    Waiting,
    Indeterminate,
    NotApplicable
}

public sealed record AppliedGuardrail
{
    public required string Id { get; init; }
    public string MetadataKey { get; init; } = GuardrailMetadataKeys.Unknown;
    public required string Category { get; init; }
    public GuardrailEnforcement Enforcement { get; init; }
    public GuardrailOutcome Outcome { get; init; }
    public decimal? Limit { get; init; }
    public decimal? ConsumedBefore { get; init; }
    public decimal? Requested { get; init; }
    public decimal? RemainingAfter { get; init; }
    public required string Message { get; init; }
}

public sealed record ThresholdEvent
{
    public required string GuardrailId { get; init; }
    public decimal ThresholdPercent { get; init; }
    public decimal BeforePercent { get; init; }
    public decimal AfterPercent { get; init; }
    public bool DeliveryDocumented { get; init; } = true;
}
