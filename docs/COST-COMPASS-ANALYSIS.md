# GitHub Copilot Cost Compass

## Refreshed analysis and build plan - Microsoft Hackathon 2026

**Analysis date:** 11 September 2026.
**Original analysis scope:** Analysis and build plan first; application implementation was approved subsequently.
**Reference:** `sujithq/hackathon2026`, commit `413f6f5359719c32ad00301861c3fdae12da3ab8`.
**Design input:** The supplied three-column Cost Compass mockup.
**Official-source retrieval cut-off:** 11 September 2026.
**Document status:** Preserved pre-implementation analysis. See the new version's `README.md` for implemented capabilities and run instructions.
**New version location:** `C:\Users\squintelier\temp\GitHub Copilot Cost Compass`.

This refreshed analysis is stored separately from `PROJECT-ANALYSIS.md` so the original engineering baseline and the newer billing/product assessment remain available. Findings below describe the referenced source revision, not an assertion that subsequent implementation work remains incomplete.

## 1. Recommendation

Build **a focused, explainable what-if experience on the existing .NET engine**, not a replacement simulation engine or a second implementation of its rules in JavaScript.

The product promise should be:

> Estimate the incremental cost of a Copilot workload, identify the first modeled blocker, and compare a safe configuration change before anyone changes production.

Keep the mockup's **Configure. Simulate. Explain. Resolve.** journey. Make the differentiator the explanation and verified before/after comparison, not merely a new price calculator.

For the first hackathon release:

- Target administrators and FinOps users of **Copilot Business and Enterprise**. The current engine's economic path is designed around pooled seats, not complete personal-plan billing.
- Offer fully specified Chat, cloud-agent, and CLI scenarios. Keep code review explicitly illustrative or evidence-driven: GitHub does not publish its chosen review model or a fixed per-review price. Treat Actions charges as a distinct meter on applicable workloads.
- Add a new `compass` route in the existing standalone Blazor WebAssembly host. Preserve the current advanced simulator during development.
- Keep the engine client-neutral, deterministic, and offline. No backend, sign-in, live account discovery, or GitHub settings writes are needed for this release.
- Prioritize trustworthy reference data, faithful decision states, reproducible scenarios, and counterfactual comparisons before adding more features.

**Do not use the old parity specification as the new backlog.** It is useful compatibility evidence, but reproducing every existing control on the first screen would undermine the focused experience in the mockup.

## 2. What actually changed since the previous analysis

The existing project analysis and parity requirements are dated 3 September 2026. The current local checkout is clean and matches upstream `main`.

The 5 September commit adds the analysis, requirements, and demo documents; despite its commit title, its diff does not change the application. Consequently, a fresh assessment should distinguish **external billing/policy changes and reference-data drift** from changes to the implementation.

### Official-source refresh

**The AI-credit foundation is still correct. The reference data and supported-case boundaries need a targeted refresh, not an engine rewrite.**

GitHub's current documentation confirms USD 0.01 per AI credit, token-based usage, pooled Business/Enterprise entitlements, and individual -> cost-center -> universal user-budget precedence. Legacy annual personal subscriptions remain a separate billing regime. [S1] [S3] [S5] [S10]

| Change or newly relevant distinction | Evidence as of 11 September | Impact on Cost Compass |
|---|---|---|
| GPT-5.6 Sol's promotion ended after 3 September. | Current pricing and the 4 September public documentation change establish successor rates, double the promotional rates. [S1] [S2] | Add a sourced successor period. The current engine correctly refuses to price the gap; carrying the old price forward would understate cost by 50%. |
| GPT-6 Astra was announced 4 September; Gemini 3.8 Flash on 3 September. | Astra's announced plans are Pro+, Max, Business, and Enterprise, with gradual rollout. Gemini 3.8 has promotional pricing through 31 December. [S16] [S17] | Update model data and eligibility, not just names. Catalog presence does not prove access for every plan or tenant. |
| MAI-Code-1-Flash retired on 10 September. | The notice recommends MAI-Code-1.1-Flash. [S18] | Remove the old model from current supported presets, retaining historical evidence. A published price does not imply continued availability. |
| Managed agent-operation permissions were announced 9 September. | Deny/ask/allow controls apply to shell, file, and network operations in specified agent surfaces. [S19] | Explain operational denial or approval separately from insufficient credits. Do not invent a financial remedy for a permission block. |
| Business/Enterprise transition credits ended before the current month. | Eligible existing customers received 3,000/7,000 credits per seat during June-August; ordinary September entitlements are 1,900/3,900. [S3] [S4] | Current default amounts are right. Historical eligible-customer scenarios need a qualified promotion, not a global replacement of standard allowances. |
| Auto and compliance adjustments are conditional. | Paid-plan Auto has a 10% model-cost discount on listed experiences. Enforced residency/FedRAMP adds 10% credit consumption and restricts model eligibility. [S7] [S8] [S9] | Do not apply these to every plan, model, or operation. Their combined/stacked treatment was not sufficiently documented to present as verified. |
| Code review has special cost and access behavior. | Model choice is not disclosed; unlicensed reviews can be paid directly by the organization; unavailable Actions can mean a limited review rather than no review. [S13] | The existing generic selected-model/required-seat path is not complete real-world review support. Explicitly limit the first demo or extend the engine. |
| Actions accounting is more than aggregate estimated minutes. | Per-job rounding, hosted-runner class, repository visibility, and the GitHub account plan affect charges. [S14] [S15] | Constrain initial runner cases and label assumptions. Copilot plan allowances must not be reused as Actions allowances. |

