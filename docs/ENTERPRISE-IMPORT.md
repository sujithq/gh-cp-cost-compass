# Enterprise Bundle Tool

`compass-bundle` is a separate, installable .NET tool. It collects read-only GitHub Enterprise Cloud evidence, replays that evidence offline with explicit workload and financial confirmations, and writes the existing Compass v1 JSON bundle. It does not require Blazor, Node.js, a browser, GitHub CLI, or a backend. It never changes GitHub settings or advances simulated balances.

## Install Locally

Use the repository-pinned .NET SDK `11.0.100-preview.7.26381.103`. The installed tool requires the matching .NET 11 preview runtime; a .NET 10 runtime alone is insufficient. No package publication is needed.

From the repository root on Windows:

```powershell
.\.dotnet\dotnet.exe pack src/CopilotUsageSimulator.BundleTool --configuration Release --output artifacts/bundle-tool/packages
.\.dotnet\dotnet.exe tool install CopilotUsageSimulator.BundleTool --version 0.2.0 --tool-path artifacts/bundle-tool/bin --add-source artifacts/bundle-tool/packages
$env:DOTNET_ROOT = (Resolve-Path .dotnet).Path
.\artifacts\bundle-tool\bin\compass-bundle.exe --help
```

On Linux/macOS, use the pinned SDK's `dotnet`, replace the executable suffix with `compass-bundle`, and set `DOTNET_ROOT` to the SDK installation when it is not installed system-wide. The package contains the tool, Bundles, Engine, Common, and System.CommandLine; it has no Web or browser runtime dependency. To replace an existing local installation after rebuilding, use `dotnet tool update` with the same tool path, package source and version (or uninstall/reinstall that local tool).

During development, commands can also run with `dotnet run --project src/CopilotUsageSimulator.BundleTool -- <command> ...`. Use the installed executable for JSON pipelines so build output cannot mix with stdout.

## Try the Offline Example

The three checked-in files under [examples/enterprise-import](../examples/enterprise-import/snapshot.json) contain synthetic accounts, two cost centers, a shared pool and selected-user budget state. They are not credentials or observations of a real enterprise.

```powershell
.\artifacts\bundle-tool\bin\compass-bundle.exe create --snapshot examples/enterprise-import/snapshot.json --workload examples/enterprise-import/workload.json --overrides examples/enterprise-import/overrides.json --output artifacts/bundle-tool/example.compass.json
.\artifacts\bundle-tool\bin\compass-bundle.exe validate artifacts/bundle-tool/example.compass.json
```

Expected: exit `0`, a valid **Blocked** preview, `cc-spend` as the first failing gate, 100 required AI credits, 60 available included credits, and no accepted consumption. A blocked or representable indeterminate preview is a useful bundle, not an invalid file.

In the primary Cost Compass page (`/`), use **Import a bundle or legacy scenario** to choose the generated bundle, then **Simulate**. The catalog and fingerprint travel with the scenario. Repeated previews do not advance balances. Import the bundle, not the collection snapshot or report.

## Collect an Enterprise

Supported host: GitHub Enterprise Cloud on `github.com`, API host `https://api.github.com`, version `2026-03-10`. GHES and data-residency-specific hosts are not supported in this release.

Populate `GH_TOKEN` in your shell using your normal secure credential workflow. The tool falls back to `GITHUB_TOKEN` when `GH_TOKEN` is blank, or reads only the variable named by `--token-env`. There is no inline-token option. Never put a token in arguments, workload JSON, snapshots, reports, source control, or chat.

For full coverage, use an enterprise-authorized classic PAT with `read:enterprise` and the endpoint-specific enterprise owner/billing access, including SSO authorization where required. Enterprise-team GET endpoints do **not** support fine-grained PATs or GitHub App tokens. Billing-only or partially authorized credentials may return a partial snapshot; a `403`/`404` is not evidence that no team or budget exists. Do not request write scopes for this workflow.

```powershell
.\artifacts\bundle-tool\bin\compass-bundle.exe collect --enterprise YOUR-ENTERPRISE --user YOUR-LOGIN --output artifacts/enterprise-import/snapshot.json
```

