# GitHub Copilot Usage Simulator - Guardrail Gap Analysis

**Analysis date:** 31 August 2026  
**Status:** Historical pre-implementation snapshot. No engine changes are authorized by this document.

> This document records the engine state analyzed on 31 August 2026. Its capability statements and gap matrix are not the current implementation status. See [docs/MAINTAINABILITY-REVIEW.md](docs/MAINTAINABILITY-REVIEW.md) for current reviewed findings.

This analysis compares:

1. [GitHub-Copilot-Token-Usage-UBB-and-Policy-Analysis.md](GitHub-Copilot-Token-Usage-UBB-and-Policy-Analysis.md);
2. current GitHub billing and cost-center documentation;
3. [Copilot-Token-Usage-Simulator-Flows.md](Copilot-Token-Usage-Simulator-Flows.md);
4. the current `.NET 11` engine in `src/CopilotUsageSimulator.Engine`.

## 1. Executive finding

The current engine correctly prices model calls and provides a first implementation of access gates, ULB precedence, shared-pool allocation, one cost-center included control, paid usage, and one selected metered budget. It is not yet a complete guardrail simulator.

The largest conceptual gap is that guardrails are not a single sequence of independent gates. They form a **time-sensitive constraint lattice**:

- attribution selects the user, licensing organization, cost center, and billing entity;
- one effective ULB constrains all of that user's included and metered consumption;
- the enterprise pool and optional cost-center included control constrain included allocation;
- paid-usage policy authorizes or denies overage;
- narrow and enterprise spending limits can constrain the same metered charge simultaneously;
- product policies, runtime limits, Actions controls, and availability gates constrain different stages;
- alerts observe threshold crossings but do not authorize usage;
- unknown attribution or undocumented billing behavior must remain indeterminate.

Extending the former balance-only budget model would not have been sufficient. The domain model instead separates identity, attribution, entitlements, controls, consumption state, and evaluation evidence.

## 2. Authoritative behavior established

### 2.1 ULB controls