### Current plans: list prices are not a complete invoice

Monthly USD list prices and currently documented included credits; organizational prices are per seat. [S3] [S5] [S6]

| Plan | Monthly list price | Included AI credits | Ownership / qualification |
|---|---:|---:|---|
| Free | USD 0 | Limited; no numeric allowance verified in the reviewed page | Individual; do not invent a credit amount. |
| Student | USD 0 | Included; no numeric allowance verified in the reviewed page | Eligibility required. |
| Pro | USD 10 | 1,000 base + 500 flex = 1,500 | Individual; current flex allowance is variable. |
| Pro+ | USD 39 | 3,900 base + 3,100 flex = 7,000 | Individual; not equivalent to Enterprise despite the same price. |
| Max | USD 100 | 10,000 base + 10,000 flex = 20,000 | Individual; current flex allowance is variable. |
| Business | USD 19 | 1,900 per seat | Shared at the billing-entity level. |
| Enterprise | USD 39 | 3,900 per seat | Shared at the billing-entity level. |

Included credits do not roll over. Individual allowances reset at 00:00 UTC on the first calendar day of the month, independently of the subscription invoice date. Do not treat flex credits as permanent contractual guarantees, or a personal subscription as an extra corporate pooled allowance. [S5]

Legacy annual Pro/Pro+ scenarios must not be passed through the token/AI-credit path as though premium requests had a fixed token-equivalent price. They are outside the recommended MVP.

### Concrete model-price refresh

USD per million tokens. These are selected material changes, not a complete replacement catalog. [S1] [S2]

| Model / documented input tier | Fresh input | Cached input | Cache write | Output |
|---|---:|---:|---:|---:|
| GPT-5.6 Sol, <=272K | 4.00 | 0.40 | 5.00 | 20.00 |
| GPT-5.6 Sol, >272K | 8.00 | 0.80 | 10.00 | 30.00 |
| GPT-6 Astra, <=272K | 10.00 | 1.00 | 12.50 | 50.00 |
| GPT-6 Astra, >272K | 20.00 | 2.00 | 25.00 | 75.00 |
| Gemini 3.8 Flash, promotional | 0.75 | 0.075 | Not listed | 3.75 |
| MAI-Code-1.1-Flash | 0.20 | 0.02 | Not listed | 1.20 |

"Not listed" is not a new published zero-price guarantee. Keep unsupported token components explicit.

Sol's standard-price transition is documented at calendar-date granularity. The public documentation commit timestamp is not proof of the precise billing cutover instant or timezone. Keep the source date and any chosen UTC simulation boundary distinguishable.

Track three independent dimensions: **price validity**, **model availability**, and **plan/policy eligibility**. For example, Gemini 3.6 Flash's promotion runs through December, but its announced retirement is earlier, on 2 October. [S1] [S22]

### Announced future changes - not current defaults

| Date | Announced behavior | Correct treatment |
|---|---|---|
| No earlier than 28 September | Unified web/mobile/cloud-agent experience; related policy and architecture changes. [S20] | Do not replace today's Actions-based cloud-agent calculation with invented future Sandbox pricing. |
| 28 September | Balanced becomes the default code-review effort; explicit Lite remains Lite. [S20] | A scheduled scenario, not a claim that every current or explicitly configured review is Balanced. |
| Starting 1 October | Existing Business/Enterprise credit-card/PayPal customers enter the updated billing/payment experience. [S21] | Account/cohort/cycle-sensitive future behavior, not a universal September access gate. |
| 2 October | Scheduled retirements include Gemini 3.5/3.6 Flash, Kimi K2.7 Code, and Claude Opus 4.7. [S22] | Keep current availability and scheduled retirement separate. Do not retire Gemini 3.7 by inference. |

For new card/PayPal customers, the August announcement named a 1 September reopening start; the September update described a gradual rollout. Payment before access for affected new seat assignments is real, but an account's rollout/payment state cannot be established from its plan name alone. These announcements do not establish identical behavior for invoiced or Azure-linked customers. [S20] [S21]

### Billing details to preserve rather than flatten