Add `--all-seat-usage` to collect an unfiltered enterprise AI-credit usage report and one monthly report for every unique observed Copilot seat login. The selected `--user` remains the single identity used when creating a bundle. For `N` unique seats, this option makes `N + 1` AI-usage requests: one enterprise aggregate and one per user. Duplicate grants for the same GitHub user do not cause duplicate usage requests.

Aggregate reports have `kind: "ai-credit"` and `user: null`; per-seat reports identify the login in `user`. These are monthly reported usage observations, not an authoritative live pool ledger. GitHub does not return a reliable reporting cutoff, so `reportedThrough` remains null and recent activity may not yet appear. A failed per-seat or aggregate request is retained as incomplete source coverage rather than being interpreted as zero usage.

Collection saves an adjacent `snapshot.json.report.json` coverage report. A failed source is recorded with its dataset, endpoint, response-page count, status when available, and sanitized problem. Exit `3` means collection is incomplete even when a useful partial snapshot was saved. Fix authorization/data availability and recollect; do not mark incomplete inventories as empty or manually flip coverage flags.

All HTTP requests are GETs. Pagination stays on the approved HTTPS API host and resource, preserves filters and page sequence, rejects repeated pages, and never follows redirects with credentials. Requests have bounded retries, page deadlines, page counts and response sizes. Ordinary authorization errors are not retried. A long `Retry-After` asks the operator to retry later instead of waiting indefinitely.

### Collected Evidence

| Dataset | GET resource under `/enterprises/{enterprise}` | Interpretation |
|---|---|---|
| Shared seats | `/copilot/billing/seats` | Every grant page; verify unique assignees against `total_seats`, then reconcile one effective seat per user. Duplicate grants do not add allowances. |
| Cost centers | `/settings/billing/cost-centers`, then `/{id}` | Full resource detail, including `has_next_page`, state and nullable included-control observations. |
| Enterprise teams | `/teams`, then `/{encoded-slug}/memberships` | Resolve cost-center team references, creation dates and actual membership. Retain membership only for referenced teams; assigning-team metadata is not a substitute. |
| Budget definitions | `/settings/billing/budgets` | All visible scopes/pages, stable IDs, product/type, amounts, enforcement, alert preference and expiry. |
| Selected-user budget | `/settings/billing/budgets?user=...`, then `/{id}/user-states?user=...` for multi-user budgets | Separate per-user consumption and returned effective-budget identity from aggregate group consumption. |
| AI usage | `/settings/billing/ai_credit/usage?year=...&month=...[&user=...]` | Reported selected-user quantities/amounts by default; `--all-seat-usage` also captures enterprise aggregate and every unique observed seat. Not a live pool balance. |
| Actions usage | `/settings/billing/usage/summary?year=...&month=...&product=actions` | Separate enterprise Actions reporting across cost centers, not the older endpoint's default no-cost-center subset. |

API response fields outside the typed snapshot are discarded, including emails, avatars, alert-recipient lists and unrelated profile data. Collection records its start/end, API version, source coverage and report periods. The APIs do not establish a current reporting cutoff, so `reportedThrough` remains null. Current configuration is not a historical effective-dated export, and collection is not atomic across endpoints.

Org billing settings are not fetched as a substitute for missing seat plans or paid-usage authorization. Unknown/conflicting plans require a typed confirmation; a user with several observed licensing organizations needs an explicit cycle-selected organization. These choices are never inferred from the largest plan allowance.

## Create Offline

```text
compass-bundle create --snapshot snapshot.json --workload workload.json --output scenario.compass.json
    [--overrides overrides.json] [--catalog catalog.json] [--report report.json] [--overwrite]
compass-bundle validate scenario.compass.json [--report report.json] [--overwrite]
```

`create` and `validate` do not access GitHub. The default catalog is the Engine's embedded, dated catalog. A supplied catalog must satisfy the same validation and supported-profile checks; a hash is not a signature or proof that custom pricing is official.

