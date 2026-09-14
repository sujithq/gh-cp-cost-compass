using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Reference;

namespace CopilotUsageSimulator.Engine.Simulation;

public sealed class SimulationPreviewService(EngineConfiguration configuration)
{
    private readonly EngineConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

    /// <summary>Evaluates a copied scenario in the supported Compass profile without advancing any balances.</summary>
    public SimulationPreview Preview(SimulationScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var assumptions = CompassPreviewProfile.Assumptions(scenario);
        try
        {
            var engine = new CopilotUsageSimulationEngine(_configuration);
            assumptions = assumptions.Concat(_configuration.ReferenceSnapshot?.Assumptions ?? [])
                .Distinct(StringComparer.Ordinal).ToArray();
            SimulationScenarioValidator.Validate(scenario);
            var snapshot = SimulationScenarioCopy.Create(scenario);
            CompassPreviewProfile.Validate(_configuration, snapshot);
            var eligibility = new ModelEligibilityEvaluator(_configuration);
            foreach (var call in snapshot.Calls)
            {
                var evidence = eligibility.Evaluate(
                    call.ModelId, snapshot.PlanId, snapshot.OperationId, snapshot.Timestamp, call);
                assumptions = assumptions.Concat(evidence.Assumptions).Distinct(StringComparer.Ordinal).ToArray();
                if (evidence.Status != ModelEligibilityStatus.Available)
                {
                    return Diagnostic(snapshot, evidence.ReasonCode, evidence.Message, assumptions);
                }
            }

            var result = engine.Simulate(snapshot);
            return new SimulationPreview
            {
                Result = result,
                InitialRemaining = new EconomicBalanceCalculator(_configuration)
                    .CreateUnchangedRemaining(snapshot, result.Attribution),
                CatalogVersion = _configuration.Version,
                ReferenceSnapshotId = _configuration.ReferenceSnapshot?.Id,
                ReferenceVerifiedOn = _configuration.ReferenceSnapshot?.VerifiedOn,
                Timestamp = snapshot.Timestamp,
                Assumptions = assumptions.Concat(result.Assumptions).Distinct(StringComparer.Ordinal).ToArray()
            };
        }
        catch (SimulationException exception)
        {
            return Diagnostic(scenario, exception.Code, exception.Message, assumptions);
        }
        catch (ConfigurationException exception)
        {
            return Diagnostic(scenario, "configuration-invalid", exception.Message, assumptions);
        }
        catch (OverflowException)
        {
            return Diagnostic(scenario, "calculation-overflow",
                "The supplied values exceed the supported numeric range; no usage estimate was produced.", assumptions);
        }
    }

    /// <summary>Re-evaluates independent local alternatives; a candidate is resolved only when it is allowed.</summary>
    public IReadOnlyList<SimulationComparison> Compare(SimulationScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var baseline = Preview(scenario);
        if (baseline.ErrorCode is not null || baseline.Result is null)
        {
            return [];
        }

        var comparisons = new List<SimulationComparison>();
        var reduction = ReduceToIncluded(scenario, baseline);
        if (reduction is not null)
        {
            comparisons.Add(reduction);
        }

        if (baseline.Result.Decision != SimulationDecision.Allowed &&
            scenario.EconomicGuardrails is { PaidUsage.State: not GuardrailValue.Enabled })
        {
            var candidate = SimulationScenarioCopy.Create(scenario);
            candidate = candidate with
            {
                EconomicGuardrails = candidate.EconomicGuardrails! with
                {
                    PaidUsage = candidate.EconomicGuardrails.PaidUsage with { State = GuardrailValue.Enabled }
                }
            };
            comparisons.Add(new SimulationComparison
            {
                Id = "enable-paid-usage",
                Title = "Preview enabling paid usage",
                Description = "Only paid-usage authorization is enabled in this candidate. Product/SKU scope, ULBs, policy, and spending budgets are unchanged.",
                CandidateScenario = candidate,
                Baseline = baseline,
                Candidate = Preview(candidate)
            });
        }

        return comparisons.ToArray();
    }

    private SimulationComparison? ReduceToIncluded(SimulationScenario scenario, SimulationPreview baseline)
    {
        if (baseline.InitialRemaining is not { } remaining || baseline.RequiredAiCredits is not { } required)
        {
            return null;
        }

        var available = remaining.IncludedUsageControlCredits is { } control
            ? Math.Min(remaining.IncludedPoolCredits, control)
            : remaining.IncludedPoolCredits;
        if (available <= 0m || required <= available)
        {
            return null;
        }

        var ratio = available / required;
        var candidate = SimulationScenarioCopy.Create(scenario);
        candidate = candidate with
        {
            Calls = candidate.Calls.Select(call => call with
            {
                FreshInputTokens = Scale(call.FreshInputTokens, ratio),
                CachedInputTokens = Scale(call.CachedInputTokens, ratio),
                CacheWriteTokens = Scale(call.CacheWriteTokens, ratio),
                OutputTokens = Scale(call.OutputTokens, ratio)
            }).ToArray()
        };
        var preview = Preview(candidate);
        if (preview.ErrorCode is not null || preview.RequiredAiCredits is not { } reduced ||
            reduced <= 0m || reduced > available || reduced >= required)
        {
            return null;
        }

        return new SimulationComparison
        {
            Id = "reduce-to-included",
            Title = "Preview a smaller workload",
            Description = "Token components are reduced and rounded down to whole tokens to fit the positive included allowance. Model, context tier, call count, Actions minutes, and every guardrail remain unchanged.",
            CandidateScenario = candidate,
            Baseline = baseline,
            Candidate = preview
        };
    }

    private static long Scale(long value, decimal ratio) => (long)decimal.Floor(value * ratio);

    private SimulationPreview Diagnostic(
        SimulationScenario scenario, string code, string message, IReadOnlyList<string> assumptions) =>
        new()
        {
            ErrorCode = code,
            ErrorMessage = message,
            CatalogVersion = _configuration.Version,
            ReferenceSnapshotId = _configuration.ReferenceSnapshot?.Id,
            ReferenceVerifiedOn = _configuration.ReferenceSnapshot?.VerifiedOn,
            Timestamp = scenario.Timestamp,
            Assumptions = assumptions
        };
}