- A ULB caps total AI usage, including included-pool consumption, and is a hard stop. A monetary spending budget generally governs metered usage; "Stop usage" is not the same as an alert. Individual ULB overrides can expire. [S10] [S11]
- Cost-center included-use caps can matter while the enterprise still has pooled credits. Explicit enterprise-budget exclusions also matter; a single "budget remaining" field loses these distinctions. [S10]
- Copilot costs generally follow the billed user/licensing attribution; Actions follows the repository/account running the workflow. A future complete cost model may therefore need different attributed payers for the two meters. [S12]
- Published review figures are typical estimates, not fixed prices or upper bounds: Lite USD 0.05-1 and Balanced USD 0.25-5, excluding Actions. Do not translate those into guaranteed affordability or a selectable actual review model. [S13]
- Standard public-repository hosted runners and self-hosted execution are free of GitHub Actions minute charges; customer infrastructure cost is separate. Larger hosted runners are chargeable even in public repositories and cannot use included minutes. Each job rounds up independently. [S14] [S15]
- Verified standard rates include Linux x64 2-core USD 0.006/minute, Windows x64 2-core USD 0.010/minute, and macOS USD 0.062/minute. The included monthly Actions minutes come from the GitHub account plan: Free 2,000; Pro/Team 3,000; Enterprise Cloud 50,000. [S14] [S15]
- The proposed self-hosted USD 0.002/minute platform fee was postponed. Do not implement the superseded proposal or apply the already-incorporated hosted-rate reductions a second time. [S23]

## 3. Reuse assessment

| Existing asset | Reuse decision | Important boundary |
|---|---|---|
| `ICopilotUsageSimulationEngine` and the simulation coordinator | Reuse as the source of decisions and calculations. | No pricing, entitlement, attribution, or guardrail arithmetic in the new UI. |
| Effective-dated model prices and plan allowances | Reuse the mechanism; refresh the data. | A structurally valid catalog is not necessarily accurate or current. Current contracts also lack customer-cohort selection for historical promotions. |
| Attribution and economic applicability resolvers | Reuse. | Selected user, effective seat, licensing organization, cost center, entitlement, and controls must remain consistent. |
| Economic allocation, hard-stop/alert-only budgets, and projected balances | Reuse. | Included workload value is different from incremental billed spend. Rejected runs do not commit consumption. |
| `SimulationSessionRunner` | Reuse for explicitly requested repeated runs. | A normal preview must not silently become a consumption-advancing action. |
| Shared guardrail metadata | Reuse and extend where needed. | Stable identifiers and settings anchors must survive the new visual grouping. |
| Existing scenario/editor adapters and blocked examples | Reuse compatible patches and fixtures. | Do not blindly copy automatic execution, balance advancement, or generic code-review assumptions. |
| Browser persistence and JSON validation | Reuse infrastructure. | Current download exports only a scenario; portable evidence needs the matching catalog as well. |
| xUnit, bUnit, and Pages deployment | Extend existing coverage and hosting. | Source-level test coverage is not evidence of a fresh successful execution. |

The implementation already supports six decisions: `Allowed`, `Blocked`, `PartiallySimulated`, `SoftStopped`, `Waiting`, and `Indeterminate`. The new client must not reduce them to a misleading red/green binary.

## 4. Gaps that materially affect the proposed product

These are product-delivery gaps, not a recommendation to rewrite or broadly refactor the solution.