`--output -` emits only snapshot/bundle JSON on stdout and diagnostics on stderr. Without `--report`, `validate` prints its report as JSON on stdout. `--report` always names a file. File outputs include a sibling report by default; file creation requires `--overwrite` to replace existing outputs and always refuses input/output/report collisions, including resolved filesystem aliases.

Every file is written as UTF-8 via a sibling temporary file and rename. Validation, cancellation and write errors cannot replace an existing bundle with partial JSON. The report is committed before the bundle; these two files are not a multi-file filesystem transaction. Stdout streams are not atomic files. New files use owner-only permissions on Unix and inherited directory ACLs on Windows.

### Workload Input

Use [workload.json](../examples/enterprise-import/workload.json) as the minimal explicit Chat example. Schema version `1` accepts `chat`, `cli`, or the supported private `cloud-agent` case, sized model calls, and optional known modifiers. No future token demand is inferred from historical usage or a task description.

Default `checkScope` is `costRelatedOnly`. Full scope uses `all` and requires explicit applicable `accessGates` and `runtimeGuardrails` (an empty runtime snapshot is a deliberate no-supplied-limits assumption). Unknown Actions access states remain Unknown; the tool never invents passing permissions.

For cloud-agent, also supply this `actions` object in the workload:

```json
{
  "account": "CONFIRMED-ACTIONS-PAYER",
  "minutes": 5,
  "includedMinutesRemaining": 0,
  "applicableBudgetIds": [],
  "applicableBudgetsConfirmed": true,
  "actionsEnabled": "unknown",
  "runnerAvailable": "unknown",
  "workflowApproved": "unknown",
  "repositoryRulesPermitRun": "unknown"
}
```

Replace those demonstration assumptions with confirmed values. Minutes must be whole, pre-accounted minutes for the private standard Linux 2-core runner. The remaining Actions allowance is a separate balance, never derived from a Copilot plan. Budget IDs must identify observed Actions product or standard Linux SKU budgets and be explicitly confirmed as applicable to that payer. An empty list confirms none; it does not auto-discover account/repository applicability.

### Financial Confirmations

All input documents are strict JSON: unknown members, duplicate keys, numeric enum values and null required collections are rejected. Optional provider financial observations remain nullable. Missing is not `0`, `false`, `[]`, Disabled, or unlimited.

Start from [overrides.json](../examples/enterprise-import/overrides.json), but do not reuse the synthetic balances for a real enterprise:

| Field | Required meaning |
|---|---|
| `schemaVersion` | `1` |
| `expectedPoolEntitlementCredits` | Explicit shared entitlement, reconciled against all effective seats using the dated Engine allowance. A mismatch blocks creation. |
| `enterprisePoolConsumedCredits` | Authoritatively confirmed included credits consumed in this UTC month. API discounts and overlapping budget consumption are not substitutes. |
| `enterpriseBudgetExcludedCostCenterIds` | Explicit observed cost-center IDs, or `[]` when none are excluded. |
| `paidUsage` | Explicit `state` (`enabled`, `disabled`, `unknown`) when known; omission preserves Unknown. |
| `seats` | Map provider user ID to optional `planId` and `licensingOrganization`. The organization must match an observed grant. |
| `includedControls` | Map cost-center ID to optional `enabled`, plus `entitlementCredits`, `consumedCredits`, `overflowBehavior` (`block` or `paidUsage`) when enabled. |
| `budgets` | Map observed budget ID to optional `limitUsd`, `consumedUsd`, `enforcement` (`hardStop` or `alertOnly`) and `trackingStartedAt`. Consumption must match the declared tracking baseline. |

Included-control `targetAmount`/`currentAmount` fields are retained as observations, but their unit and consumed-versus-remaining semantics are not guessed. Enabled selected controls need confirmed credit values and overflow behavior. Confirmed cap entitlement must agree with the Engine's effective cost-center seats.

