# GitHub Copilot Cost Compass

**Configure. Simulate. Explain. Resolve.** A responsive, offline Blazor WebAssembly experience for exploring GitHub Copilot usage costs and the first modeled blocker, backed by the existing deterministic .NET 11 engine.

This new version is implemented in the independent `GitHub Copilot Cost Compass` clone, based on `sujithq/hackathon2026` at `413f6f5359719c32ad00301861c3fdae12da3ab8`. The original implementation analysis and repository history are preserved. The [refreshed Cost Compass analysis and build plan](docs/COST-COMPASS-ANALYSIS.md) is the dated pre-implementation snapshot, verified against 23 official sources on 11 September 2026; it records the supported-case decisions and acceptance criteria.

The [updated AI-credit financial flow](docs/decision-flow.md) explains request-sized headroom, provisional allocation, budget enforcement, and Compass's supported boundaries. Its [unchanged upstream Markdown](docs/reference/decision-flow.upstream-2026-09-12.md) is preserved separately with pinned revision provenance in the updated document.

The reusable engine still follows [Copilot-Token-Usage-Simulator-Flows.md](Copilot-Token-Usage-Simulator-Flows.md). It has no browser, UI, persistence, or network dependency and can also serve a CLI, API, desktop application, or test harness.

## Run Cost Compass

Install the SDK pinned in `global.json`, **11.0.100-preview.7.26381.103**, into the ignored `.dotnet` directory, or use an existing installation of that exact SDK. No Node.js packages, CDN, credentials, or backend service are required.

```powershell
.\.dotnet\dotnet.exe run --project src\CopilotUsageSimulator.Web --configuration Release --no-launch-profile --urls http://localhost:5086
```

Open `http://localhost:5086/` (also available at `/compass`). `/advanced` retains the original full simulator, including its explicit repeat-and-advance workflow. Navigation links to `/advanced` and `/guide` are disabled because these pages use the legacy layout; direct URLs remain available. Append `?scoutTheme=light` or `?scoutTheme=dark` to choose a theme; otherwise the app follows the OS preference.

### Dev Container

Install Docker with Linux containers and the VS Code **Dev Containers** extension (`ms-vscode-remote.remote-containers`). Open this repository and run **Dev Containers: Reopen in Container**. The [container configuration](.devcontainer/devcontainer.json) installs the exact SDK pinned in [global.json](global.json), Node.js 24 for optional Mermaid asset maintenance, and C# Dev Kit. Initial setup restores the solution; it does not run the app automatically.

In the container terminal:

```bash
dotnet run --project src/CopilotUsageSimulator.Web --configuration Release --no-launch-profile
```

Open the **Cost Compass** entry on VS Code's Ports tab (container port `5086`). The app listens on `0.0.0.0` inside the container so forwarding works; if local port `5086` is occupied, use the local address shown in Ports.

```bash
dotnet test CopilotUsageSimulator.slnx --configuration Release
dotnet build CopilotUsageSimulator.slnx --configuration Release --no-restore
```

Use `dotnet`, not the host's Windows `.dotnet/dotnet.exe`. An empty container volume masks the host-local SDK so SDK resolution uses the container installation. Container builds use `artifacts/devcontainer` for both binaries and intermediate files; host `bin`/`obj` outputs are not reused. A separate writable volume isolates `node_modules`. Host SDKs, caches and source files are not deleted.

First creation requires access to Microsoft/GitHub container registries, SDK downloads and NuGet. Setup points the container user's NuGet source at `https://packagefeedproxy.microsoft.io/nuget/v3/index.json`, which works on the repository's corporate network. To use a different feed, set `COMPASS_NUGET_SOURCE` in the container configuration's `containerEnv` and rebuild. Credentials and host package-source settings are not copied into the image, and host NuGet settings are never modified.

No npm install is needed to run the app; the unused pnpm installer is disabled. For optional registry-accessing npm commands, use `--registry=https://packagefeedproxy.microsoft.io/npm/` as required by this repository. After changing the SDK pin or container features, run **Dev Containers: Rebuild Container**; setup rejects an SDK mismatch rather than rolling forward silently.

## Supported preview scope