| Priority | Gap and evidence | Required treatment |
|---|---|---|
| P0 | Catalog provenance is absent. The configuration has a version, but no rule-level source, verification timestamp, confidence, or model availability/plan eligibility. Multipliers and runner rates are not effective-dated. [E1] | Add a versioned reference-data manifest and eligibility/effective-date support where required. Distinguish verified facts, explicit assumptions, unsupported cases, and expired data. |
| P0 | `gpt-5.6-sol` has no configured price after the exclusive 4 September boundary. The model selector lists all models, while pricing correctly throws `pricing-not-effective`. [E2, E3] | Add the now-verified successor period described above. Preserve the old promotion and explicit gap errors; price expiry is not model retirement. |
| P0 | Mockup-style pass indicators could misrepresent checks that never ran. Cost-only mode skips access/runtime checks; access checks are not all represented in `AppliedGuardrails`. [E3, E4] | Add a structured, ordered trace projection owned by the engine. Carry applicability and evaluation state explicitly; do not parse prose or synthesize successful checks. |
| P0 | The current main simulation action advances working balances. Repeatedly pressing it is not an immutable what-if preview. [E5] | Make **Simulate** preview-only in Cost Compass. Keep **Advance simulated usage** or repeated execution explicit, preserving the existing advanced workflow. |
| P0 | Code review follows generic billed-user/selected-model/Actions-stop logic. Actions uses aggregate minutes and does not model every public/large-runner exception or per-job rounding. [E3] | Limit the supported first-release cases and label assumptions. Full review billing, unlicensed users, fallback reviews, and broader runner accounting require engine work before authoritative-looking results are advertised. |
| P1 | Individual plans are present in the catalog, but the entitlement calculator explicitly excludes non-pooled plans. [E1, E6] | Limit the first experience to Business/Enterprise. Personal subscriptions require a separate entitlement path and dedicated acceptance cases, not just another dropdown choice. |
| P1 | Standalone GitHub Actions is not an existing operation. Unbilled operations return before Actions pricing. [E2, E3] | Defer the independent Actions preset, or implement independent AI/Actions meter sequencing in the engine. Adding a card and a catalog row is insufficient. |
| P1 | Recommendations are currently metadata, explanations, and settings links, not a validated remediation/comparison contract. [E4, E7] | Add reusable counterfactual evaluation. A proposed change counts as a resolution only after the engine re-simulates it and exposes any next blocker. |
| P1 | The cost result does not include subscription/seat fees, tax, currency conversion, or invoice reconciliation. Actions pricing is aggregate minutes times a runner rate after allowance. [E1, E3, E4] | Label results as modeled incremental usage, not a complete GitHub bill. Constrain runner support until rounding, allowance eligibility, and other billing rules are covered. |
| P1 | Presets use the current UTC timestamp. Local save includes catalog and preferences, but exported JSON does not bundle the catalog. [E8] | Pin the demo timestamp and export a versioned scenario/catalog/reference bundle. Distinguish the simulation date from the source verification date. |
| P1 | The plan allowance contract selects by date and plan only; billing inputs have no promotional-customer cohort. [E1, E11] | September standard-allowance scenarios can proceed. For historical promotion support, use explicit qualified snapshots or add an eligibility-aware entitlement contract; do not grant the June-August promotion to everyone. |

P0 means necessary for a trustworthy first demo; P1 means required for the corresponding advertised capability or explicitly deferred from release.

## 5. How to adapt the mockup

### Left: Preset scenarios

Keep the compact scenario cards, selected state, and common/advanced grouping. Separate the **workload** from the **problem preset**: for example, Chat plus "Paid usage disabled" rather than encoding every combination as a new operation.

| Mockup card | Current support | First-release recommendation |
|---|---|---|
| Copilot Chat | Existing operation and standard preset. | Include. |
| Coding agent | Existing `cloud-agent` operation and Actions integration. | Include explicit private-repository/standard-runner cases first; distinguish cloud agent from local IDE agent. |
| Code review | Generic token calculation and visibility-dependent Actions metering, not all real review behavior. | Include only an explicitly illustrative/evidence-driven explanation. Do not imply a known actual model, a guaranteed price, or complete unlicensed/fallback support. |
| Copilot CLI | Existing `cli` operation, but not a top-level labeled preset. | Add a catalog-driven preset and reuse the same engine. |
| GitHub Actions | Runner charging exists only within the current Copilot pipeline. | Defer the standalone card until independent metering is implemented. Do not present a nonfunctional placeholder as a completed feature. |

Retain the six existing cost-blocked examples as reusable demonstration material: user budget, included-use overflow, paid-usage applicability, paid usage disabled, AI spending budget, and Actions spending budget.

### Middle: Configure environment

Use progressive disclosure rather than copying the complete JSON contract into the visible form.

Show the operation, billing user, plan, model, cost center, relevant allowances, and estimated workload first. Expand advanced policy, attribution, token-component, and budget controls on demand.

Important corrections:

- **Enterprise / organization / repository are not interchangeable billing scopes.** Show the selected user and the resolved billing attribution. Repository visibility affects supported Actions cases; it does not by itself determine who pays for Copilot.
- Label gates as **supplied scenario assumptions**, not discovered live settings. Distinguish seat assigned, payment applicability/readiness, feature eligibility, and unknown account rollout. "Licensed" must not imply these have been verified.
- Replace ambiguous `1,200 / 10,000` displays with explicit **used**, **remaining**, **limit**, and units.
- Separate user-level AI-credit limits, shared included credits, included-use controls, and USD spending budgets. They are different controls.
- Show model rates effective for the simulation timestamp and separately check availability/eligibility. Use current catalog-driven model names, not the mockup's fixed GPT-4o/Premium terminology. A code-review model assumption is not a supported real-service model selector.
- A "Medium" agent workload must map to visible assumptions about calls, token components, context, and runtime. It is not a guaranteed cost inferred from a natural-language task.
- Hide irrelevant Actions inputs for Chat/CLI. Do not imply that every Copilot task incurs runner minutes.

### Right: Simulation result

Keep the prominent decision, concise first-blocker explanation, detailed trace, and resolution actions.

Present:

1. The modeled decision and first failing gate.
2. AI credits required, included allocation, additional AI charge, and separate Actions charge.
3. The evaluated trace in actual engine order, with expandable categories.
4. The relevant entity, control, limit, prior consumption, requested amount, and projected balance or shortfall.
5. Counterfactual actions with before/after cost and decision.
6. The scenario date, reference snapshot, source links, and assumptions.