ULB amounts are documented USD limits, converted with the validated catalog's USD-per-credit rate. Multi-user budgets require this selected user's consumption, never a group's aggregate or an absent row interpreted as zero. Returned effective IDs, per-user targets and consumption must agree with resolved definitions unless an allowed numeric confirmation explicitly reconciles the difference. ULBs are hard stops; spending budgets retain hard-stop versus alert-only behavior. Date-only expiry is interpreted as an exclusive midnight-UTC boundary.

Supported AI mapping is explicit: `BundlePricing/ai_credits`, `ProductPricing/copilot` (or `github-copilot`), and `SkuPricing/copilot_ai_credits` (or `copilot-ai-credits`). Recognized non-AI product budgets are reported as not applicable. Other type/product combinations and unsupported AI scopes block creation rather than silently discarding a possible constraint. An API schema that changes must be reviewed before adapting the projection.

The report includes the typed confirmations, unapplied-confirmation warnings, reconciliation diagnostics and Engine preview assumptions. Compact provenance and snapshot/override hashes go in scenario metadata; the raw snapshot is not rewritten or embedded wholesale.

## Boundaries and Privacy

- One selected user and one workload per bundle. Shared seat inventory across cost centers is retained, and `--all-seat-usage` may retain monthly usage observations for every seat, but ULB consumption and applicable controls remain selected-user data. Export again before changing the selected user; the app's user selector does not fetch fresh consumption.
- Current Compass evidence supports Business/Enterprise scenarios from **September 1 through September 27, 2026**, with narrower model-price evidence windows. Collection can run later; creation then reports an unsupported date. Never backdate a live snapshot or extend prices to obtain a successful estimate.
- The output bundle must fit the browser's **2,097,152-byte** file-import limit. The collector/replay input limit is **64 MiB**, and each HTTP page is bounded to **4 MiB**. Oversized enterprises fail explicitly; seats are never dropped to fit the bundle.
- Required seat, cost-center, team and budget inventory coverage must be complete. Optional usage/state failures remain visible; missing nonnullable balances still require confirmations. Capture may not cross a UTC billing-month boundary.
- Seat changes, pending cancellations, retained entitlements, historical membership and ambiguous licensing sources may require reconciliation beyond this tool. No unsupported dates, products, accounts or historical states are synthesized.
- Store real output under an access-controlled directory, such as the ignored `artifacts/enterprise-import/` folder. Git ignore is not access control. Snapshots/reports/bundles contain account identities and financial assumptions; review them before sharing or uploading to a hosted app. No token is embedded, and no telemetry or raw HTTP response log is written.
- There are no POST report jobs, settings changes, CSV guessing, batch portfolio simulation, automatic pricing downloads, NuGet publication or automatic Git operations.

## Exit Codes

| Code | Meaning |
|---|---|
| `0` | Valid output/report, including modeled Blocked or Indeterminate verdicts |
| `2` | Invalid arguments or input JSON/file |
| `3` | Missing credentials, failed source or incomplete collection |
| `4` | Unreconciled data, unsupported mapping/profile or incompatible bundle |
| `5` | Output/report failure or overwrite refusal |
| `130` | Cancellation |

Verification uses sanitized HTTP fixtures, offline CLI replay, the primary Compass file-import workflow, and local pack/install/invoke smoke checks. No authenticated enterprise test is implied by those checks.

## API References

- [Copilot seats](https://docs.github.com/en/enterprise-cloud@latest/rest/copilot/copilot-user-management)
- [Cost centers](https://docs.github.com/en/enterprise-cloud@latest/rest/billing/cost-centers)
- [Enterprise teams](https://docs.github.com/en/enterprise-cloud@latest/rest/enterprise-teams/enterprise-teams) and [memberships](https://docs.github.com/en/enterprise-cloud@latest/rest/enterprise-teams/enterprise-team-members)
- [Budgets and per-user states](https://docs.github.com/en/enterprise-cloud@latest/rest/billing/budgets)
- [Billing usage reports](https://docs.github.com/en/enterprise-cloud@latest/rest/billing/usage)

Endpoint schemas and limitations were checked against these public references on 2026-09-12; preview APIs can change.