Source: [Budgets for usage-based billing](https://docs.github.com/en/copilot/concepts/billing/budgets-for-usage-based-billing)

| Rule | Required simulator behavior |
|---|---|
| Universal ULB | Default per-user limit for licensed enterprise users |
| Cost-center ULB | Per-user default for members of the attributed cost center |
| Individual ULB | Specific-user override |
| Precedence | Individual, then cost-center, then universal |
| Phase | Applies to included pool and metered usage |
| Enforcement | Always a hard stop |
| Zero value | Blocks immediately |
| Independence | No group spending budget supplements or overrides an exhausted ULB |
| Reset | User consumption resets with the monthly UTC billing cycle |

The simulation state must retain `limit`, `consumed`, `reserved`, `remaining`, `cycle`, and `effective interval`. A precomputed remaining value alone cannot explain resets, concurrent reservations, or historical scenarios.

### 2.2 Cost-center attribution

Source: [Cost center allocation for different products](https://docs.github.com/en/billing/reference/cost-center-allocation)

For Copilot license and AI-credit usage:

1. direct user assignment has priority;
2. otherwise an assigned enterprise team applies;
3. if several enterprise teams map the user to different cost centers, the earliest-created team applies;
4. otherwise the organization granting the Copilot license determines cost-center attribution;
5. if the organization is not in a cost center, usage is enterprise-only.

For users licensed by multiple organizations, the billing organization can differ by cycle. A simulator needs either the cycle-selected organization or an explicit attribution strategy. It must not silently choose the first organization.

Attribution affects:

- cost-center ULB;
- included-usage cap;
- cost-center metered budget;
- enterprise-budget exclusion;
- usage ledger and reporting.

### 2.3 Included usage controls

Source: [Budgets for usage-based billing](https://docs.github.com/en/copilot/concepts/billing/budgets-for-usage-based-billing#included-usage-controls-for-cost-centers)

The cap is derived from licenses assigned to members of the cost center:

```text
included_cap =
    business_licenses * business_credits_per_license
  + enterprise_licenses * enterprise_credits_per_license
```

At the standard 1 September 2026 amounts, these inputs are 1,900 and 3,900 credits. They must remain effective-dated.

| Change | Timing |
|---|---|
| Licensed member added, license granted, or license upgraded | Immediate increase |
| Member removed, license lost, or license downgraded | Decrease next cycle |
| Member moves between controlled cost centers | Recalculate next cycle |
| Unlicensed member changes | No effect |

The control limits only that cost center's draw from the enterprise pool. At its cap it either blocks or routes further consumption to paid usage. It is distinct from both a cost-center ULB and a cost-center metered budget.

### 2.4 Metered spending constraints

Sources:

- [Budgets for usage-based billing](https://docs.github.com/en/copilot/concepts/billing/budgets-for-usage-based-billing)
- [Optimizing your budget configuration](https://docs.github.com/en/copilot/tutorials/budgets/optimizing-your-budget-configuration)

Metered usage requires `AI credits paid usage` to be enabled. When enabled:

- attributed cost-center usage checks the cost-center budget if one exists;
- otherwise an applicable organization budget may be checked;
- enterprise budget is the failsafe for usage not covered by a narrow budget;
- cost-center and organization metered usage counts against enterprise spend by default;
- cost-center exclusion removes cost-center metered usage from the enterprise budget;
- all applicable hard-stop constraints must have enough headroom;
- exhausted limits with `Stop usage when budget limit is reached` disabled continue charging;
- the stop flag is off by default;
- organization budgets can further restrict but cannot loosen enterprise controls;
- a budget applies only to metered use from its creation time, which permits first-cycle apparent overshoot.

The simulator therefore needs an array of applicable constraint evaluations, not a single `MeteredBudgetId`.

### 2.5 Product and SKU matching

Budgets can target a product, a SKU such as `Copilot AI credits`, `Spark AI credits`, or `Copilot cloud agent`, or bundled AI credits. Scope cannot be changed after creation.

Each request must identify product and SKU before budget matching. A budget with the right owner scope but the wrong SKU is not applicable.

### 2.6 Individual-plan overage

Individual plans do not use the enterprise cost-center path. The model needs:

- included allowance and consumption;
- additional-usage enablement;
- possible provider cap, which is not numerically documented;
- unpaid additional usage;
- temporary authorization hold;
- subscription purchase channel;
- inability to buy extra credits through mobile iOS/Android subscriptions.

Unknown provider caps must produce `indeterminate`, not unlimited use.

### 2.7 Runtime and session guardrails

| Control | Type | Documented behavior |
|---|---|---|
| Copilot CLI `max-ai-credits` | Soft stop | Per-session public-preview limit |
| VS Code `chat.agent.maxRequests` | Hard client stop | Default 25 model requests per turn |
| Subagent nesting | Hard technical stop | Maximum depth 5 |
| Cloud-agent duration | Hard technical stop | 59 minutes, cannot be bypassed |
| Rate limits | Retryable availability stop | Separate from credits; numeric thresholds unpublished |
| Utility-model rate limits | Rate stop | Can apply even though usage is not billed |

These controls consume counters different from monthly AI credits and must not be represented as spending budgets.

### 2.8 Actions guardrails

Cloud agent and private-repository code review can consume Actions minutes in addition to AI credits. The complete path needs:

- Actions enabled state;
- GitHub-hosted runner availability;
- repository visibility;
- workflow approval;
- automation write access;
- branch protection and rulesets;
- included Actions minutes;
- Actions budget and hard-stop state;
- runner type, duration, and additional cost;
- cloud-agent 59-minute maximum.

AI-credit success does not imply Actions authorization.

### 2.9 Policy and access guardrails

The existing flat access-gate input can simulate a resolved answer but cannot explain how it was reached. A complete resolver needs:

- enterprise tri-state and lock;
- organization delegation;
- repository-wide enterprise blocks for cloud agent and code review;
- policy-specific multi-org and multi-enterprise conflict rules;
- enterprise-assigned user defaults;
- repository settings and rulesets;
- user controls where allowed;
- managed client precedence;
- surface applicability;
- model-specific and preview enablement;
- content exclusion support holes;
- network, proxy, TLS, authentication, SSO, rate-limit, and regional/provider state.

This can remain a separate module from economic guardrails, but its evidence must be included in the same ordered result trace.

## 3. Notification model

Budget thresholds at 75%, 90%, and 100% are notifications. They do not block unless the underlying spending limit is configured as a hard stop.

Important distinctions:

- ULB alerting is not consistently available;
- AI-credit shared-pool depletion alerts are not documented;
- included-usage alerts documented by GitHub cover Actions, Packages, LFS, and Codespaces, not Copilot AI credits;
- alert recipients and delivery are administrative metadata, not simulation authorization.

The result should emit threshold events with evidence and confidence rather than mix them into `decision`.

## 4. Explicit assumptions and unknowns

| Topic | Status | Required treatment |
|---|---|---|
| Fractional-credit rounding | Undocumented | Retain precision; label display rounding |
| Split final pool credits plus metered remainder | Undocumented | Configurable strategy with assumption |
| Reasoning-token price class | Undocumented | Configurable; default may use output rate only with assumption |
| Auto-selection plus residency multiplier interaction | Not documented together | Configurable ordered adjustment pipeline with assumption |
| AI-credit pool-depletion alerts | Unverified | Do not emit by default |
| Multi-org billing organization before cycle resolution | Non-deterministic | Require explicit cycle attribution or return indeterminate |
| Claude extended-context price tiers | Unpublished | Reject or return indeterminate beyond documented tiers |
| Individual additional-usage cap | Unpublished | Unknown value, not infinity |
| IDE/enterprise BYOK AI-credit treatment | Unverified | Separate configured mode or indeterminate |

## 5. Engine gap matrix at analysis date

| Capability | State on 31 August 2026 | Gap identified on 31 August 2026 |
|---|---|---|
| Token pricing | Effective-dated models and context tiers | Add unknown/unpublished tier outcomes rather than only exceptions |
| ULB precedence | Enum ordering chooses one supplied entry | No target identity, effective date, configured amount, consumption, or attribution binding |
| ULB enforcement | Compares request against remaining credits | No reservations, cycle state, or concurrent-consumption protection |
| Cost-center attribution | Caller supplies one ID | No direct/team/org resolution or evidence |
| Multiple organization licensing | One organization ID | No cycle-selected billing organization or indeterminate result |
| Included cap | Caller supplies remaining amount | No license-derived cap or timing rules |
| Shared pool | Caller supplies or derives one-user plan allowance | Pooled enterprise allowance must derive from effective seat inventory, not one user |
| Paid usage | Boolean | Needs billing-entity and product/SKU applicability |
| Metered budgets | One selected narrow budget | Must evaluate all applicable constraints plus enterprise unless excluded |
| Cost-center exclusion | Missing | Required |
| Organization restriction | Single selection path | Must coexist with enterprise constraint |
| Product/SKU scope | Missing | Required for budget matching |
| Budget lifecycle | Remaining dollars only | Add created/effective/deleted timestamps and tracking baseline |
| Alerts | Missing | Emit threshold events without changing authorization |
| Individual overage | Missing | Separate path required |
| Runtime guardrails | Mostly generic access gates | Add typed counters and soft/hard outcomes |
| Actions | Runner minutes and price | Missing Actions entitlement, allowance budget, repository controls |
| Policy resolver | Pre-resolved dictionary | Missing precedence, locks, conflicts, and evidence |
| Outcomes | Allowed, blocked, partially simulated | Add soft-stopped, waiting, and indeterminate |

## 6. Target domain boundaries

```mermaid
flowchart LR
    SCENARIO[Simulation Scenario] --> IDENTITY[Identity and Entitlement]
    IDENTITY --> ATTRIBUTION[Billing and Cost-Center Attribution]
    ATTRIBUTION --> POLICY[Effective Policy Resolver]
    POLICY --> ACCESS[Access Guardrails]
    ACCESS --> USAGE[Usage Estimator]
    USAGE --> ULB[Effective ULB]
    ULB --> INCLUDED[Pool and Included Controls]
    INCLUDED --> METERED[Paid Usage and Constraint Lattice]
    METERED --> ACTIONS[Actions Guardrails]
    ACTIONS --> ALERTS[Threshold Events]
    ALERTS --> RESULT[Decision, Ledger, Evidence, Assumptions]
```

Recommended aggregate roots:

| Aggregate | Responsibility |
|---|---|
| `SimulationScenario` | Immutable requested simulation and effective timestamp |
| `BillingContext` | Billing entity, cycle, plan and effective seat inventory |
| `AttributionContext` | User, licensing organization, cost center, rule and confidence |
| `PolicySnapshot` | Raw policies plus resolved effective controls |
| `UsageEstimate` | Calls, tokens, adjustments, AI credits and secondary units |
| `GuardrailSnapshot` | ULBs, included controls, paid-usage policy, spending budgets, session and Actions controls |
| `ConsumptionSnapshot` | Per-cycle user, pool, cost-center, budget and Actions consumption |
| `SimulationResult` | Decision, all applied constraints, reservations, allocations, alerts and evidence |

## 7. Recommended implementation sequence

1. Replace balance-only budget DTOs with effective-dated configured controls plus consumption snapshots.
2. Implement deterministic billing-organization and cost-center attribution with `indeterminate` support.
3. Implement effective ULB selection and per-user cycle headroom.
4. Derive enterprise pool and cost-center included cap from seat/license inventory.
5. Implement paid-usage authorization and product/SKU matching.
6. Evaluate the complete metered constraint lattice, including enterprise concurrency and cost-center exclusion.
7. Add alert events and budget lifecycle baselines.
8. Add individual-plan overage.
9. Add typed session/runtime and Actions guardrails.
10. Add effective policy resolution and surface applicability.
11. Migrate existing tests and add scenario suites for every interaction.

The engine should remain unchanged until the first four domain contracts are agreed, because they determine the shape of every downstream API.