Use **Not evaluated** after a terminal failure, **Not applicable** for irrelevant checks, and **Not included in this simulation** for cost-only exclusions. Reserve **Waiting** for an actual modeled wait, such as approval. Do not draw later stages as passed.

The mockup's fixed six rows must not replace the engine's order. Current sequencing includes attribution/seat validation, runtime preflight, Actions access, catalog access, token pricing, runtime credit checks, economics, then Actions spending approval. Some stages are conditional.

This is the **simulator's documented order**, not a claim to have reverse-engineered GitHub's internal request-processing order. For a partially supported feature, "insufficient evidence" is more honest than a precise-looking live-service verdict.

### Correct the blocked example

**Paid usage disabled does not, by itself, block a request covered entirely by included credits and applicable limits.** The engine only evaluates paid authorization when metered credits are needed. [E9]

If the mockup's `1,200 / 10,000` means 1,200 credits used and ample included allowance remains, its blocked banner needs additional evidence: a request exceeding the remaining allowance, an exhausted included-use control permitting paid overflow, or another genuine first failure.

Also, enabling paid usage cannot guarantee success if the USD budget is already exhausted. Re-simulation may reveal that budget as the next blocker.

The action label should be **Preview enabling paid usage**, not a promise to change a real enterprise setting. Never default to raising spend merely because it removes a block.

## 6. Recommended architecture

Retain the current project boundaries and introduce focused capabilities, not a new platform.

| Boundary | Responsibility in Cost Compass |
|---|---|
| `CopilotUsageSimulator.Common` | Stable metadata, documentation links, and shared identifiers. |
| `CopilotUsageSimulator.Engine` | Pricing, attribution, entitlements, ordered evaluation, trace contracts, counterfactual evaluation, and any new independent-meter behavior. No browser or network dependencies. |
| Reference-data loading boundary | Load validated, versioned, source-backed catalog snapshots. Retrieval or refresh happens outside request simulation. |
| `CopilotUsageSimulator.Web` | New page layout, view state, form composition, accessibility, browser persistence, imports/exports, and presentation. |
| Existing test projects | Preserve current contracts and add explicit tests for approved behavior changes. |

**Suggested first implementation:** `Pages\Compass.razor` plus focused preset, environment, decision-trace, and comparison components, backed by a small page coordinator reusing existing adapters.

A separate `CopilotCostCompass.Web` project is justified later if it needs independent release or ownership. For this hackathon, another host would add deployment and state-management work without improving the core demonstration.

Do not port the engine into React/TypeScript simply to recreate the mockup. Keep its composition and information hierarchy; refine visual tokens and responsive behavior in the existing client.

### Minimal reusable additions

**Reference snapshot:** Catalog identity/hash, verification timestamp, source URLs, documented effective periods, eligibility, assumptions, and supported billing regime. Do not backdate a newly retrieved rate to the start of usage-based billing unless a source supports that period.

For the smallest first release, restrict the default snapshot to supported September standard-allowance cases. Historical promotions and future cohort-dependent payment rules should be separate explicit scenario profiles, not silent defaults.

**Structured trace:** Stable check ID, stage/order, outcome, applicability/evaluation state, selected entity, explanatory metadata, and quantitative evidence. Preserve existing `FirstFailingGate` and explanation contracts.

**Counterfactual comparison:** Immutable baseline, explicit proposed change, re-evaluated result, cost/balance delta, assumptions, and unresolved blockers. Never treat a successful local preview as authorization to change GitHub.

**Portable scenario bundle:** Schema version, engine/reference identity, complete scenario, matching catalog, simulation timestamp, and optional result snapshot. Keep old scenario-only imports supported with an explicit active-catalog warning.

## 7. Delivery plan

| Order | Work package | Main ownership | Dependency / exit condition |
|---|---|---|---|
| 1 | Establish the dated reference snapshot and supported-case matrix: Sol successor prices, current additions/retirements, qualified modifiers, and constrained review/runner cases. | Reference data, Engine, Common | No preset uses guessed, expired, unavailable, or known-ineligible data. Every supported rule has a source or visible assumption. |
| 2 | Make results presentation-safe: structured trace, explicit unsupported/unpriced handling, and preview-only execution for the new client. | Engine, Web | First-failure semantics remain stable; skipped checks cannot appear passed; preview leaves inputs unchanged. |
| 3 | Build the three-column experience, three fully specified workload presets, and clearly qualified code-review content. Preserve the advanced route. | Web | Configure -> simulate -> explain works with keyboard and narrow screens; unsupported cases cannot masquerade as supported estimates. |
| 4 | Add verified counterfactuals and before/after comparison for the highest-value blockers. | Engine, Web | Candidate fixes run through the engine, show new blockers, and leave the baseline untouched. |
| 5 | Add portable evidence bundles, pinned demos, integration coverage, and refreshed user/demo documentation. | Web, tests, docs | Another browser reproduces the scenario with the same reference snapshot; demo requires no live account or service. |
| Later | Individual-plan billing, complete review attribution/fallback, standalone Actions, historical cohorts, forecasts, or read-only live adapters. | Engine plus host-specific adapters | Each extension has sourced semantics and separate acceptance coverage before being advertised. |

