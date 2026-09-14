# Cost Compass and advanced simulator user guide

The web app estimates whether an agent task can run under a given GitHub Copilot billing and guardrail configuration. It runs entirely in the browser and does not send scenario data to a server.

## Cost Compass: configure, simulate, explain, resolve

The root page (`/`, or `/compass`) offers focused **Chat**, **Cloud agent**, and **CLI** presets for standard September Business/Enterprise pooled-seat scenarios. The default demo requires 100 credits with 60 included credits remaining and paid usage disabled. It is a rejected request, not consumed usage.

Edit the billed user, selected seat plan, cost center, workload, and controls. Select **Simulate** for an immutable preview: pressing it again does not consume additional balances. An input change replaces the old verdict with **Needs simulation**. Invalid numbers and failed imports cannot leave a stale success visible.

The result shows the exact first blocker, required credits, proposed charges, accepted allocation, and remaining balances. The trace differentiates actual checks from **Not applicable**, **Not evaluated**, and **Excluded** in cost-only mode. These are the deterministic engine's stages, not a discovered live GitHub request pipeline.

The setting responsible for the current first blocker has a red outline and a **First blocker** label. Its section opens when the result is evaluated. Select **Review blocking setting** to reopen any collapsed sections, scroll to the setting, and focus it. Changing inputs clears the old highlight until a new simulation; automatic highlighting does not move keyboard focus.

When the exact blocking record is outside the guided fields, such as an enterprise budget or a second imported Actions budget, the configuration panel shows its identity, message, and available limit/usage values. **Inspect in bundle editor** opens the complete scenario for that record without changing or applying it. A similarly named editable control is never substituted for the actual blocker. Unsupported-evidence results without a first failing check do not invent a blocking setting.

Select **Preview alternatives** to re-evaluate candidate changes. Compare their decisions, USD deltas, and any remaining blocker. **Use this scenario** changes only the local form; simulate again to confirm it. A smaller workload is not advertised as fitting an empty allowance, and enabling paid usage may reveal a subsequent hard-stop budget.

The cloud-agent preset accepts pre-accounted, per-job-rounded minutes for a private repository on a standard Linux 2-core runner. Its GitHub account allowance is separate from Copilot credits. Code review's undisclosed model, unlicensed/fallback behavior, and non-guaranteed price ranges are explained rather than presented as a fixed-price preset. Personal billing, historical promotional cohorts, compliance modifier stacking, broader runner classes, standalone Actions, and future rollout/payment scenarios are not silently modeled as supported.

**Save scenario** uses a separate single browser slot. **Export bundle** includes current form edits, complete scenario, catalog, reference source/date metadata, engine contract version, and catalog fingerprint. **Load browser save** and **Import** validate the bundle before changing the working state. Legacy scenario-only imports clearly use the active reference. Use the complete bundle editor for additional calls and advanced records; the guided workload fields edit the first call without discarding the others.

The result evidence identifies the simulation date, catalog, verification date, limitations, and official sources. A fingerprint detects accidental catalog mismatches, not the trustworthiness of a custom reference. The header theme switch supports light/dark; an explicit `scoutTheme=light` or `scoutTheme=dark` query parameter takes precedence over OS preference.

## Advanced simulator quick start

Open `/advanced` for the preserved full simulator. **Only this workflow advances balances on successful runs.** The instructions below describe Advanced, not the preview-only Compass.

In-app links to `/advanced` and `/guide` are disabled while they use the legacy layout. Their direct URLs remain available; the Cost Compass, Decision flow, Sources & scope, and external documentation links remain active.

1. Choose **Cloud agent**, **Code review**, or **Chat** as a starting template. Alternatively, select one of the six green cost-blocked scenarios to load and inspect a user-level budget, included-use overflow, paid-usage applicability, paid-usage state, AI spending budget, or Actions spending budget failure. The selected starter remains highlighted, and the scenario source changes from **Template defaults** to **Customized** after you edit a field.
2. Describe the task the agent is expected to perform.
3. Select the operation first. The guided form shows only settings that operation can use.
4. For billed operations, select the model and enter expected context, fresh input, cached input, cache-write, and output tokens.
5. Override the visible billing, ULB, budget, repository, and Actions settings as needed.
6. Select **Apply overrides and simulate**.
7. Review the decision, first failing check, cost estimate, attribution, remaining balances, and ordered guardrail checks.

Set **Repeat task** above one to run the same workload sequentially. Successful runs carry their AI-credit pool, ULB, included-control, spending-budget, runtime, Actions-minute, and Actions-budget consumption into the next run. Simulation stops at the first non-allowed result, and the run history identifies exactly which repetition was blocked.

Successful clicks on **Apply overrides and simulate** also advance the current working balances. Clicking it again continues from the previous result. Choose a template or load a saved/imported scenario to reset the starting state. A blocked, waiting, soft-stopped, or indeterminate run does not consume balances.

The task description records intent; it does not automatically predict tokens. For uncertainty, save separate low, expected, and high workload scenarios.

The operation definition in the active catalog drives field visibility. Unbilled operations
hide and ignore token, billing, runtime, ULB, and spending controls. Billed operations that
do not use Actions hide runner and repository controls. Code review shows repository
visibility because Actions metering applies only to private and internal repositories, while
cloud agent always uses Actions. Hidden values remain in the complete scenario JSON so
switching operations does not erase configuration, but irrelevant values are not evaluated.
The explanation beneath the operation selector summarizes the active behavior.