| Area | Supported in Compass | Explicit boundary |
|---|---|---|
| Billing | Business and Enterprise pooled effective seats; standard September allowance | Not personal subscriptions, legacy annual plans, or inferred historical promotional cohorts |
| Workloads | Supplied Chat, CLI, and cloud-agent token calls | The task description is not a token predictor; extra calls remain explicit |
| Cloud-agent Actions | Private repository, standard Linux 2-core runner, USD 0.006 per accounted minute after supplied included minutes | Enter per-job-rounded, pre-accounted minutes, not raw durations. The account allowance is separate from the Copilot plan |
| Controls | Supplied access and runtime assumptions, attribution, ULBs, included usage, paid usage, AI budgets, Actions budgets | A simulator trace is not a claim about GitHub's internal processing order or discovered live settings |
| Model adjustments | Qualified Auto model-cost discount | Residency/FedRAMP eligibility and combined modifier stacking are not asserted as universal rules |
| Code review | Official-source explanation of uncertainty | No invented actual review model, guaranteed price, unlicensed-seat denial, or limited-review fallback calculation |
| Future announcements | Visible scope/evidence boundaries | September 28 unified/Sandbox changes and October cohort/payment behavior are not default live rules |

Estimates are **incremental usage**, not complete invoices: seat subscriptions, taxes, currency conversion, infrastructure, and invoice reconciliation are excluded. Standalone Actions, public/self-hosted/larger-runner accounting, and complete code-review billing are deferred.

Full-scope cloud-agent previews require an explicit Actions guardrail snapshot. Cost-only scope can omit it, but clearly excludes access/approval verification. Unchanged imported scenarios are not silently completed with new seats, model calls, direct cost-center assignments, or passing permissions from editor defaults.

### The two-minute demo

The default Chat fixture is pinned to 11 September 2026: **100 AI credits required, 60 pooled credits remaining, paid usage disabled**. It is genuinely blocked despite a nonzero allowance. The required credits are shown separately from accepted consumption.

1. **Simulate** repeatedly: the same inputs produce the same decision; balances do not advance.
2. **Preview alternatives**: each proposed change is re-run through the engine. A smaller workload can fit the included allowance; enabling paid usage can expose a later spending budget instead of falsely promising success.
3. **Use this scenario** applies only a local candidate. Simulate again to confirm it. Raising an AI spending budget cannot override an earlier ULB, access, or model restriction.
4. Select **Cloud agent** to see a distinct Actions meter, or **Copilot CLI** for a local workflow without runner charges.

Input changes invalidate the previous verdict. Invalid numeric edits and failed imports cannot leave a successful verdict looking current. All six engine outcomes remain distinct; an unsupported or unpriced request is a non-estimate, not zero cost.

The current first blocker's setting is highlighted and its section expanded. **Review blocking setting** scrolls to and focuses it. Snapshot-only controls show the exact blocking record with a bundle-editor action instead of highlighting an unrelated field; edits clear stale highlights. These actions do not change settings or advance balances.

### Save and reproduce the evidence

**Save scenario** stores one Compass bundle in browser storage, separately from the Advanced simulator's save slot. Save and export capture current valid form edits even before simulation. A versioned bundle contains the complete scenario, matching catalog and reference metadata, engine contract identity, and a SHA-256 catalog fingerprint. The fingerprint detects mismatches; it is not a digital signature or proof that custom prices are official.

Load/import validates the schema, version, scenario, catalog, and fingerprint before replacing the working state. Malformed input preserves the previous scenario and catalog while clearing the old verdict. Legacy scenario-only JSON remains importable with an explicit warning that the active catalog is being used.

Sources have a verification date separate from the simulation date. Calendar-date model announcements use explicit midnight-UTC simulation boundaries, not an inferred exact live billing cutover. Sol's old promotional period is preserved alongside its sourced standard successor; no undocumented post-promotion Gemini price or cache-write rate is invented.

## Projects

| Project | Purpose |
|---|---|
| `src/CopilotUsageSimulator.Common` | Shared guardrail metadata, stable identifiers, cost classification, and GitHub documentation links |
| `src/CopilotUsageSimulator.Engine` | Pure simulation domain and JSON configuration loader |
| `src/CopilotUsageSimulator.Bundles` | Shared Compass v1 transport, catalog fingerprint and legacy scenario serialization |
| `src/CopilotUsageSimulator.BundleTool` | Installable `compass-bundle` tool for read-only enterprise snapshots and offline bundle creation |
| `src/CopilotUsageSimulator.Web` | Cost Compass at `/`, preserved full simulator at `/advanced`, and static GitHub Pages hosting |
| `tests/CopilotUsageSimulator.Common.Tests` | Shared metadata contract and documentation-link tests |
| `tests/CopilotUsageSimulator.Engine.Tests` | Contract and calculation tests |
| `tests/CopilotUsageSimulator.BundleTool.Tests` | CLI, snapshot reconciliation, HTTP fixtures and output-safety regressions |
| `tests/CopilotUsageSimulator.Web.Tests` | bUnit component, guided-workflow, and serialization tests |

## Create a Bundle With the .NET Tool