Effort is driven primarily by reference-data and contract work, not CSS. Calendar estimates should follow the team's size and hackathon timebox; no delivery duration is assumed here.

Do not add live administration, autonomous spending changes, a billing database, or an LLM-generated price oracle to this release.

## 8. Acceptance criteria

| ID | Required outcome |
|---|---|
| CC-01 | Every displayed estimate identifies its simulation date, catalog snapshot, and source verification date. Expired or unsupported pricing produces an explicit non-estimate, not zero cost. |
| CC-02 | Re-running an unchanged preview returns the same result and leaves the baseline scenario and balances unchanged. Explicit repeated runs preserve existing advancement semantics. |
| CC-03 | The primary blocker is exactly the engine's first terminal gate. Later checks are not presented as evaluated. Cost-only results never imply that access or runtime was verified. |
| CC-04 | A fixture requiring 100 credits with 60 included credits available and paid usage disabled is blocked; the same fully included 60-credit fixture is not blocked by paid authorization alone. Other limits are held permissive. |
| CC-05 | For that synthetic fixture, enabling paid usage requires 40 metered credits, or USD 0.40 at USD 0.01 per credit. A USD 0.20 hard-stop budget then becomes a blocker. This is test arithmetic, not a quoted real-model rate. |
| CC-06 | A rejected run or rejected candidate changes no consumption and emits no accepted-charge budget alerts. An alert-only budget does not become a hard stop in the UI. |
| CC-07 | Changing the selected user/plan/cost center updates seat and attribution inputs consistently. Unrelated advanced records survive guided edits. |
| CC-08 | AI-credit charges and Actions charges are distinct. Public/private behavior and supported runner eligibility are tested against the chosen official reference snapshot. |
| CC-09 | Historical dates select the appropriate historical rate/allowance or explicitly report lack of coverage. Boundary tests cover inclusive starts and exclusive ends. |
| CC-10 | A recommendation is shown as resolving a blocker only after re-simulation. Cost-reducing advice must not claim that a positive-cost workload fits an exhausted zero-credit allowance. |
| CC-11 | Import/export reproduces the catalog and scenario together. Invalid or unsupported bundles preserve the user's existing state and show a useful error. |
| CC-12 | All actions are keyboard-accessible; status is communicated through text and icons as well as color; controls have associated labels; the layout works at 200% zoom and on a narrow viewport. |
| CC-13 | The supported demo completes without GitHub credentials, changing cloud settings, executing real Copilot work, or sending scenario data to a server. |
| CC-14 | Sol's otherwise identical token fixture costs twice as much under the documented post-promotion rate as under the promotion. Date-boundary assumptions are explicit rather than inferred from a documentation commit timestamp. |
| CC-15 | Availability, price validity, and eligibility are independent: retired MAI-Code-1-Flash is not offered for a current scenario; a scheduled retirement is not applied early; unsupported plan/model combinations do not receive a price-only approval. |
| CC-16 | Auto's discount is restricted to documented plans and experiences. Residency/FedRAMP model restrictions are evaluated as well as the uplift; unsupported combinations are qualified, not asserted as verified billing. |
| CC-17 | Historical eligible-customer August allowances differ from standard allowances. September amounts remain standard. Unknown promotional eligibility is not silently promoted to eligibility. |
| CC-18 | A review's typical published cost range is never labeled a guaranteed maximum. Unlicensed and limited-review cases are either correctly modeled or explicitly unsupported, never generalized from the ordinary required-seat path. |
| CC-19 | Per-job Actions minute rounding is covered for advertised job-level estimates. If the first version accepts already-accounted minutes instead, the contract and UI explicitly say so and do not claim raw-runtime accuracy. |
| CC-20 | September 28/October changes are labeled announced future scenarios. Unknown payment/rollout state does not become a fabricated passed gate for September 11. |

Keep regression tests for existing catalog validation, attribution ambiguity, selected-seat identity, ULB precedence, included/metered splitting, hard-stop versus alert-only budgets, economic-before-Actions failure, and terminal-state balance preservation.

During implementation, use the smallest matching test project first, then the full Release suite for shared-contract/engine changes and the existing Pages deployment path. Add real-browser checks for the new interaction flow rather than assuming bUnit alone proves it.

## 9. Two-minute demo

**0:00-0:20 - Configure:** Open a pinned Business/Enterprise Chat or cloud-agent scenario. Show the billing user, model, remaining included credits, and reference date.