**Cost-related checks only** is enabled by default. It evaluates and displays attribution,
ULBs, included-credit controls and pools, paid usage, spending budgets, and Actions cost.
It skips access gates, runtime limits, repository policies, and workflow approvals. Turn
the switch off to include the complete operational and cost guardrail sequence.

To simulate without a cost center, clear the **Cost center** field. The user is then attributed at organization/enterprise level. Cost-center ULBs, included controls, and budgets do not apply, while applicable organization and enterprise constraints remain active.

Optional guardrails are configured with plain-language cards. Universal, cost-center, and individual ULBs can be configured independently. The most specific applicable ULB wins: **Individual → Cost center → Universal**. This lets an individual ULB act as a higher or lower exception to the broader defaults. Turn off a switch to omit that control from the simulation. Each enabled budget exposes its limit, current spending, and stop/alert behavior.

The **AI credits paid usage policy** field represents GitHub's documented control for allowing Copilot overage after included AI credits are exhausted. See [Usage-based billing for organizations and enterprises](https://docs.github.com/en/copilot/concepts/billing/usage-based-billing-for-organizations-and-enterprises#what-happens-if-i-exceed-my-included-ai-credits).

The guided **Authorized product IDs** and **Authorized SKU IDs** fields configure simulator applicability at `economicGuardrails.paidUsage.productIds` and `economicGuardrails.paidUsage.skuIds`. Enter comma-separated IDs; an empty field applies to all values. The **Paid usage not authorized for GitHub Copilot** starter requests `github-copilot` while authorizing only `github-actions`, so it stops at `paid-usage.not-applicable`. These allow-lists are simulator inputs, not a literal GitHub paid-usage policy setting. They model product/SKU scope using the same concepts GitHub documents for [Product-level, SKU-level, and Bundled AI credits budgets](https://docs.github.com/en/billing/how-tos/set-up-budgets#creating-a-budget).

## Result decisions

| Decision | Meaning |
|---|---|
| Allowed | Every required check passed. |
| Blocked | A hard-stop guardrail rejected the task. |
| Soft stopped | A runtime or client limit stopped further work. |
| Waiting | Approval or another external condition is required. |
| Indeterminate | Required billing or policy data is unknown or ambiguous. |
| Partially simulated | The supplied information cannot produce a complete calculation. |

`FirstFailingGate` identifies the check that stopped the simulation. Blocked usage is not deducted from returned balances.

When a run stops, start with the **Why it stopped** card. It names the blocking
scope in plain language, such as agent runtime, individual ULB, cost-center control,
or spending budget. It also shows the configured limit, already-used amount, current
request, and projected remainder. Later checks may not appear because evaluation stops
at the first blocking guardrail.

Guided sections marked **Scenario JSON** update the corresponding values in the complete
scenario document. Guided changes patch the displayed guardrail by its ID; additional
guardrails and advanced properties such as applicability filters and effective dates are
preserved. Additional model calls, licensing organizations, and effective-dated assignments
are also retained; the guided fields update the displayed call or assignment only. After a
run stops, the responsible control is outlined in red and marked
**Stopped here**. Use **Review highlighted setting** to jump to it. If that setting has no
guided control, the complete scenario JSON opens and is highlighted instead.

A **Docs ↗** link beside a setting opens the matching official GitHub documentation in a new
tab. Simulator-only controls, such as repeat count and custom runtime limits, intentionally
have no GitHub Docs link because they do not represent GitHub billing settings.

A green **Cost related** badge identifies fields and groups that affect pricing, credit
allocation, cost attribution, or cost guardrails. Unmarked controls, such as the task
description and runtime limits, do not directly affect the cost calculation.

## Cost and attribution

- **AI credits** is the calculated request total.
- **Included credits** come from the applicable included pool.
- **Metered credits** are paid overage.
- **Metered cost** is the overage charge in USD.
- **Actions cost** is the additional runner charge.
- **Attribution** shows the user, licensing organization, cost center, and effective ULB.

Each visible guardrail row reports its outcome, enforcement, limit, prior consumption, requested usage, projected remainder, and explanation. Display all checks, issues only, failures only, or selected categories.

## Developer scenario configuration

Most simulations do not require JSON. The optional complete scenario JSON editor exposes every engine input when a developer needs a setting that is not available in the guided form:

- task metadata, operation, plan, product, SKU, timestamp, and repository visibility;
- model calls, token classes, and multipliers;
- access gates;
- billing cycle and effective seat assignments;
- direct, team, and organization cost-center attribution;
- individual, cost-center, and universal ULBs;
- included pool consumption and cost-center included-usage controls;
- paid-usage policy and guided product/SKU applicability;
- cost-center, organization, and enterprise spending budgets;
- enterprise budget exclusions;
- runtime call, depth, duration, and CLI credit limits;
- Actions access, runner, approval, repository, included-minute, and spending controls.

Select **Simulate JSON** after editing it. Select **Load JSON into guided fields** to synchronize common values back into the form.

## Pricing and policy catalog

The catalog editor controls plans, included allowances, operations, model price periods, context tiers, access gates, multipliers, pool overflow behavior, and Actions runner prices. Select **Apply catalog** before rerunning the scenario. Invalid catalogs are rejected rather than silently using defaults.

## Local storage and files

- **Save locally** stores the scenario, catalog, and visibility preferences in browser storage.
- **Load saved** restores the latest local state.
- **Export JSON** downloads the current scenario.
- **Import JSON** loads and simulates a scenario file.

Clearing browser site data removes saved configurations. Export scenarios that must be retained or shared.