The independent `compass-bundle` tool supports `collect`, `create`, and `validate`. It shares the app's existing v1 bundle codec and deterministic Engine without depending on Web.

Try the checked-in offline example with the pinned SDK:

```powershell
.\.dotnet\dotnet.exe run --project src/CopilotUsageSimulator.BundleTool --configuration Release -- create --snapshot examples/enterprise-import/snapshot.json --workload examples/enterprise-import/workload.json --overrides examples/enterprise-import/overrides.json --output artifacts/bundle-tool/example.compass.json
.\.dotnet\dotnet.exe run --project src/CopilotUsageSimulator.BundleTool --configuration Release --no-build -- validate artifacts/bundle-tool/example.compass.json
```

Import the generated bundle in the primary Compass page, then simulate. The synthetic example is valid but blocked by `cc-spend`; validity does not mean approval, and no balances advance.

See the [enterprise import guide](docs/ENTERPRISE-IMPORT.md) for pack/install instructions, environment-token authentication, live read-only collection, source coverage and explicit confirmations. A bundle represents one selected user/workload, must fit the 2 MiB file limit, and remains inside the existing September 2026 profile. Missing financial data is not silently treated as zero. Live collection does not configure GitHub or automatically produce a complete financial snapshot.

## Use the engine

```csharp
using CopilotUsageSimulator.Engine;
using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Guardrails;
using CopilotUsageSimulator.Engine.Simulation;

var configuration = EngineConfigurationLoader.LoadDefault();
ICopilotUsageSimulationEngine engine = new CopilotUsageSimulationEngine(configuration);

var result = engine.Simulate(new SimulationScenario
{
    OperationId = "chat",
    PlanId = "business",
    ProductId = "github-copilot",
    SkuId = "copilot-ai-credits",
    Timestamp = DateTimeOffset.Parse("2026-08-31T12:00:00Z"),
    Calls =
    [
        new ModelCallInput
        {
            ModelId = "gpt-5.6-luna",
            ContextTokens = 25_000,
            FreshInputTokens = 10_000,
            CachedInputTokens = 15_000,
            OutputTokens = 2_000,
            EnabledMultiplierIds = ["auto-model-selection"]
        }
    ],
    BillingContext = new BillingContext
    {
        BillingEntityId = "enterprise-1",
        CycleStart = DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
        CycleEnd = DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
        SeatAssignments =
        [
            new EffectiveSeatAssignment { UserId = "user-1", PlanId = "business" }
        ]
    },
    Attribution = new AttributionInput { UserId = "user-1" },
    EconomicGuardrails = new EconomicGuardrailSnapshot
    {
        PaidUsage = new PaidUsageAuthorization { State = GuardrailValue.Disabled }
    }
});

Console.WriteLine($"{result.Decision}: {result.Allocation.TotalCredits} credits");
```

`SimulationResult.Explanation` preserves the legacy prose explanation. The additive structured trace distinguishes actual evaluated checks from not-applicable, skipped, and cost-only-excluded stages. `FirstFailingGate` remains a stable machine-readable identifier. Each applied guardrail carries a shared `MetadataKey`; hosts can resolve it through `GuardrailMetadataCatalog` for a consistent label, category, settings anchor, documentation link, unit, and cost classification.

For the constrained Compass profile, any client can call `new SimulationPreviewService(configuration).Preview(scenario)` or `.Compare(scenario)`. `SimulationPreview` exposes nullable required costs, accepted costs, initial balances, reference identity, and explicit diagnostics. `SimulationComparison` carries an independent candidate scenario and its re-evaluated result; it does not mutate the baseline or authorize a live settings change. Use `ModelEligibilityEvaluator` in the engine's `Reference` namespace for supported model choices and selected tariff evidence rather than treating every historical catalog entry as currently available.

Direct core pricing also honors declared model restrictions where evidence covers the scenario, explicitly unpriced token components, and multiplier plan applicability for the selected seat. Unknown legacy availability metadata still permits configured historical pricing. Compass preview is stricter: unverified eligibility produces a non-estimate rather than approval.

## Configuration

The embedded `Configuration/default-catalog.json` contains effective-dated:

- plans and included-credit allowances;
- billed and unbilled operations;
- ordered access gates and operation applicability;
- model prices and context tiers;
- generic cost multipliers;
- GitHub Actions runner prices.

Load a replacement catalog without modifying the engine:

```csharp
var configuration = EngineConfigurationLoader.Load("my-catalog.json");
var engine = new CopilotUsageSimulationEngine(configuration);
```

Configuration is validated at load and engine construction. Unknown references, duplicate IDs, invalid effective periods, invalid context ranges, and negative prices are rejected.