**0:20-0:50 - Simulate and explain:** Run a fixture with insufficient included allowance and paid usage disabled. Point to the exact first blocker, requested credits, and unchanged balances.

**0:50-1:25 - Resolve safely:** Preview a smaller workload that actually fits the remaining allowance, or preview enabling paid usage. Show the before/after result and incremental cost. If another limit blocks it, display that rather than claiming success.

**1:25-1:45 - Show the second meter:** Switch to the supported private-repository cloud-agent example and point out separate Actions charges and runner assumptions.

**1:45-2:00 - Reproduce:** Export the scenario/reference bundle. Close with: "We changed a simulation, not production. The same deterministic engine explains every result."

A useful success measure is whether a first-time viewer can identify the payer, the first blocker, the incremental cost, and a validated alternative without opening JSON.

## 10. Evidence and limitations

### Repository evidence

Line references apply to commit `413f6f5359719c32ad00301861c3fdae12da3ab8`.

| ID | Repository-relative evidence |
|---|---|
| E1 | `src\CopilotUsageSimulator.Engine\Configuration\EngineConfiguration.cs:3-14,27-38,95-107` - catalog, plan, multiplier, and runner contracts. |
| E2 | `src\CopilotUsageSimulator.Engine\Configuration\default-catalog.json:13-34,53-69,116-122` - plans, operations, modifiers, runners, and expired Sol promotion. |
| E3 | `src\CopilotUsageSimulator.Engine\CopilotUsageSimulationEngine.cs:24-216,219-248,251-356` - actual pipeline, early return for unbilled operations, pricing coverage errors, and aggregate Actions arithmetic. |
| E4 | `src\CopilotUsageSimulator.Engine\Simulation\SimulationResult.cs:5-30,54-100` - decisions, allocation, trace, and projected-state contracts. |
| E5 | `src\CopilotUsageSimulator.Web\Services\HomePageModel.cs:167-177,390-403`; `src\CopilotUsageSimulator.Engine\Simulation\SimulationSessionRunner.cs:7-34` - preview versus balance advancement. |
| E6 | `src\CopilotUsageSimulator.Engine\Guardrails\EconomicBalanceCalculator.cs:269-297` - only pooled plans contribute entitlement. |
| E7 | `src\CopilotUsageSimulator.Common\Guardrails\GuardrailMetadata.cs:39-47,97-123`; `src\CopilotUsageSimulator.Web\Shared\SimulationResults.razor:29-57` - metadata-driven explanations and settings links. |
| E8 | `src\CopilotUsageSimulator.Web\Services\ExampleScenarioFactory.cs:14-35`; `src\CopilotUsageSimulator.Web\Services\BrowserScenarioPersistence.cs:9-29,70-80` - current-time presets, local envelope, and scenario-only export. |
| E9 | `src\CopilotUsageSimulator.Engine\Guardrails\EconomicGuardrailEvaluator.cs:192-272` - included allocation, paid-usage gating only for metered credits, and later spending budgets. |
| E10 | `tests\CopilotUsageSimulator.Engine.Tests\SimulationEngineTests.cs`; `tests\CopilotUsageSimulator.Engine.Tests\GuardrailEngineTests.cs`; `tests\CopilotUsageSimulator.Web.Tests\HomeTests.cs`; `.github\workflows\deploy-pages.yml` - existing regression surfaces and static hosting. |
| E11 | `src\CopilotUsageSimulator.Engine\Guardrails\BillingAndAttribution.cs:3-18`; `src\CopilotUsageSimulator.Engine\Configuration\EngineConfiguration.cs:27-39` - billing/seat and allowance inputs lack a promotional-customer cohort dimension. |

### Official sources

Live documentation was retrieved on 11 September 2026. A retrieval date is not a publication date or an effective date. Dated announcements and the specific Sol documentation revision support the temporal claims.