An empty operation/model applicability set means "all." Nullable multiplier `applicablePlanIds` preserves legacy all-plan behavior when omitted; a supplied set is an explicit allow-list. IDs are compared case-insensitively. A model may have several non-overlapping `pricePeriods`, making promotions and future price changes data changes rather than code changes. Undocumented future prices should be omitted; the engine then fails explicitly with `pricing-not-effective`.

## Access integration

The engine does not call GitHub, an identity provider, or a network probe. A host evaluates those real-world conditions and supplies their state:

```csharp
AccessGates = new Dictionary<string, AccessGateState>
{
    ["license-seat"] = new() { Passed = true },
    ["network"] = new()
    {
        Passed = false,
        Reason = "Copilot endpoint timed out.",
        Remediation = "Check proxy and allowlist configuration."
    }
}
```

Unspecified gates use the catalog's `passWhenUnspecified` setting. Set it to `false` in strict deployments that require every gate to be supplied.

## Budget integration

`BillingContext`, `Attribution`, and `EconomicGuardrails` are immutable snapshots supplied with every billed request. The engine returns projected remaining balances but does not persist them. This keeps simulations deterministic and lets the host use a database, browser state, files, or no persistence.

The budget engine supports:

- individual, cost-center, and universal user-budget precedence;
- shared included pools;
- cost-center included-usage controls;
- paid-usage enablement;
- cost-center, organization, and enterprise metered budgets;
- hard-stop and alert-only metered budgets;
- split allocation between the remaining pool and metered usage.

Set `poolOverflowBehavior` to `split` or `meterEntireRequest` in the catalog. The engine derives the pool entitlement from effective seat assignments and subtracts `EnterprisePoolConsumedCredits` for the in-progress billing cycle.

Persist returned balances only after the host accepts a simulation as actual usage. Concurrent consumers should apply their own optimistic concurrency or transaction boundary.

### Economic guardrails

The single economic model derives pooled entitlement from effective Business and Enterprise seats, resolves cost centers and licensing organizations, selects the effective ULB, and evaluates every applicable cost-center, organization, and enterprise spending constraint.

```csharp
var scenario = new SimulationScenario
{
    OperationId = "chat",
    PlanId = "business",
    ProductId = "github-copilot",
    SkuId = "copilot-ai-credits",
    Timestamp = cycleTimestamp,
    Calls = calls,
    BillingContext = billingContext,
    Attribution = attributionInput,
    EconomicGuardrails = economicSnapshot,
    RuntimeGuardrails = runtimeSnapshot,
    ActionsGuardrails = actionsSnapshot
};

var result = engine.Simulate(scenario);
```

`AppliedGuardrails` reports every evaluated constraint, `EffectiveUlb` identifies the selected individual, cost-center, or universal limit, and `Alerts` contains thresholds crossed only by accepted charges. Actions access is checked before economic evaluation. Economic guardrails and AI-credit allocation run next, followed by the Actions spending budget only after economic approval. This preserves the economic constraint as the first failure when both spending meters would reject the request.

## Advanced simulator and hosting

The standalone web client runs the engine entirely in the browser. It provides guided task, workload, cost-center, ULB, budget, and Actions overrides, plus complete JSON editors for every scenario and catalog setting. Every evaluated check is visible by default and can be filtered by outcome or category.

See the [end-user guide](docs/USER-GUIDE.md). The in-app **User guide** link is disabled along with the other legacy-layout navigation; `/guide` remains available by direct URL.

```powershell
.\.dotnet\dotnet.exe run --project src\CopilotUsageSimulator.Web --configuration Release
```

The original Advanced workflow can still save scenarios, custom catalogs, and display preferences to its browser slot; its legacy export remains scenario-only. Use Compass bundles for portable matching reference evidence. No scenario data is sent to a server by either client.

The workflow in `.github\workflows\deploy-pages.yml` publishes the static WebAssembly output to GitHub Pages on pushes to `main`. Enable **Settings → Pages → Source: GitHub Actions** in the destination repository.

## Build and test

```powershell
.\.dotnet\dotnet.exe test CopilotUsageSimulator.slnx --configuration Release
.\.dotnet\dotnet.exe build CopilotUsageSimulator.slnx --configuration Release --no-restore
git diff --check
```

The web tests run through bUnit without starting a browser. They cover template rendering,
cost-only visibility, repeated simulations, blocking-setting highlights, validation errors,
official documentation links, shared controls, and JSON round trips.

The repository pins `.NET SDK 11.0.100-preview.7.26381.103` through `global.json`. `.dotnet` is ignored; no machine-specific SDK path is committed.