| Ref | Official source | Use in this analysis |
|---|---|---|
| S1 | [Models and pricing][S1] | Current token rates, credit value, context tiers, and promotions. |
| S2 | [4 September Sol pricing documentation change][S2] | Immutable evidence of the promotional-to-standard rate update. |
| S3 | [Organization and enterprise billing][S3] | Pooling, current allowances, and historical promotion. |
| S4 | [Usage-based billing migration announcement][S4] | Published 27 April; June migration and existing-customer promotion. |
| S5 | [Individual billing][S5] | Base/flex allowances, resets, and individual/legacy distinctions. |
| S6 | [Copilot plans][S6] | Monthly list prices and plan differentiation. |
| S7 | [Auto model selection][S7] | Paid-plan discount and qualifying experiences. |
| S8 | [Copilot with data residency][S8] | Conditional enforcement, regional model eligibility, and uplift. |
| S9 | [FedRAMP models][S9] | Conditional US enforcement and model eligibility. |
| S10 | [Organization and enterprise budgets][S10] | ULB precedence, included-use controls, spending, and exclusions. |
| S11 | [Budgets and alerts][S11] | Alert versus stop behavior and tracking limits. |
| S12 | [Cost-center allocation][S12] | Different Copilot and Actions attribution. |
| S13 | [Code review][S13] | Cost uncertainty, effort levels, unlicensed users, and limited review. |
| S14 | [GitHub Actions billing][S14] | Allowances, runner/account distinctions, and free usage. |
| S15 | [Actions runner pricing][S15] | Rates, larger runners, and per-job rounding. |
| S16 | [GPT-6 Astra announcement][S16] | Published 4 September; eligibility and gradual availability. |
| S17 | [Gemini 3.8 Flash announcement][S17] | Published 3 September; model addition and promotion. |
| S18 | [MAI-Code-1-Flash retirement][S18] | Published/effective 10 September. |
| S19 | [Managed agent-operation permissions][S19] | Published 9 September; operational controls distinct from spend. |
| S20 | [Upcoming Copilot policy and billing changes][S20] | Published 28 August; September rollout and scheduled later changes. |
| S21 | [Reopening Business/Enterprise sign-ups][S21] | Published 3 September; gradual rollout and October customer cohort. |
| S22 | [Upcoming selected-model retirements][S22] | Published 3 September; scheduled 2 October retirements. |
| S23 | [Updated Actions pricing announcement][S23] | Corrected status of the proposed self-hosted fee. |

[S1]: https://docs.github.com/en/copilot/reference/copilot-billing/models-and-pricing
[S2]: https://github.com/github/docs/commit/9b446626f08f19ad2aa81538373d0d070b9a4abf
[S3]: https://docs.github.com/en/copilot/concepts/billing-and-usage/organizations-and-enterprises/billing
[S4]: https://github.blog/news-insights/company-news/github-copilot-is-moving-to-usage-based-billing/
[S5]: https://docs.github.com/en/copilot/concepts/billing-and-usage/individuals/billing
[S6]: https://docs.github.com/en/copilot/get-started/plans
[S7]: https://docs.github.com/en/copilot/concepts/models/auto-model-selection
[S8]: https://docs.github.com/en/enterprise-cloud@latest/admin/data-residency/github-copilot-with-data-residency
[S9]: https://docs.github.com/en/copilot/concepts/enterprise/fedramp-models
[S10]: https://docs.github.com/en/copilot/concepts/billing-and-usage/organizations-and-enterprises/budgets
[S11]: https://docs.github.com/en/billing/concepts/budgets-and-alerts
[S12]: https://docs.github.com/en/billing/reference/cost-center-allocation
[S13]: https://docs.github.com/en/copilot/concepts/agents/code-review
[S14]: https://docs.github.com/en/billing/concepts/product-billing/github-actions
[S15]: https://docs.github.com/en/billing/reference/actions-runner-pricing
[S16]: https://github.blog/changelog/2026-09-04-gpt-6-astra-is-generally-available-in-github-copilot/
[S17]: https://github.blog/changelog/2026-09-03-gemini-3-8-flash-is-now-available-in-github-copilot/
[S18]: https://github.blog/changelog/2026-09-10-mai-code-1-flash-deprecated/
[S19]: https://github.blog/changelog/2026-09-09-enterprise-managed-permissions-for-github-copilot-agent-operations/
[S20]: https://github.blog/changelog/2026-08-28-upcoming-changes-to-github-copilot-policies-and-billing/
[S21]: https://github.blog/changelog/2026-09-03-reopening-copilot-business-and-enterprise-signups/
[S22]: https://github.blog/changelog/2026-09-03-upcoming-deprecation-of-selected-github-copilot-models/
[S23]: https://github.blog/changelog/2025-12-16-coming-soon-simpler-pricing-and-a-better-experience-for-github-actions/

### Unresolved public-evidence questions

Do not turn these into silently hard-coded rules:

- The exact combined treatment of Auto plus compliance enforcement, or stacking multiple compliance uplifts.
- Every customer's promotion/payment eligibility, rollout status, and proration formula.
- Exact model cutover timestamps where only calendar dates are documented.
- A successor Gemini promotional price after 31 December, a fixed per-review price, or future Sandbox pricing.
- Universal AI-credit rounding and guaranteed concurrency/overshoot behavior.
- Budget-document simplifications around overlapping scopes and "any zero budget," versus the separately documented metered-only phases and exclusions.

For supported assumptions, preserve the existing deterministic modeled behavior and expose the assumption. Do not silently reinterpret the engine's ordered rules as verified live enforcement.

### Limits of this analysis

This is a product and architecture refresh, not a live billing reconciliation, full security audit, or claim of exact GitHub enforcement behavior. Source and test inventory were inspected; application compilation and test execution are outside this approved analysis-only pass.

At the time of this analysis, no application, catalog, deployment, or live GitHub setting had been changed. Subsequent implementation is confined to the separate new-version directory above. The previous repository documents remain intact.
