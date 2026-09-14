# GitHub Copilot — Complete Analysis: Consumption Billing, Policies, and Everything That Blocks a Request

**Compiled 31 August 2026.** Every rule, setting, and number below carries the GitHub documentation URL that states it. Items GitHub does not publish are explicitly marked **UNVERIFIED** rather than guessed.

---

## 0. Read this first — the model changed on 1 June 2026

If you are working from knowledge of "premium requests" and "model multipliers", that model is **legacy** and applies only to a shrinking set of annual subscribers.

| Concept | Before 1 Jun 2026 | Now |
|---|---|---|
| Unit of consumption | Premium Request Unit (PRU) | **GitHub AI Credit** — 1 credit = **$0.01 USD** |
| How it is counted | 1 per user prompt × model multiplier | **Per token** — input, output, cached input, cache write |
| Tool calls in an agent loop | Not counted | **Every model call is billed** |
| Out of quota | Fall back to a cheaper included model | **No fallback. Blocked.** |
| Coding agent name | Copilot coding agent | **Copilot cloud agent** |

Primary sources:
- https://docs.github.com/en/copilot/reference/copilot-billing/models-and-pricing
- https://github.blog/news-insights/company-news/github-copilot-is-moving-to-usage-based-billing/ (27 Apr 2026)
- Legacy appendix: https://docs.github.com/en/copilot/reference/copilot-billing/request-based-billing-legacy/copilot-requests

> "Fallback experiences will no longer be available. Today, users who exhaust PRUs may fall back to a lower-cost model and continue working. Under the new model, usage will instead be governed by available credits and admin budget controls."
> — https://github.blog/news-insights/company-news/github-copilot-is-moving-to-usage-based-billing/

### Three dated changes that land in the next 30 days

| Date | Change | Impact | Source |
|---|---|---|---|
| **1 Sep 2026** | Promotional included credits end. Business 3,000 → **1,900**, Enterprise 7,000 → **3,900** | Pool drops **−36.7% / −44.3%** overnight at 00:00:00 UTC. **No grace period, no ramp-down, and no transition notice is documented.** | https://docs.github.com/en/copilot/concepts/billing/usage-based-billing-for-organizations-and-enterprises |
| **1 Sep 2026** | Business/Enterprise self-serve signups reopen with strengthened vetting. **"All new Copilot Business or Copilot Enterprise seat assignments will require payment for each seat before users gain Copilot access."** Extends to existing card/PayPal customers **1 Oct 2026** | New blocking cause: unpaid seat = no access | https://github.blog/changelog/2026-08-28-upcoming-changes-to-github-copilot-policies-and-billing/ |
| **28 Sep 2026** | (a) Copilot Chat on github.com, Chat in Mobile, and cloud agent merge into **one unified experience with a single policy**, enabled by default. **"If you opt out of the unified experience, you or your teams will LOSE ACCESS to Copilot on github.com and GitHub Mobile."** Chat data retention goes from 28 days → **life of the account**. (b) Code review effort **Default** silently flips **Lite → Balanced** (~5× credit cost) | Two silent cost/access changes | same changelog |

**Compounding risk for September:** the pool shrinks 37–44%, code review gets ~5× more expensive, "Stop usage when budget limit is reached" is **off by default**, and there is no longer any cheap-model fallback. Orgs that never configured budgets will see silent overage; orgs that did will see users blocked far earlier than in June–August.

---

# PART A — The consumption model

## A1. What is and is not billed

### Billed in AI credits
> "Copilot features that use AI models consume AI credits, such as: Copilot Chat · Copilot CLI · Copilot cloud agent · Copilot Spaces · Spark · Third-party coding agents"
> — https://docs.github.com/en/copilot/concepts/billing/usage-based-billing-for-individuals

| Surface | Billed? | Notes | Source |
|---|---|---|---|
| Code completions (inline) | **NO** | Unlimited on all paid plans; 2,000/mo on Free | https://docs.github.com/en/copilot/reference/copilot-billing/models-and-pricing |
| Next edit suggestions | **NO** | Unlimited on paid plans | same |
| Commit message generation | **NO** | Utility model | https://docs.github.com/en/copilot/concepts/models/utility-models |
| Chat session title generation | **NO** | Utility model | same |
| Copilot Chat (all surfaces) | **YES** | | https://docs.github.com/en/copilot/concepts/billing/usage-based-billing-for-individuals |
| Agent mode in IDE | **YES** | Multiple model calls per task | same |
| Copilot CLI | **YES** | | same |
| Copilot cloud agent | **YES** | **+ GitHub Actions minutes** | https://docs.github.com/en/copilot/concepts/agents/cloud-agent/about-cloud-agent |
| Copilot code review | **YES** | **+ Actions minutes** on private repos | https://docs.github.com/en/copilot/reference/copilot-billing/models-and-pricing |
| Copilot Spaces | **YES** | | https://docs.github.com/en/copilot/concepts/billing/usage-based-billing-for-organizations-and-enterprises |
| Spark | **YES** | Separate SKU: "Spark AI credits" | https://docs.github.com/en/billing/how-tos/set-up-budgets |
| Third-party coding agents (Codex, Claude) | **YES** | No separate meter | https://docs.github.com/en/copilot/concepts/billing/usage-based-billing-for-individuals |
| Agent apps | **YES** | "consume AI credits in the same way as Copilot cloud agent" | https://docs.github.com/en/copilot/concepts/agents/agent-apps |
| MCP servers / skills / custom agents | Not separately billed | But they **inflate input tokens on every request** | https://docs.github.com/en/copilot/tutorials/optimize-ai-usage |
| GitHub Copilot app | Not explicitly listed | Eligible for the auto-model discount, implying it bills — **UNVERIFIED** | https://docs.github.com/en/copilot/concepts/models/auto-model-selection |
| PR summaries | **UNVERIFIED** | Absent from every billing list | — |
| Copilot Extensions | **UNVERIFIED** | Term no longer appears in current docs; superseded by agent apps + MCP | — |
| Knowledge bases | **UNVERIFIED** | Superseded by Copilot Spaces | — |

### Utility models — a free class most people don't know about
> "Utility models are a small set of models that are automatically enabled for all GitHub Copilot users across every plan… **Cannot be disabled by organization or enterprise administrators, except by disabling Copilot completely. Do not consume premium request units or tokens for usage-based billing, and do not appear as a billed line item in usage reports. Are subject to per-user rate limits.**"
> Current utility models: **GPT-4o mini, GPT-4o, GPT-4.1, GPT-5.4 nano**
> — https://docs.github.com/en/copilot/concepts/models/utility-models

## A2. Allowances by plan

**Source:** https://docs.github.com/en/copilot/get-started/plans · https://docs.github.com/en/copilot/concepts/billing/usage-based-billing-for-individuals · https://docs.github.com/en/copilot/concepts/billing/usage-based-billing-for-organizations-and-enterprises

| Plan | Price/mo | Base credits | Flex | **Total credits** | Models |
|---|---|---|---|---|---|
| Copilot Free | $0 | "an allowance" (number **not published**) | — | — | Auto selection only; **2,000 completions/mo** |
| Copilot Student | $0 | "an allowance" (**not published**) | — | — | Auto only; **unlimited completions**; excludes third-party agents |
| Copilot Pro | $10 | 1,000 | 500 | **1,500** | A selection of models |
| Copilot Pro+ | $39 | 3,900 | 3,100 | **7,000** | Premium models |
| Copilot Max | $100 | 10,000 | 10,000 | **20,000** | Priority access |
| Copilot Business | $19/seat | — | — | **1,900 per user** (pooled) | Premium models |
| Copilot Enterprise | $39/seat | — | — | **3,900 per user** (pooled) | Priority access |

**Org/enterprise credits are pooled at the billing-entity level** — 100 Business users share one 190,000-credit pool. Adding licences mid-cycle raises the pool immediately; removing licences does **not** shrink it until the next cycle.

### Reset rules — three traps
> "Included AI credits do not carry over between months. Unused credits are forfeited, and your allowance resets to the full monthly amount at **00:00:00 UTC on the first day of each calendar month**. This reset date is fixed and does not change based on your subscription billing date."
> — https://docs.github.com/en/copilot/concepts/billing/usage-based-billing-for-individuals

> "**Paying for, renewing, upgrading, downgrading, converting from a trial, or resuming a plan after a lapse does not grant a fresh AI credits allowance immediately.**… if you exhaust your AI credits on May 28 and renew or upgrade your plan on May 30, your allowance does not reset until June 1."
> — https://docs.github.com/en/copilot/reference/copilot-billing/license-changes

> "Additional seats are billed on a prorated basis… **Included AI credits may also be prorated.**" — same URL

**Timezone:** billing cycle is **UTC**. "if your cycle ends at 11:59 PM UTC, canceling a seat at 7:00 PM EST (which is 12:00 AM UTC) will fall into the next cycle, and you will be charged for that seat." — https://docs.github.com/en/copilot/reference/copilot-billing/billing-cycle

## A3. Full per-token price table

**Single source: https://docs.github.com/en/copilot/reference/copilot-billing/models-and-pricing**
All prices are **USD per 1 million tokens**. Divide by 0.01 for AI credits (e.g. $2.50 = 250 credits per 1M tokens).

### OpenAI
> "GPT-5.6 Sol, GPT-5.6 Terra, and GPT-5.6 Luna include a cache write cost in addition to cached input. Earlier OpenAI models have no cache write cost."

| Model | Category | Tier | Threshold | Input | Cached in | Cache write | Output |
|---|---|---|---|---|---|---|---|
| GPT-5 mini | Lightweight | Default | — | $0.25 | $0.025 | — | $2.00 |
| GPT-5.3-Codex | Powerful | Default | — | $1.75 | $0.175 | — | $14.00 |
| GPT-5.4 | Versatile | Default | ≤272K | $2.50 | $0.25 | — | $15.00 |
| GPT-5.4 | Versatile | Long ctx | >272K | $5.00 | $0.50 | — | $22.50 |
| GPT-5.4 mini | Lightweight | Default | — | $0.75 | $0.075 | — | $4.50 |
| GPT-5.4 nano | Lightweight | Default | — | $0.20 | $0.02 | — | $1.25 |
| GPT-5.5 | Powerful | Default | ≤272K | $5.00 | $0.50 | — | $30.00 |
| GPT-5.5 | Powerful | Long ctx | >272K | $10.00 | $1.00 | — | $45.00 |
| GPT-5.6 Luna | Lightweight | Default | ≤200K | $0.20 | $0.02 | $0.25 | $1.20 |
| GPT-5.6 Luna | Lightweight | Long ctx | >200K | $0.40 | $0.04 | $0.50 | $1.80 |
| GPT-5.6 Sol ᴾ | Powerful | Default | ≤272K | $2.00 | $0.20 | $2.50 | $10.00 |
| GPT-5.6 Sol ᴾ | Powerful | Long ctx | >272K | $4.00 | $0.40 | $5.00 | $15.00 |
| GPT-5.6 Terra | Versatile | Default | ≤272K | $2.00 | $0.20 | $2.50 | $12.00 |
| GPT-5.6 Terra | Versatile | Long ctx | >272K | $4.00 | $0.40 | $5.00 | $18.00 |

ᴾ **GPT-5.6 Sol is at 50%-off promotional pricing through 3 September 2026.** Post-promo standard rates are not stated numerically — doubling is INFERRED, not documented.

### Anthropic
> "Anthropic models include a cache write cost in addition to cached input." No long-context tier is published for any Claude model.

| Model | Category | Input | Cached in | Cache write | Output |
|---|---|---|---|---|---|
| Claude Haiku 4.5 | Versatile | $1.00 | $0.10 | $1.25 | $5.00 |
| Claude Sonnet 4 | Versatile | $3.00 | $0.30 | $3.75 | $15.00 |
| Claude Sonnet 4.5 | Versatile | $3.00 | $0.30 | $3.75 | $15.00 |
| Claude Sonnet 4.6 | Versatile | $3.00 | $0.30 | $3.75 | $15.00 |
| Claude Sonnet 5 | Versatile | $2.00 | $0.20 | $2.50 | $10.00 |
| Claude Opus 4.5 | Powerful | $5.00 | $0.50 | $6.25 | $25.00 |
| Claude Opus 4.6 | Powerful | $5.00 | $0.50 | $6.25 | $25.00 |
| Claude Opus 4.7 | Powerful | $5.00 | $0.50 | $6.25 | $25.00 |
| Claude Opus 4.8 | Powerful | $5.00 | $0.50 | $6.25 | $25.00 |
| Claude Opus 5 | Powerful | $5.00 | $0.50 | $6.25 | $25.00 |
| Claude Opus 4.8 (fast mode) | Powerful | $10.00 | $1.00 | $12.50 | $50.00 |
| Claude Fable 5 | Powerful | $10.00 | $1.00 | $12.50 | $50.00 |

⚠️ **Gap:** Sonnet 4.6, Sonnet 5, Opus 4.6/4.7/4.8/5 and Fable 5 all support a **1M-token context window** (https://docs.github.com/en/copilot/reference/ai-models/supported-models#models-with-extended-capabilities), yet **no long-context tier is published for them**. How 1M-context Claude usage prices is **UNVERIFIED**.

### Google
| Model | Category | Tier | Threshold | Input | Cached in | Output |
|---|---|---|---|---|---|---|
| Gemini 3.1 Pro (preview) | Powerful | Default | ≤200K | $2.00 | $0.20 | $12.00 |
| Gemini 3.1 Pro (preview) | Powerful | Long ctx | >200K | $4.00 | $0.40 | $18.00 |
| Gemini 3.5 Flash | Lightweight | Default | — | $1.50 | $0.15 | $9.00 |
| Gemini 3.6 Flash ᴾ | Versatile | Default | — | $0.75 | $0.075 | $3.75 |
| Gemini 3.7 Flash ᴾ | Versatile | Default | — | $0.75 | $0.075 | $3.75 |

ᴾ Promotional pricing **through 31 December 2026**. No cache-write charge for Google models.

### xAI, Microsoft, Moonshot, fine-tuned
| Model | Category | Tier | Input | Cached in | Output |
|---|---|---|---|---|---|
| Grok 4.5 | Versatile | Default ≤200K | $2.00 | $0.50 | $6.00 |
| Grok 4.5 | Versatile | Long >200K | $4.00 | $1.00 | $12.00 |
| Grok 4.6 | Versatile | Default ≤200K | $2.00 | $0.50 | $6.00 |
| Grok 4.6 | Versatile | Long >200K | $4.00 | $1.00 | $12.00 |
| MAI-Code-1-Flash | Lightweight | — | $0.75 | $0.075 | $4.50 |
| MAI-Code-1.1-Flash | Lightweight | — | $0.20 | $0.02 | $1.20 |
| Kimi K2.7 Code | Versatile | — | $0.95 | $0.19 | $4.00 |
| Kimi K3 | Powerful | — | $3.00 | $0.30 | $15.00 |
| Raptor mini (fine-tuned GPT-5 mini) | Versatile | — | $0.25 | $0.025 | $2.00 |

**Note:** xAI cached input is **25% of input**, not the 10% used by every other provider.

### Cost ranking by output price (cheapest → dearest)
GPT-5.6 Luna & MAI-Code-1.1-Flash $1.20 · GPT-5.4 nano $1.25 · GPT-5 mini & Raptor mini $2.00 · Gemini 3.6/3.7 Flash $3.75 · Kimi K2.7 Code $4.00 · MAI-Code-1-Flash & GPT-5.4 mini $4.50 · Claude Haiku 4.5 $5.00 · Grok 4.5/4.6 $6.00 · Gemini 3.5 Flash $9.00 · Claude Sonnet 5 & GPT-5.6 Sol $10.00 · Gemini 3.1 Pro & GPT-5.6 Terra $12.00 · GPT-5.3-Codex $14.00 · Claude Sonnet 4/4.5/4.6, GPT-5.4, Kimi K3 $15.00 · Claude Opus 4.5–5 $25.00 · GPT-5.5 $30.00 · Opus 4.8 fast mode & Fable 5 $50.00.

*(Ranking derived; all inputs from the pricing page above.)*

## A4. How a token becomes a credit

> "the interaction consumes tokens: input tokens (what's sent to the model), output tokens (what the model generates), and cached tokens (context the model reuses or stores). Each token is priced based on the model used, and the total is converted into AI credits, where **1 AI credit = $0.01 USD**."
> — https://docs.github.com/en/copilot/reference/copilot-billing/models-and-pricing

**Rounding is not documented — UNVERIFIED.** The only hint is "a quick question… might cost **a fraction of an AI credit**", implying sub-credit precision is retained.

### Caching
- Cached tokens are billed at **10% of input price** for all providers except xAI (25%). — https://docs.github.com/en/copilot/tutorials/optimize-ai-usage
- **Cache write is an extra charge, not a discount** — priced *above* input (e.g. Opus 4.8: input $5.00, cache write $6.25 = 1.25×).
- **Cache invalidation events re-bill the full context as fresh input:**
  > "Switching models mid-session… Coming back to an old session. **Caches expire after a period of inactivity (24 hours for OpenAI models and 1 hour for most others)**… Changing the reasoning effort level, context size, or the set of enabled tools and MCP servers during a session invalidates the cache."
  > — https://docs.github.com/en/copilot/tutorials/optimize-ai-usage
- Auto model selection protects the cache: "It only changes models at natural cache boundaries, when a new session starts or after you run `/compact`, never mid-task."

### Reasoning tokens
> "Choosing a larger context window or higher reasoning will impact AI credits consumption; **more tokens will be consumed, so more credits will be used.**"
> — https://docs.github.com/en/copilot/reference/ai-models/supported-models#models-with-extended-capabilities

Whether reasoning tokens bill at the **output** rate specifically is **UNVERIFIED**.
1M context is available in **VS Code and Copilot CLI only**; configurable reasoning in **VS Code, Copilot CLI, and Copilot cloud agent**.

### The critical behavioural inversion
> "**Agentic features**: Features like agent mode and Copilot cloud agent can involve **multiple model calls within a single task**."
> — https://docs.github.com/en/copilot/concepts/billing/usage-based-billing-for-individuals

Under legacy premium requests, "actions Copilot takes autonomously… such as tool calls, do not [count]". **Under AI credits every model call in the loop is billed.** This is the single largest cost-behaviour change, and it is why agent mode and cloud agent now dominate spend.

Observable per call in the SDK: `assistant.usage` fires "once for **every model API call in a turn (including calls made by sub-agents)**" with `model`, `inputTokens`, `outputTokens`, `cost`. — https://docs.github.com/en/copilot/how-tos/copilot-sdk/features/usage-and-billing

Context inflation sources: "open editor tabs, attached files, and the full back-and-forth of a long conversation all count as context" and "**Large tool sets (for example, a full MCP server's worth of tools) add to the context on every request.**"

### Two multipliers applied on top of raw token cost
1. **−10% for auto model selection** (paid plans, in Chat / CLI / Copilot app / cloud agent) — https://docs.github.com/en/copilot/concepts/models/auto-model-selection
2. **+10% for data residency / FedRAMP enforcement**: "if an interaction would normally consume 100 AI credits, the same interaction processed with this enforcement enabled consumes **110 AI credits**." — https://docs.github.com/en/enterprise-cloud@latest/admin/data-residency/github-copilot-with-data-residency

## A5. Actions minutes — the second meter

Cloud agent and code review burn **GitHub Actions minutes in addition to AI credits**.

| Runner | Per-minute rate | | Plan | Included minutes/mo |
|---|---|---|---|---|
| Linux 1-core (`actions_linux_slim`) | $0.002 | | GitHub Free | 2,000 |
| Linux 2-core (`actions_linux`) | $0.006 | | GitHub Pro | 3,000 |
| Linux arm64 | $0.005 | | Free for orgs | 2,000 |
| Windows 2-core | $0.010 | | Team | 3,000 |
| Windows arm64 | $0.010 | | **GHEC** | **50,000** |
| macOS 3/4-core | $0.062 | | | |

— https://docs.github.com/en/billing/concepts/product-billing/github-actions

- **Public repositories: Actions minutes remain free.** Private repos draw from plan entitlement.
- Code review Actions usage is filterable in billing reports by `workflow_path` = `dynamic/agents/copilot-pull-request-reviewer`.
- Self-hosted runners **do not consume Actions minutes**.
- Cloud agent hard cap: **59 minutes per session**, "a hard limit that cannot be extended or bypassed"; shorten via `timeout-minutes` in `copilot-setup-steps.yml`. — https://docs.github.com/en/copilot/concepts/agents/cloud-agent/about-cloud-agent

## A6. Code review cost specifically

| Effort level | AI credits per review | Notes |
|---|---|---|
| **Lite** | **$0.05 – $1.00** | Current default |
| **Balanced** | **$0.25 – $5.00** | Higher-reasoning model. **Becomes the "Default" behaviour on 28 Sep 2026** |

— https://docs.github.com/en/copilot/concepts/agents/code-review#estimated-consumption

- **Billed twice:** AI credits + Actions minutes (private repos).
- **The model is not disclosed:** "Copilot code review is an exception—the model is selected automatically and is not disclosed, so per-token costs may vary between reviews."
- **Attribution:** automatic reviews → **PR author**; manual → **requester**; bot/Actions PRs → triggering user or a designated billing owner. "If neither has a Copilot seat, usage is billed to the enterprise or cost center instead."
- **Code review bypasses model policy:** "Copilot code review may use models that are **not enabled on your organization's 'Models' settings page**. The 'Models' settings page only controls Copilot Chat."

---

# PART B — Budgets and the block-evaluation order

**Primary source:** https://docs.github.com/en/copilot/concepts/billing/budgets-for-usage-based-billing

## B1. The exact evaluation sequence

Every credit-consuming request passes through three gates, in this order:

```
┌─ 1. USER-LEVEL BUDGET (ULB) ──────────────────────────────────┐
│  Active in BOTH pool phase and metered phase.                 │
│  Exceeded → BLOCKED IMMEDIATELY. Always a hard stop.          │
│  "no other budget can override or supplement them"            │
└───────────────────────────────┬───────────────────────────────┘
                                ▼ passes
┌─ 2. SHARED POOL ──────────────────────────────────────────────┐
│  Credits remaining → served free.                             │
│  Pool empty → move to metered at $0.01/credit                 │
│  ⚠ Requires the "AI credit paid usage" policy to be ENABLED.  │
│    If disabled → BLOCKED until next cycle, regardless of      │
│    any budget configuration.                                  │
└───────────────────────────────┬───────────────────────────────┘
                                ▼ metered
┌─ 3. COST CENTER → ORG → ENTERPRISE budget ────────────────────┐
│  In a cost center?      → cost center budget                  │
│  Else org has a budget? → organization budget                 │
│  Else                   → enterprise spending limit           │
│  Limit reached + "Stop usage…" ON  → BLOCKED                  │
│  Limit reached + "Stop usage…" OFF → charges keep accruing    │
│                                      (OFF IS THE DEFAULT)     │
└───────────────────────────────────────────────────────────────┘
```

> "**Any budget set to $0 USD stops usage immediately for the users it applies to.**"

## B2. Budget types

| Control | What it caps | Active when | Scope | Hard stop? |
|---|---|---|---|---|
| Universal user-level budget | Each user's total consumption | **Always** (pool + metered) | Per user | **Always** |
| Cost center ULB | Each member's total, per cost center | **Always** | Per user, by cost centre | **Always** |
| Individual ULB | One specific user | **Always** | Per user | **Always** |
| Cost center budget | Team's metered charges | Metered only | Per cost centre | Only if "Stop usage" on |
| Organization budget | Org's metered charges | Metered only | Per org | Only if "Stop usage" on |
| Enterprise budget | Enterprise metered charges | Metered only | Enterprise | Only if "Stop usage" on |

**ULB precedence:** individual ULB > cost centre ULB > universal ULB.

## B3. The five traps

**1. "Stop usage when budget limit is reached" is OFF by default.**
> "it applies to enterprise spending limits, cost center budgets, and organization budgets only, and is **off by default**. Without it, charges continue to accrue past the limit. **Always enable it when creating a budget.**"
> "Without [it], your spending limits are alerts only, not guardrails." — https://docs.github.com/en/copilot/tutorials/budgets/getting-started-with-budget-controls

**2. The enterprise budget is not a bill cap.**
> "400 Copilot Business licenses at $19 USD per month means $7,600 USD in license fees. A $5,000 USD enterprise budget means your maximum bill is **$12,600 USD, not $5,000 USD**."

**3. Lowest remaining headroom wins.**
> "if a user has $5 USD remaining on their individual ULB but the enterprise budget only has $1 USD remaining, **the enterprise budget blocks them**." And in reverse: "raising a cost center or enterprise budget **does not unblock** a user who has hit their ULB."

**4. Org budgets are unreliable for multi-org users.**
> "If a user receives Copilot licenses from multiple organizations, **GitHub picks one organization at random each billing cycle to bill the seat.**… making enforcement unpredictable."

**5. First-cycle overshoot.**
> "the budget applies only to metered usage **from the date of its creation onwards**… you may **exceed your budget in the first billing cycle** even if you select stop usage." — https://docs.github.com/en/billing/concepts/budgets-and-alerts

## B4. Included usage controls for cost centres

Caps a cost centre's draw from the **pool**, before the metered phase. Cap is auto-calculated: **1,900 credits per Business licence + 3,900 per Enterprise licence**. Example: 10 Business + 5 Enterprise = **38,500 credits**.

| Change | Cap effect | When |
|---|---|---|
| Licence added / upgraded | Increases | **Immediately** |
| Licence removed / downgraded | Decreases | **Next billing cycle** |
| Member moves between cost centres | Recalculated both | Next cycle |
| Unlicensed user added/removed | No change | — |

At the cap, the admin chooses **block** or **paid overage**. Not retroactive — enabling it does not redistribute the existing pool.

**Cost centre exclusion:** when enabled, that team's metered charges "are **not counted against the enterprise budget** and will not be blocked when the enterprise budget is reached."

## B5. Who can set what

| Budget | Who | Source |
|---|---|---|
| Enterprise, cost centre | Enterprise owners, billing managers | https://docs.github.com/en/billing/concepts/budgets-and-alerts |
| Organization | Organization owners (only option available to them) | same |
| Personal | Account owner | same |
| Policies (AI controls) | Enterprise owners or **"Manage enterprise AI controls"** custom role | https://docs.github.com/en/copilot/concepts/policies |

**Budget scopes at creation:** product-level, SKU-level (`Copilot AI credits`, `Spark AI credits`, `Copilot cloud agent`), or **bundled AI credits** (spans all AI SKUs). Enterprise AI-credit budgets can scope to enterprise / org / cost centre / **per user**. **Scope cannot be changed after creation.** Max 10,000 budgets per account. Not available for pre-paid volume licences.

## B6. Alerts

- **Budget threshold alerts: 75%, 90%, 100%.** UI + email. Default recipients: account owners and billing managers; additional recipients configurable. Available for enterprise / cost centre / org / repository scopes.
- ⚠️ "**Alerting for user-level budgets is not consistently available in all scenarios. Don't rely on user-level budget alerts as your only signal.**"
- **Included-usage alerts (90%/100%)** exist for Actions, Packages, LFS and Codespaces — **Copilot/AI credits is absent from that list.** Whether pool-depletion alerts exist for AI credits is **UNVERIFIED**.

## B7. What a blocked user actually experiences

> "When a user reaches any budget limit, their access to Copilot features that consume AI credits is blocked. **There is no automatic fallback to lower-cost models.** Code completions and next edit suggestions continue to work… A blocked user remains blocked until: the next billing cycle begins… [or] an administrator increases the relevant budget."

**Verbatim error strings for budget exhaustion are not published — UNVERIFIED.**

## B8. Azure / invoiced billing

- "Copilot license usage is measured as the **number of active seats**."
- "If your enterprise exceeds its included pool of AI credits, the cost of any additional usage is included [on the Azure invoice]."
- "Usage data is sent **daily** to Azure." Charges appear "at the **start of the next month**."
- Mid-cycle switch: usage before the switch bills via GitHub, after via Azure.
— https://docs.github.com/en/copilot/reference/copilot-billing/azure-billing · https://docs.github.com/en/billing/reference/azure-billing

**Being invoiced does not exempt you from hard stops.** A $0 ULB or an enabled "Stop usage" still blocks. No documentation states otherwise.

**Individuals:** "additional usage **may be capped**, so to keep working, you'll need to **pay off any additional usage you've already consumed** in order to continue." GitHub may place "a **temporary authorization hold**". Buying extra credits is **not available if you subscribed via GitHub Mobile iOS/Android**.

## B9. Admin cost-control levers, ranked

| # | Lever | Source |
|---|---|---|
| 1 | **Universal ULB** — the only always-on hard stop. Set it *above* per-licence value ($19/$39) so pooling still works | https://docs.github.com/en/copilot/tutorials/budgets/getting-started-with-budget-controls |
| 2 | **Disable "AI credits paid usage"** — absolute overage prevention, overrides all budget config | https://docs.github.com/en/copilot/concepts/billing/budgets-for-usage-based-billing |
| 3 | **Enable "Stop usage when budget limit is reached" on every budget** | same |
| 4 | Enterprise spending limit (failsafe) | same |
| 5 | **Cost centre budgets with users assigned directly** — "charges always follow the user, so enforcement is predictable" | https://docs.github.com/en/copilot/tutorials/budgets/optimizing-your-budget-configuration |
| 6 | Included usage controls on cost centres | budgets page |
| 7 | Cost centre ULBs (per-department per-user defaults) | same |
| 8 | Individual ULB overrides for power users | getting-started tutorial |
| 9 | **Model access policies** — "restricting which models are available [may] be more effective than tightening budgets" | optimizing tutorial |
| 10 | Push **auto model selection** — 10% discount, cheaper routing, cache preservation | https://docs.github.com/en/copilot/concepts/models/auto-model-selection |
| 11 | **CLI session limits**: `/limits set max-ai-credits N` or `--max-ai-credits N` (**soft limit**, public preview) | https://docs.github.com/en/copilot/how-tos/copilot-cli/use-copilot-cli/set-session-limit |
| 12 | Lean toolsets / instructions — "Large tool sets… add to the context on **every request**" | https://docs.github.com/en/copilot/tutorials/optimize-ai-usage |
| 13 | Actions governance — shorten `timeout-minutes`, standard Linux runners | https://docs.github.com/en/billing/concepts/product-billing/github-actions |

**Budget sizing formula (verbatim):**
> "1. Calculate the maximum total consumption your user-level budgets allow… 2. Calculate your **pool value: multiply your Copilot Business seats by $19 USD and your Copilot Enterprise seats by $39 USD**… 3. **Subtract the pool value from the maximum total consumption. The result is the maximum metered charges your budgets need to cover.**"
> — https://docs.github.com/en/copilot/tutorials/budgets/optimizing-your-budget-configuration

## B10. Where usage is visible

| Audience | Location | Shows |
|---|---|---|
| Business/Ent end user | Profile → Copilot settings → Usage | "450 / 1,000 AI credits used" (with ULB) or "100 AI credits used" |
| Individual plan user | Settings → Billing → **AI usage** | Included vs additional, chart by model, credits + cost per model. **Individual plans only** |
| Enterprise owner / billing mgr | Billing & Licensing → Usage → AI usage | Total, heaviest consumers, models, adoption by org. **Can filter by user** |
| **Organization owner** | same | ⚠️ **"cannot view user-level data directly"** — must download a report |
| Cost centre admin | Cost centre home page | Credits consumed vs cap |
| IDEs | VS Code status bar · VS "Copilot Consumptions" · JetBrains "View quota usage" · Xcode menu bar · Eclipse status bar | Plan limits + **allowance reset date** |

— https://docs.github.com/en/copilot/how-tos/manage-and-track-spending/monitor-ai-usage · https://docs.github.com/en/billing/how-tos/products/view-productlicense-use

**Reports** — https://docs.github.com/en/billing/reference/billing-reports

| Report | Max period | Grouping | Extra fields |
|---|---|---|---|
| Summarized usage | **1 year** | date, sku, repository, cost_center_name (+organization) | — |
| Detailed usage | **31 days** | + organization, username, workflow_path | `username`, `workflow_path` |
| **AI usage** | **31 days** | date, model, username | **`input`, `output`, `cache_read`, `cache_write`** |

- Delivered by **email**; link expires after **24 hours**; one report per account at a time.
- ⚠️ "Data for the detailed usage report is available **only through the GitHub web interface and cannot be obtained via the REST API**."
- All usage is logged in **UTC**. `gross_amount − discount_amount = net_amount`, where `discount_amount` reflects included usage.

**REST API** — https://docs.github.com/en/rest/billing/usage (`X-GitHub-Api-Version: 2026-03-10`)

| Endpoint | Notes |
|---|---|
| `GET /organizations/{org}/settings/billing/ai_credit/usage` | Params `year, month, day, user, model, product`. **24 months** of history |
| `GET /organizations/{org}/settings/billing/premium_request/usage` | Legacy SKU, still live |
| `GET /organizations/{org}/settings/billing/usage` and `/usage/summary` | Totals (summary is public preview) |
| `GET /users/{username}/settings/billing/ai_credit/usage` | Individual |
| `GET /enterprises/{enterprise}/settings/billing/usage/summary` | Enterprise; supports `cost_center_id` |

- **Auth: classic PAT only.** "The billing usage endpoints **do not support fine-grained personal access tokens**."
- Org/enterprise-licensed users' usage is **not** in user-level endpoints.

**SDK telemetry** — https://docs.github.com/en/copilot/how-tos/copilot-sdk/features/usage-and-billing: `assistant.usage` (per model call, incl. sub-agents; ephemeral, not replayed on resume), `session.usage_info`, `session.metadata.contextInfo`, `session.usage.getMetrics`, `models.list`, `account.getQuota`.

**Data latency on GitHub surfaces is not documented — UNVERIFIED.** Only the Azure "daily" figure is published.

## B11. Rate limits — separate from credits, and they still exist

**Docs page (qualitative only):** https://docs.github.com/en/copilot/concepts/usage-limits — four stated reasons: capacity, high usage, fairness, abuse mitigation.

> "**Most people see rate limiting for select models, due to limited capacity.**… If you are rate limited, the error message may tell you to wait for your limit to reset, suggest a retry time, or prompt you to upgrade your plan."
> — https://docs.github.com/en/copilot/how-tos/troubleshoot-copilot/troubleshoot-common-issues ("Error: You've hit a rate limit")

**The only published specifics** come from a pre-June-2026 blog post (https://github.blog/news-insights/company-news/changes-to-github-copilot-individual-plans/):
> "GitHub Copilot has two usage limits today: **session** and **weekly (7 day)** limits. Both limits depend on… **token consumption and the model's multiplier**."
> "**Usage limits are separate from your premium request entitlements**… You can have premium requests remaining and still hit a usage limit."
> "If you hit a weekly limit… **you can continue to use Copilot with Auto model selection. Model choice will be reenabled when the weekly period resets.**"

Exact strings from that post's screenshots:
- VS Code: `You've used over 75% of your weekly usage limit. Your limit resets on Apr 27 at 8:00 PM.`
- Copilot CLI: `! You've used over 75% of your weekly usage limit. Your limit resets on Apr 24 at 3 PM.`

⚠️ GitHub said limits would be "loosened" once UBB was in effect. **Whether session/weekly limits still apply post-1-June-2026, and at what values, is UNVERIFIED.** No numeric limits are published anywhere current.

Note: **utility models "Are subject to per-user rate limits"** even though they are free.

---

# PART C — Policy layers and precedence

**Sources:** https://docs.github.com/en/copilot/concepts/policies · https://docs.github.com/en/copilot/reference/enterprise-administrators/policy-conflicts

## C1. The five control layers

```
1. ENTERPRISE  → AI controls tab (renamed; was Settings → Policies)
                 sub-pages: Copilot | Agents | MCP
2. ORGANIZATION→ Settings → Code, planning, and automation → Copilot
                 sub-pages: Policies | Models | Cloud agent | Content exclusion
3. REPOSITORY  → Settings → Copilot (cloud agent config, content exclusion,
                 Memory) + rulesets + instruction files
4. USER        → github.com/settings/copilot (+ /features, /coding_agent, /memory)
5. CLIENT      → IDE settings + enterprise managed settings (MDM/server/file)
```

## C2. The tri-state and the lock

Most enterprise policies offer three values:

| Value | Effect on orgs |
|---|---|
| **Enabled everywhere** | Org **cannot** override |
| **Disabled everywhere** | Org **cannot** override |
| **Let organizations decide** | Org sets its own value |

> "If your enterprise owner has selected a specific policy… you **cannot override that setting at the organization level**." — https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-for-organization/manage-policies

**Exception — Copilot cloud agent** has a fourth value: **"Enabled for selected organizations."**

**Enterprise-assigned users** (licensed by the enterprise, not through an org) are not covered by "Let organizations decide." A separate policy — **"Policies for enterprise-assigned users"** — sets whether delegated policies default enabled or disabled for them.

**Individuals cannot conflict with employers:**
> "A user's individual plan is cancelled when they are added to a Copilot Business or Copilot Enterprise plan, so a user's personal policies cannot conflict with an enterprise's or organization's."

## C3. Multi-org / multi-enterprise conflict resolution

- **Multiple orgs in the same enterprise** → generally **least restrictive** wins.
- **Multiple enterprises** → **most restrictive** wins. Two documented exceptions: **AI credit paid usage** (applies per-enterprise) and **GitHub Spark**.

**The full conflict table** (verbatim, https://docs.github.com/en/copilot/reference/enterprise-administrators/policy-conflicts):

| Policy | Resolution |
|---|---|
| Copilot Metrics API | **Most restrictive** |
| Semantic indexing for non-GitHub repositories | **Most restrictive** — needs **all** orgs explicitly Enabled; Unconfigured = disabled |
| Suggestions matching public code | **Most restrictive** |
| Allow members without a Copilot license to use Copilot code review in GitHub.com | **Most restrictive** |
| Copilot Memory | **Most restrictive** — "will not be used unless **all** of those organizations have enabled this feature" |
| Copilot can search the web | Least restrictive |
| Copilot Chat in GitHub Mobile | Least restrictive |
| Copilot Chat in the IDE | Least restrictive |
| Copilot Agent Mode in IDE Chat | Least restrictive |
| Copilot code review | Least restrictive |
| Copilot cloud agent | Least restrictive |
| Spark | Least restrictive |
| Copilot in GitHub.com | Least restrictive |
| Copilot in GitHub Desktop | Least restrictive |
| Copilot CLI | Least restrictive |
| GitHub Copilot app | Least restrictive |
| Editor preview features | Least restrictive |
| MCP servers in Copilot | Least restrictive |
| Copilot-generated commit messages | Least restrictive |

⚠️ Naming drift: this table says "Copilot Metrics API"; the REST reference calls the same policy **"Copilot usage metrics."**

## C4. Enterprise managed client settings — a separate precedence chain

**Source:** https://docs.github.com/en/copilot/reference/enterprise-administrators/enterprise-managed-settings

Precedence (earlier wins): **1. MDM-managed → 2. Server-managed → 3. File-based → 4. User-level.**

**Exception:** `sandbox`, `permissions.deny`, `permissions.ask`, `permissions.allow` compose in the **most restrictive** direction across all delivery methods.

Multiple enterprise teams → team files combine **least restrictive** per key, then apply beneath enterprise settings, "where platform decisions always win."

**Overridable-by-team keys (exhaustive):** `model`, `permissions.disableBypassPermissionsMode`, `permissions.deny`, `permissions.ask`, `permissions.allow`, `allowedMcpServers`, `deniedMcpServers`. Mark with `{ "overridable": <VALUE> }`.
**Additive keys:** `enabledPlugins`, `extraKnownMarketplaces`.

## C5. Custom-agent / instruction precedence

> "the lowest level configuration overrides higher-level configurations… repository-level agent takes precedence over an organization-level agent, and the organization-level agent overrides an enterprise-level agent."
— https://docs.github.com/en/copilot/reference/custom-agents-configuration

Note this is the **opposite direction** from policy precedence. Deduplication key = filename minus `.md`/`.agent.md`.

**MCP processing order:** out-of-the-box MCP → custom agent MCP → repository settings MCP (each overrides the previous).

## C6. Who can change what

| Layer | Who |
|---|---|
| Enterprise policies | Enterprise owners **or** the **"Manage enterprise AI controls"** custom role |
| Enterprise audit logs | **"Read enterprise audit logs"** custom role |
| Enterprise metrics | **"View Enterprise Copilot Metrics"** custom role (Insights tab only) |
| Org policies | Organization owners (granular org-level Copilot permissions: **UNVERIFIED / not found**) |
| Repo cloud-agent config | Repo admins (also via REST) |
| Budgets | Enterprise owners / billing managers; org owners for org budgets |

**Explicitly excluded from AI-manager roles:** "Access management settings for Copilot; Settings in the 'Billing' section of the Copilot page; Settings in the 'Metrics' section of the Copilot page."
— https://docs.github.com/en/copilot/tutorials/roll-out-at-scale/govern-at-scale/establish-ai-managers

---

# PART D — The policy roster

> ⚠️ **GitHub publishes no exhaustive policy roster.** Confirmed by reading `/copilot/reference` and `/copilot/reference/enterprise-administrators` in full. `copilot-feature-matrix` is an IDE support matrix, not policies. The roster below is assembled from `policy-conflicts` + `supported-surfaces-for-policies` + per-feature how-tos. **Defaults marked ❓ are genuinely not documented.**

## D1. Enterprise policies (33)

| # | Policy | Page | Values | Default | Source |
|---|---|---|---|---|---|
| 1 | **Suggestions matching public code** | Copilot → Privacy | Allowed / **Blocked** | **Blocked** for Business | [enterprise-policies](https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-for-enterprise/manage-enterprise-policies) |
| 2 | Copilot in GitHub.com | Features & clients | tri-state | ❓ | [policy-conflicts](https://docs.github.com/en/copilot/reference/enterprise-administrators/policy-conflicts) |
| 3 | Copilot Chat in the IDE | Features & clients | tri-state | ❓ | same |
| 4 | Copilot Agent Mode in IDE Chat | Features & clients | tri-state | ❓ | same |
| 5 | Copilot Chat in GitHub Mobile | Features & clients | tri-state | ❓ | same |
| 6 | Copilot in GitHub Desktop | Features & clients | tri-state | ❓ | same |
| 7 | Copilot CLI | Features & clients | tri-state | ❓ | same |
| 8 | GitHub Copilot app | Features & clients | tri-state | ❓ | [github-copilot-app](https://docs.github.com/en/copilot/concepts/agents/github-copilot-app) |
| 9 | Copilot-generated commit messages | Features & clients | tri-state | ❓ | policy-conflicts |
| 10 | **Editor preview features** | Features & clients | tri-state | ❓ | Gates the inline-suggestion model switcher: [code-suggestions](https://docs.github.com/en/copilot/concepts/completions/code-suggestions) |
| 11 | **Copilot can search the web** | Copilot policies | tri-state | **Disabled** (personal equivalent documented as disabled) | [supported-surfaces](https://docs.github.com/en/copilot/reference/supported-surfaces-for-policies) |
| 12 | **Copilot usage metrics** | Copilot policies | Enabled everywhere / … | Must be **Enabled everywhere** for REST metrics to work | [copilot-usage-metrics](https://docs.github.com/en/rest/copilot/copilot-usage-metrics) |
| 13 | **Semantic indexing for non-GitHub repositories** | Copilot policies | Enabled / **Unconfigured** | **Disabled**; Unconfigured = unavailable | [repository-indexing](https://docs.github.com/en/copilot/concepts/context/repository-indexing) |
| 14 | **Default availability for released models** | Configure models | Enabled / Disabled | **Enabled** — new GA models auto-enable | [default-availability](https://docs.github.com/en/copilot/concepts/models/default-availability) |
| 15 | **Configure models** (per-model) | Configure models | Enabled / Disabled / Delegate to Organizations / Delegate to Enterprise Teams/Apps / Delegate to Default Policy | varies | [manage-availability-of-default-models](https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-for-enterprise/manage-availability-of-default-models) |
| 16 | **Configure custom models** (BYOK) | Copilot policies | Enabled / Disabled | ❓ | [enable-custom-models](https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-for-enterprise/enable-custom-models) |
| 17 | Enterprise teams mode (preview) | AI controls → Copilot | Toggle | Off — **when on, deactivates all org model settings** | manage-availability-of-default-models |
| 18 | **Copilot cloud agent** | Agents | Enabled everywhere / **Enabled for selected organizations** / Disabled / Let orgs decide | **Disabled by default** | [enable-copilot-cloud-agent](https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-for-enterprise/manage-agents/enable-copilot-cloud-agent) |
| 19 | **Block Copilot cloud agent in all repositories owned by ENTERPRISE** | Agents | Toggle | Off — **applies to everyone**, incl. personal plans and other enterprises' licensees | [block-agentic-features](https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-for-enterprise/manage-agents/block-agentic-features) |
| 20 | **Copilot code review** | Agents | Enabled everywhere / Let orgs decide / Disabled | ❓ | [enable-copilot-code-review](https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-for-enterprise/manage-agents/enable-copilot-code-review) |
| 21 | **Block Copilot code review in all enterprise repositories** | Agents | Toggle | Off — applies to everyone | block-agentic-features |
| 22 | Automatically request Copilot code review | **Enterprise branch ruleset** | + "review on every push", "review draft PRs" | Off | enable-copilot-code-review |
| 23 | **Agent apps** | Agents | Enabled / Disabled | ❓ — **enterprise-only gate, no org toggle** | [agent-apps](https://docs.github.com/en/copilot/concepts/agents/agent-apps) |
| 24 | **Third-party coding agents** (Anthropic Claude, OpenAI Codex) | Agents | Enabled / Disabled | Off — org settings invisible until enabled here | [org manage-policies](https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-for-organization/manage-policies) |
| 25 | **MCP servers in Copilot** | MCP | tri-state | **Disabled by default** | [mcp concept](https://docs.github.com/en/copilot/concepts/context/mcp) |
| 26 | MCP Registry URL | MCP | URL | — | **Does NOT apply to cloud agent** — enable-copilot-cloud-agent |
| 27 | Restrict MCP access to registry servers | MCP | Enabled / Disabled | ❓ | IDEs + CLI + app only — supported-surfaces |
| 28 | **Spark** | AI controls | Enabled everywhere / Let orgs decide / (Disabled) | **Disabled** for enterprise-owned orgs. ⚠️ **Being sunset** | [manage-spark](https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-for-enterprise/manage-spark) |
| 29 | **Copilot Memory** | Copilot policies | Enabled / Disabled | **OFF** for managed plans; ON for individual | [manage-as-administrator](https://docs.github.com/en/copilot/how-tos/use-copilot-agents/copilot-memory/manage-as-administrator) |
| 30 | **Content exclusion** | Copilot → Content exclusion | Path rules | Empty | [exclude-content](https://docs.github.com/en/copilot/how-tos/configure-content-exclusion/exclude-content-from-copilot) |
| 31 | **Policies for enterprise-assigned users** | Enterprise policies | Sets default for delegated policies for directly-licensed users | ❓ | [policies concept](https://docs.github.com/en/copilot/concepts/policies) |
| 32 | Opt in to user feedback collection | Features & clients | On / Off | Off | enterprise-policies |
| 33 | **Organization access** (licensing) | Billing → Licensing → Copilot | Enable for all orgs / Allow for specific orgs | ❓ — gates whether org owners can assign seats at all | [grant-access](https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-for-enterprise/manage-access/grant-access) |
| — | **AI credits paid usage** | Billing | Enabled / Disabled | **Enabled** | [budgets](https://docs.github.com/en/copilot/concepts/billing/budgets-for-usage-based-billing) |

## D2. Organization policies

| Policy | Values | Default | Notes |
|---|---|---|---|
| All feature/client policies | Enabled / Disabled | inherited | **Locked** if enterprise set explicit value |
| Suggestions matching public code | Allow / Block | Blocked | Most restrictive across orgs; org-seated users **cannot** set this personally |
| **Copilot cloud agent** | Enabled / Disabled | **Disabled** | [add-copilot-cloud-agent](https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-for-organization/add-copilot-cloud-agent) |
| **MCP servers on GitHub.com** | Enabled / Disabled | **Disabled** | Distinct from the enterprise "MCP servers in Copilot" |
| Repository access (cloud agent) | All / Selected / None | **All** where agent available | Also via REST |
| **Automations** (cloud agent) | Allowed / Not allowed | **Allowed** | Controlled **separately** from the cloud agent policy |
| Partner agents | per-agent toggle | Off | Visible only if enterprise enabled 3P agents |
| Models (per model) | Enabled / Disabled / Delegate to Default Policy / 🛡 locked | varies | 🛡 = enterprise-enforced |
| Default availability for released models | Enabled / Disabled | Enabled | |
| Opt in to user feedback collection | On / Off | Off | Only if Business/Enterprise **and** "Copilot in GitHub.com" enabled |
| Opt in to preview features | On / Off | Off | Same condition |
| Semantic indexing for non-GitHub repos | Enabled / Unconfigured | Unconfigured | Needs **all** orgs Enabled |
| Content exclusion | Path rules | Empty | Applies to users seated **by that org** |
| Configure runners for cloud agent | Runner type + repo customisation | ❓ | [configure-runner](https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-for-organization/configure-runner-for-coding-agent) |
| Network access | — | — | [manage-network-access](https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-for-organization/manage-access/manage-network-access) |
| Copilot Memory | Enabled (named) / others inferred | **OFF** for managed | most-restrictive across orgs |
| **Agent apps** | ❌ **not settable at org level** | — | "Agent apps are not enabled here. They are controlled separately by a single 'agent apps' policy." |

## D3. Which policies apply to which surface

**Source:** https://docs.github.com/en/copilot/reference/supported-surfaces-for-policies

> "not every policy applies to every available surface." Note: "Copilot cloud agent **can** access the Internet, but the 'Copilot can search the web' policy does not affect this capability."

| Policy | IDEs | Cloud agent | 3P agents | CLI | Copilot app | Chat in GitHub | Code review | Spark |
|---|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| Editor preview features | ✔ | ✘ | ✘ | ✘ | ✘ | ✘ | ✘ | ✘ |
| Copilot can search the web | ✔ | ✘ | ✘ | ✘ | ✘ | ✔ | ✘ | ✘ |
| Configure custom models | ✔ | ✘ | ✘ | ✔ | ✔ | ✔ | ✘ | ✘ |
| Suggestions matching public code | ✔ | ✔¹ | ✔¹ | ✘ | ✘ | ✘ | ✘ | ✘ |
| MCP servers in Copilot | ✔ | ✔ | ✔ | ✔ | ✔ | ✘ | ✔ | ✘ |
| Restrict MCP access to registry servers | ✔ | ✘ | ✘ | ✔ | ✔ | ✘ | ✘ | ✘ |
| Content exclusion | ✔ | ✔ | ✘ | ✘ | ✘ | ✔ | ✔ | ✘ |
| Configure models | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✘ | ✘ |
| Copilot Memory | ✘ | ✔ | ✔ | ✔ | ✘ | ✘ | ✔ | ✘ |

¹ "Only supported in annotate mode."

**Read this table as a list of holes.** Content exclusion does **nothing** in Copilot CLI, third-party agents, the Copilot app, or Spark. "Configure models" does **nothing** for code review.

## D4. Repository-level settings

Enumerable via `GET /repos/{owner}/{repo}/copilot/cloud-agent/configuration` — https://docs.github.com/en/rest/copilot/copilot-cloud-agent-management

```
mcp_configuration                            : object|null
enabled_tools : { codeql, copilot_code_review, secret_scanning,
                  dependency_vulnerability_checks }
require_actions_workflow_approval            : boolean
is_firewall_enabled                          : boolean
is_firewall_recommended_allowlist_enabled    : boolean
custom_allowlist                             : string[]
is_automations_enabled                       : boolean
require_write_access_for_automation_triggers : boolean
```

Plus: **Repo → Settings → Copilot → Memory** (view/delete repository-level facts), repository content exclusion, branch rulesets, and instruction/agent/skill files in `.github/`.

## D5. Personal settings — github.com/settings/copilot

**Source:** https://docs.github.com/en/copilot/how-tos/manage-your-account/manage-policies

| Setting | Values | Default | Locked by org? |
|---|---|---|---|
| Suggestions matching public code | Allow / Block | — | **Yes** — "you will not be able to configure [this] in your personal account settings" if org-seated on GHEC |
| **Copilot access to Bing** | Enabled / Disabled | **Disabled** | via "Copilot can search the web" |
| Allow GitHub to use my data for AI model training | Enabled / Disabled | Enabled (opt-out), since 24 Apr 2026 | **Not displayed** for Business/Enterprise |
| Cloud agent → Repository access | No / All / Only selected | **All** | Personal repos only |
| Partner agents | per-agent toggles | — | Enterprise gate |
| **Copilot Memory** (`/settings/copilot/features`) | Enabled / Disabled | ON for paid individual; ON if org allows | Org can disable |
| **Default billing entity** (`/settings/copilot/features`) | picker | none | **Required** for multi-licence users to generate Memory preferences |
| Automatic Copilot code review | Enabled | Off | Pro/Pro+/Max only |

**Settings that do NOT exist** (checked and denied): a personal "Copilot in the CLI" toggle; a personal "Copilot Extensions" toggle; a personal model-preference setting; a personal "Editor preview features" toggle (that's an org/enterprise policy). Account-wide betas live in **Feature preview**, not Copilot settings — https://docs.github.com/en/get-started/using-github/exploring-early-access-releases-with-feature-preview

## D6. Client-side kill switches (VS Code)

**Source:** https://code.visualstudio.com/docs/agents/reference/ai-settings

| Setting | Default | Effect |
|---|---|---|
| **`chat.disableAIFeatures`** | `false` | **The true master off switch** — hides chat + inline suggestions, disables Copilot extensions |
| **`chat.agent.enabled`** | `true` | Agent-mode kill switch |
| `chat.agent.maxRequests` | **`25`** | **Hard cap on model calls per agent turn** — the single most effective per-turn cost brake |
| `github.copilot.enable` | `{"*":true,"plaintext":false,"markdown":false,"scminput":false}` | Per-language completions |
| `chat.mcp.access` | `true` | MCP gating |
| `chat.tools.global.autoApprove` | `false` | "**disables critical security protections**" — forced off by `permissions.disableBypassPermissionsMode` |
| `chat.permissions.default` | `"default"` | "**If enterprise policy disables auto-approval, new sessions use Default Approvals.**" |
| `chat.agent.sandbox.enabled` | `off` | `off`/`on`/`allowNetwork`; macOS + Linux only |
| **`chat.sessionSync.enabled`** | **`true`** | **Session data syncs to your GitHub account.** Exclude repos via `chat.sessionSync.excludeRepositories` |
| `github.copilot.chat.localIndex.enabled` | `true` | Local session tracking |
| `chat.useAgentsMdFile` / `chat.useClaudeMdFile` | `true` | Instruction-file loading |
| `chat.byokUtilityModelDefault` | `"GitHub Copilot"` | Utility model when main model is BYOK |
| `chat.subagents.allowInvocationsFromSubagents` | `false` | Max nesting depth **5** |

---

# PART E — Everything that can block a request

Twelve gates. A request must pass **all** of them.

## Gate 1 — Licence / seat

| Condition | Result | Source |
|---|---|---|
| No Copilot seat | Blocked | [plans](https://docs.github.com/en/copilot/get-started/plans) |
| Seat unassigned | Access lost **at end of current billing cycle** | audit event `copilot.cfb_seat_assignment_unassigned` |
| Enterprise "Organization access" not granted to your org | Org owners cannot assign seats at all | [grant-access](https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-for-enterprise/manage-access/grant-access) |
| Subscription ended / billing failure / account flagged spammy / suspended | Blocked | audit event `copilot.access_revoked` |
| **GitHub Enterprise Server** | "Copilot is **not currently available** for GitHub Enterprise Server." | [plans](https://docs.github.com/en/copilot/get-started/plans) |
| Copilot Free while org-seated | Free plan unavailable — "only available to individual developers who don't have access to Copilot through an organization or enterprise" | plans |
| Self-serve B/E trials | **Paused** since 22 Apr 2026 | plans |

## Gate 2 — Quota / budget

Full detail in Part B. Summary of hard stops:
1. **User-level budget exceeded** → always blocked.
2. **Pool empty + "AI credits paid usage" policy disabled** → blocked until next cycle.
3. **Cost centre / org / enterprise budget reached + "Stop usage" ON** → blocked.
4. **Any budget = $0** → blocked immediately.
5. Cost centre **included usage control** reached with "block" selected → blocked.
6. Individual: unpaid additional usage → "additional usage may be capped… you'll need to pay off any additional usage you've already consumed."

**Never blocked by quota:** code completions, next edit suggestions, utility models.
**No fallback to cheaper models.** That behaviour was removed on 1 June 2026.

## Gate 3 — Policy

Any of the ~33 enterprise / ~17 org policies in Part D can disable a surface outright. The high-frequency ones:

| Policy | Default | Blocks |
|---|---|---|
| Copilot cloud agent | **Disabled** | All agent tasks |
| MCP servers in Copilot | **Disabled** | All MCP tools |
| MCP servers on GitHub.com (org) | **Disabled** | MCP on the web |
| Copilot Memory | **OFF** for managed plans | Memory recall/write |
| Semantic indexing for non-GitHub repos | **Unconfigured = disabled** | Indexing |
| Copilot can search the web / Bing | **Disabled** | Web search in chat |
| Spark | **Disabled** for enterprise orgs | Spark |
| Block cloud agent in all enterprise repos | Off | **Everyone**, incl. personal plans and other enterprises' licensees |
| Block code review in all enterprise repos | Off | Everyone |
| Model not enabled | varies | That model only |

**Model-specific gates:**
- **Claude Fable 5** requires explicit admin enablement — "Enterprise and business users need to enable the Claude Fable 5 model." (Anthropic retains prompts/outputs for safety classifiers on this model.) — [configure-access-to-ai-models](https://docs.github.com/en/copilot/how-tos/copilot-on-github/set-up-copilot/configure-access-to-ai-models)
- If **no models are enabled**, the **base model** is used: GPT-5.3-Codex, designated 18 Mar 2026, Business/Enterprise only. LTS = one year. New GA models auto-enable at **day 60**. — [fallback-and-lts-models](https://docs.github.com/en/copilot/concepts/models/fallback-and-lts-models)

**Cloud-agent-specific access rules** — [access-management](https://docs.github.com/en/copilot/concepts/agents/cloud-agent/access-management):
- **Managed user account-owned personal repositories are excluded.**
- **Automations require private or internal repositories** — not public.

## Gate 4 — Content exclusion

**Source:** https://docs.github.com/en/copilot/how-tos/configure-content-exclusion/exclude-content-from-copilot

| Fact | Detail |
|---|---|
| Syntax | fnmatch, case-insensitive, `- "/PATH"` per line; org level supports `"*":` and repo refs via http(s)/git/ssh/scp, incl. Azure DevOps |
| **Propagation delay** | **Up to 30 minutes** in already-loaded IDEs. JetBrains/VS: restart. VS Code: *Developer: Reload Window*. Vim: per-file fetch |
| **NOT supported in** | **Copilot CLI**; **Agent mode and Edit mode** in Copilot Chat; third-party agents; Copilot app; Spark |
| Preview | Public preview on the GitHub website and GitHub Mobile |
| **Leakage** | "Copilot may use **semantic information** from an excluded file if the information is provided by the IDE indirectly… type information and hover-over definitions… build configuration" |
| **Leakage** | "do not apply to **symbolic links** and **repositories located on remote filesystems**" |
| Inheritance | From parent org and orgs in the same enterprise. **Fork inheritance: undocumented** |
| REST | https://docs.github.com/en/rest/copilot/copilot-content-exclusion-management |
| Audit | `copilot.content_exclusion_changed` (includes `excluded_paths`) |

Also: **code review file exclusions** — https://docs.github.com/en/copilot/reference/code-review-excluded-files

## Gate 5 — Public code filter

**Source:** https://docs.github.com/en/copilot/concepts/completions/code-referencing

- Compares the suggestion plus **~150 characters** of surrounding code against an index of all public GitHub repos.
- Matches occur in "**less than one percent**" of suggestions.
- **Index refreshed every few months** — may miss new code and may match deleted code.
- Only **accepted** suggestions are checked. "Code you have written, and Copilot suggestions you have altered, are not checked."
- **Block vs annotate differs by IDE:**
  > "In **VS Code**… displays a message with a link to show the matched code… In **Visual Studio, JetBrains, Eclipse, and GitHub Mobile**, GitHub Copilot Chat uses filters that **block** matches with public code." — [responsible-use/chat](https://docs.github.com/en/copilot/responsible-use/chat)
- Default for Business: **Blocked**. Conflict resolution: **most restrictive**.
- GHE.com requires `origin-tracker.githubusercontent.com`.

## Gate 6 — Responsible AI filters

**Source:** https://docs.github.com/en/copilot/responsible-use/chat

- "For **all of the default AI models**, input prompts and output completions run through GitHub Copilot's content filters for harmful, offensive, or off-topic content."
- Categories: **Hate and unfairness · Sexual · Violence · Self-harm · Protected material · Jailbreak · Code vulnerability.**
- Off-topic: "not designed to answer non-coding questions."
- Language: "The primary supported language… is **English**."
- **BYOK is still filtered:** "Regardless of which provider is active, responses still pass through GitHub's API and may have content filtering before results are shown to you."
- Report offensive output to `copilot-safety@github.com`.
- Trade controls: https://docs.github.com/en/site-policy/other-site-policies/github-and-trade-controls

⚠️ **Exact refusal strings are not documented — UNVERIFIED.**

## Gate 7 — Context and technical limits

| Limit | Value | Source |
|---|---|---|
| **Cloud agent session** | **59 minutes — "a hard limit that cannot be extended or bypassed"** | [about-cloud-agent](https://docs.github.com/en/copilot/concepts/agents/cloud-agent/about-cloud-agent) |
| Cloud agent image attachment | **3.00 MiB** — larger images "removed from the request" | [troubleshoot-cloud-agent](https://docs.github.com/en/copilot/how-tos/use-copilot-agents/cloud-agent/troubleshoot-cloud-agent) |
| PR summaries | Files with **>400 combined additions+deletions excluded** | responsible-use/chat |
| Custom agent prompt | **30,000 characters max** | [custom-agents-configuration](https://docs.github.com/en/copilot/reference/custom-agents-configuration) |
| VS Code agent turn | `chat.agent.maxRequests` = **25** | [ai-settings](https://code.visualstudio.com/docs/agents/reference/ai-settings) |
| Subagent nesting | **depth 5** | same |
| Long-context threshold | 272K (GPT-5.4/5.5/5.6 Sol/Terra) · 200K (GPT-5.6 Luna, Gemini 3.1 Pro, Grok 4.5/4.6) — **pricing tier switch, not a block** | [models-and-pricing](https://docs.github.com/en/copilot/reference/copilot-billing/models-and-pricing) |
| Spaces | "have defined size limits, and Copilot Chat only processes a portion of the content" — **numbers not published** | responsible-use/chat |
| Cloud agent scope | One repo, one branch, one PR per task; GitHub-hosted repos only | about-cloud-agent |
| Audit log retention | 180 days | agentic-audit-log-events |
| Streamed audit body | trimmed at **1 MB** (`truncated: true`) | same |
| Agent skills size | **not documented** | — |

**Client version incompatibility:**
> "every new version of Copilot Chat is **only compatible with the latest release of Visual Studio Code**… older clients cannot communicate with the GitHub Copilot servers."

**Minimum versions for correct UBB display** — VS Code **1.120** · VS 2022 **17.14.33** · VS 2026 **18.6.0** · SSMS **22.6** · JetBrains **1.9.1** · Eclipse **0.18.0** · Xcode **0.50.0** · Copilot CLI **1.0.48**. "Older versions will continue to work, but may display incorrect model pricing, inaccurate usage information, or outdated billing terminology."

**Subscription-based routing minimums** — VS Code Chat 0.17+ · JetBrains 1.5.6.5692+ · VS 2022 17.11+.

## Gate 8 — Network: firewall, proxy, TLS

**Required domains** — https://docs.github.com/en/copilot/reference/copilot-allowlist-reference
Discovery: `gh api meta -q '.domains | .website, .copilot'` — **plus apex `github.com`, which is not covered by `*.github.com` and not returned by that query.**

| URL | Purpose |
|---|---|
| `https://github.com/login/*`, `github.githubassets.com`, `avatars.githubusercontent.com` | Auth |
| `https://github.com/copilot/*` | Copilot on GitHub |
| `https://github.com/enterprises/YOUR-ENTERPRISE/*` | EMU auth only |
| `https://api.github.com/user`, `api.github.com/copilot_internal/*` | User management |
| `https://collector.github.com/*`, `copilot-telemetry.githubusercontent.com/telemetry` | Telemetry |
| `https://default.exp-tas.com` | Experimentation |
| `https://copilot-proxy.githubusercontent.com`, `origin-tracker.githubusercontent.com` | Suggestions API |
| `https://*.githubcopilot.com/*` | All plans — **do not allow if using subscription-based routing** |
| `https://*.individual` / `*.business` / `*.enterprise.githubcopilot.com` | Per-plan endpoints |
| `https://copilot-reports.github.com`, `copilot-reports-*.b01.azurefd.net`, `usagereports*.blob.core.windows.net` | Report downloads |

Voice (Foundry Local): `ai.azure.com`, `api.catalog.azureml.ms`, `*.api.azureml.ms`, `amlwlrt4*.blob.core.windows.net`. VS Code also needs `vscode.dev`.
**GHE.com:** `*.SUBDOMAIN.ghe.com` + `SUBDOMAIN.ghe.com`; individual plans and subscription-based routing not supported.

**Subscription-based network routing — an intentional block mechanism** ([manage-network-access](https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-for-enterprise/manage-access/manage-network-access)): allow `*.business`/`*.enterprise.githubcopilot.com`, **blocklist `*.individual.githubcopilot.com`** to stop personal-plan use on the corporate network. Affects inline suggestions, Chat, Chat on GitHub, Mobile, and Copilot CLI.

**Proxy** — https://docs.github.com/en/copilot/how-tos/troubleshoot-copilot/troubleshoot-network-errors
- Documented error: `GitHub Copilot could not connect to server. Extension activation failed: "read ETIMEDOUT" or "read ECONNRESET"`
- ⚠️ "**If your proxy's URL starts `https://`, it is not currently supported by GitHub Copilot.**"
- Auth: **basic or Kerberos only.** SPN derived from proxy URL; override via `http.proxyKerberosServicePrincipal` / JetBrains Network settings / `AGENT_KERBEROS_SERVICE_PRINCIPAL`. **Cannot override SPN in Visual Studio.**
- Visual Studio reads proxy from Windows but **not credentials** → `COPILOT_USE_DEFAULTPROXY=true`.
- Env var priority `HTTPS_PROXY > https_proxy > HTTP_PROXY > http_proxy` — used for **both** HTTP and HTTPS (non-standard).

**TLS inspection** — "certificate signature failure", "unable to verify the first certificate" — "usually caused by a corporate proxy… that uses custom certificates to intercept and inspect secure connections." Cert discovery: win-ca / mac-ca / `/etc/ssl/certs/ca-certificates.crt` / `NODE_EXTRA_CA_CERTS`.

**Diagnostics:**
```
curl --verbose https://copilot-proxy.githubusercontent.com/_ping        # expect 200
curl --verbose -x http://PROXY:PORT -i -L https://copilot-proxy.githubusercontent.com/_ping
curl --verbose https://api.githubcopilot.com/_ping                      # Chat
```
"If the request only succeeds when the `--insecure` flag is added, this may indicate that GitHub Copilot will only connect successfully if you ignore certificate errors."

**Cloud agent's own firewall** — enabled by default with a recommended allowlist covering Azure IMDS (`168.63.129.16`), 11 CAs, container registries, 20 Actions artifact storage accounts (`productionresultssa0…19.blob.core.windows.net`), and package ecosystems for .NET, Dart, Go, Haskell, Java, Node, Perl, PHP, Python, Ruby, Rust, Swift, HashiCorp, Playwright, and Linux distros. Full list: https://docs.github.com/en/copilot/reference/copilot-allowlist-reference#copilot-cloud-agent-recommended-allowlist
Blocked egress is the single most common cause of cloud-agent task failure. Toggle via `is_firewall_enabled` / `is_firewall_recommended_allowlist_enabled` / `custom_allowlist`.

## Gate 9 — Authentication / SSO

- **GHE.com managed users must configure the IDE *before* sign-in** or auth fails — [authenticate-to-ghecom](https://docs.github.com/en/copilot/how-tos/configure-personal-settings/authenticate-to-ghecom):
  - VS Code: `Github-enterprise: Uri` = `https://octocorp.ghe.com` **and** `"github.copilot.advanced": { "authProvider": "github-enterprise" }`
  - JetBrains: plugin **1.4.11+**, Tools → GitHub Copilot → Authentication Provider
  - Xcode "Auth provider URL" · Eclipse "GitHub Enterprise Authentication Endpoint" · CLI `copilot login --host SUBDOMAIN.ghe.com`
- VS Code fix: Sign out → `F1` → *Developer: Reload Window* → sign in.
- **Copilot SDK token types:** `gho_`, `ghu_`, `github_pat_` supported. **`ghp_` classic PATs not supported.** Auth order: explicit `gitHubToken` → `GITHUB_COPILOT_API_TOKEN` → `COPILOT_GITHUB_TOKEN` → `GH_TOKEN` → `GITHUB_TOKEN` → stored CLI OAuth → `gh auth`.
- **Billing REST endpoints: classic PAT only** — "do not support fine-grained personal access tokens."

⚠️ **UNVERIFIED:** IP allow list interaction with Copilot; SAML/SSO session-expiry error strings; PAT/OAuth scope errors — none documented in the Copilot doc tree.

## Gate 10 — Rate limits

See §B11. Documented qualitatively only. Four causes: capacity, high usage, fairness, abuse mitigation.
> "the error message may tell you to wait for your limit to reset, suggest a retry time, or prompt you to upgrade your plan."
> "In case you experience repeated rate limiting in Copilot contact GitHub Support."

⚠️ **No numeric limits, 429 semantics, backoff intervals, or concurrency caps are published.** Treat any specific number as community-reported.
Capacity incidents: https://githubstatus.com — "check GitHub's Status page for any active incidents affecting GitHub Copilot **or model availability**."

## Gate 11 — Model availability

> "**Model availability is subject to change. Some models may be replaced or updated over time.**" — [supported-models](https://docs.github.com/en/copilot/reference/ai-models/supported-models)

Blocks: model not GA in your plan tier; model disabled by policy; model in preview and previews not opted in; Fable 5 without explicit enablement; regional/data-residency restriction (e.g. **Spark is unavailable with data residency**); provider-side outage.

⚠️ **Model-unavailable-in-region behaviour is not documented — UNVERIFIED.**

## Gate 12 — Actions, runners, rulesets (cloud agent only)

- Cloud agent **runs on GitHub Actions**. Actions disabled → agent cannot run.
- **EMU personal repos:** "Copilot cloud agent is not available in personal repositories owned by managed user accounts… runs on **GitHub-hosted runners**, which are not available to personal repositories owned by managed user accounts." → "you may see an error message reporting that **GitHub Actions are not available for your repository**."
- `require_actions_workflow_approval` — a human must approve the workflow run.
- **Rulesets and branch protection** are listed under cloud-agent limitations as capable of "blocking access."
- `require_write_access_for_automation_triggers` — non-write users cannot trigger automations.
- Actions minutes are billed separately and can themselves be exhausted (see §A5).
- **Automations require private or internal repositories.**

---

# PART F — Non-enterprise and individual scenarios

## F1. Plan matrix

**Source:** https://docs.github.com/en/copilot/get-started/plans — "All plans include Copilot CLI and Copilot app."

| Plan | Price | AI credits | Agents | Models |
|---|---|---|---|---|
| **Copilot Free** | Free | "An allowance of GitHub AI Credits" — **number unpublished** | **Limited** | **Auto model selection only** |
| **Copilot Student** | Free | "An allowance" — **unpublished** | ✓ **excludes third-party agents** | Auto only |
| **Copilot Pro** | $10/mo | 1,000 base + 500 flex = **1,500** | ✓ | A selection |
| **Copilot Pro+** | $39/mo | 3,900 + 3,100 = **7,000** | ✓ | Premium models |
| **Copilot Max** | $100/mo | 10,000 + 10,000 = **20,000** | ✓ | **Priority** access |
| **Copilot Business** | $19/seat/mo | **1,900** per user per month | ✓ | Premium models |
| **Copilot Enterprise** | $39/seat/mo | **3,900** per user per month | ✓ | **Priority** access |

**Hard numbers for Free:** 2,000 code completions per month. **Copilot Student: unlimited code completions.** Older "50 chat requests/month" figures no longer appear in current docs and should not be asserted.

Free eligibility: "**only available to individual developers who don't have access to Copilot through an organization or enterprise.**"
"Verified teachers, and maintainers of popular open source projects may be eligible for free access to Copilot Pro."

## F2. What an individual controls that an org member does not

| Setting | Individual | Org-seated (GHEC) |
|---|---|---|
| Suggestions matching public code | Configurable | **Locked** — "you will not be able to configure [it] in your personal account settings" |
| Allow GitHub to use my data for AI model training | Configurable (opt-out, since 24 Apr 2026) | **Not displayed** |
| Cloud agent repository access | No / All / Only selected | Governed by org/enterprise policy |
| Copilot Memory | ON by default | OFF by default; org can force off |
| Automatic code review on own PRs | Pro/Pro+/Max only | Governed by ruleset |
| Personal budget | Yes | Enterprise/org/cost-centre budgets apply |

**When you join a Business/Enterprise plan, your individual plan is cancelled** — "so a user's personal policies cannot conflict with an enterprise's or organization's."

## F3. Individual overage

- "additional usage **may be capped**, so to keep working, you'll need to **pay off any additional usage you've already consumed**."
- GitHub may place "a **temporary authorization hold**."
- **Buying extra credits is not available if you subscribed via GitHub Mobile (iOS/Android).**
- Individual UBB view: Settings → Billing → **AI usage** (individual plans only).

## F4. BYOK — the billing escape hatch

| Path | Copilot subscription needed? | Billing |
|---|---|---|
| **Copilot SDK + BYOK** | **No** — "bypassing GitHub Copilot authentication" | **Direct with your model provider** |
| SDK server-to-server | No user subscription; **organization policy required** | Organization-attributed |
| SDK GitHub user / OAuth / env vars | **Yes** | Copilot |
| **IDE local BYOK** | Not explicitly stated. "removes dependency on GitHub's Copilot API, making it suitable for air-gapped environments **or users without Copilot subscriptions**" | ⚠️ **UNVERIFIED** whether AI credits are consumed |
| Enterprise BYOK (server-side) | — | ⚠️ **UNVERIFIED** |

Sources: [copilot-sdk/auth/byok](https://docs.github.com/en/copilot/how-tos/copilot-sdk/auth/byok) · [auth/authenticate](https://docs.github.com/en/copilot/how-tos/copilot-sdk/auth/authenticate) · [bring-your-own-key](https://docs.github.com/en/copilot/concepts/models/bring-your-own-key)

**BYOK is gated by the "Configure custom models" policy** and **is still content-filtered.**

---

# PART G — Diagnostics and observability

## G1. Triage order for "Copilot isn't working"

1. **https://githubstatus.com** — incidents affecting Copilot *or model availability*.
2. **Seat** — is one assigned, and did it survive the billing cycle?
3. **Usage panel** — IDE status bar / `github.com/settings/copilot`. Credits exhausted? ULB hit?
4. **Policy** — org/enterprise AI controls for that specific surface.
5. **Content exclusion** — test by attaching the file and prompting `explain this file`; it should not be listed as a reference. Remember the **30-minute propagation delay** and reload the window.
6. **Network** — the three `curl … /_ping` commands in Gate 8. If `--insecure` is required, it's TLS inspection.
7. **Client version** — Copilot Chat only supports the latest VS Code release.
8. **Logs** — https://docs.github.com/en/copilot/how-tos/troubleshoot-copilot/view-logs
9. **Cloud agent** — check the Actions run, the firewall allowlist, and the 59-minute cap.

## G2. Audit log events

**Source:** https://docs.github.com/en/enterprise-cloud@latest/admin/monitoring-activity-in-your-enterprise/reviewing-audit-logs-for-your-enterprise/audit-log-events-for-your-enterprise

| Event | Meaning |
|---|---|
| `copilot.access_revoked` | Subscription ended, billing issue, spammy, or suspended |
| `copilot.cfb_enterprise_org_enablement_changed` | Enterprise enablement policy changed |
| **`copilot.cfb_enterprise_settings_changed`** | Enterprise feature settings changed — ⚠️ **no fields identifying which policy** |
| **`copilot.cfb_org_settings_changed`** | Org feature settings changed — ⚠️ same gap |
| `copilot.cfb_seat_added` / `_assignment_created` / `_refreshed` / `_reused` / `_unassigned` / `_cancelled` / `_cancelled_by_staff` | Seat lifecycle |
| `copilot.cfb_seat_management_changed` | Seat management mode changed |
| `copilot.clickwrap_save_event` | Product Terms / Pre-Release Terms accepted |
| **`copilot.content_exclusion_changed`** | Includes `excluded_paths` |
| `copilot.enterprise_enablement_changed` | Copilot enabled/disabled at enterprise level |
| `copilot.memory_user_opt_out` | User opted out of Memory |
| `copilot.plan_changed` / `plan_downgrade_scheduled` | Plan changes |
| `enterprise_team.copilot_assignment` / `_unassignment` | Team licence changes |
| `enterprise_role.create/update/destroy/assign/revoke` | Custom role lifecycle |
| `billing.overage_policy_updated` | Paid-usage policy changed |

🚩 **Real auditability gap:** the two settings-change events carry **no field naming the specific policy that changed**.

**Agentic events** — https://docs.github.com/en/copilot/reference/enterprise-administrators/agentic-audit-log-events
Filter `actor:Copilot`. Retention **180 days**. Fields: `action`, `actor_is_agent`, `agent_session_id`, `user`.

**Streaming Copilot API usage records** (public preview; EMU or GHEC-with-data-residency only): streams `type` (`request`|`response`), `user_id`, `enterprise_id`, `endpoint`, **`body` (JSON-encoded prompt/completion content)**, `@timestamp`, `truncated`, `event_id`, `github_request_id` to a SIEM. **Full prompts and completions can be exported.** Significant data-governance consideration.

## G3. Metrics

- **Copilot usage metrics REST API** requires the **"Copilot usage metrics" policy set to "Enabled everywhere"** — https://docs.github.com/en/rest/copilot/copilot-usage-metrics
- Data properties: https://docs.github.com/en/copilot/reference/metrics-data
- Agent session filters: https://docs.github.com/en/copilot/reference/enterprise-administrators/agent-session-filters

---

# APPENDIX 1 — Legacy premium requests (still live for some)

Premium requests survive **only** for Copilot Pro/Pro+ subscribers on an **existing annual plan**.

- They receive **no new models or features**.
- At plan expiry they **downgrade to Copilot Free**.
- Legacy REST endpoint `GET /organizations/{org}/settings/billing/premium_request/usage` is still live.
- Legacy audit event `billing.overage_policy_updated` still reads "premium request paid usage policy."
- **No global sunset date is published.** Latest possible expiry ≈ May 2027 (INFERRED from a 12-month annual term starting May 2026 — **not stated by GitHub**).

Under the old model, autonomous tool calls inside an agent loop were **free**. Under AI credits, **every model call in the loop is billed.** This is the single largest behavioural change and the reason agent-mode spend rose sharply.

---

# APPENDIX 2 — Consolidated list of things GitHub does not document

Because you asked for a source on every rule, here is the honest list of rules that **have no published source**.

**Billing / pricing**
1. Copilot **Free** and **Student** numeric AI-credit allowance.
2. Token→credit rounding precision.
3. Whether reasoning tokens bill at the output rate.
4. Long-context pricing for 1M-context Claude models.
5. Billing treatment of PR summaries, Extensions, knowledge bases, and the Copilot app.
6. Whether **IDE-local BYOK** and **enterprise BYOK** consume AI credits.
7. Verbatim budget-exhaustion error strings.
8. Whether AI-credit **pool-depletion alerts** exist (the included-usage alert list omits Copilot).
9. Usage-data latency on GitHub surfaces (only Azure's "daily" is published).

**Policies**
10. **No exhaustive policy roster exists.** Confirmed by reading `/copilot/reference` and `/copilot/reference/enterprise-administrators` in full. Best substitutes: `policy-conflicts` (18 rows) + `supported-surfaces-for-policies` (9 rows) + per-feature how-tos.
11. **Defaults for ~12 enterprise policies** (marked ❓ in §D1).
12. A named **Copilot Spaces policy** — docs reference an org "Spaces disabled" state but name no policy, values, default, or location, and say the control **is not enforced** across orgs.
13. **"Agent apps" policy** values and default.
14. **Org-level fine-grained Copilot permissions** — only enterprise custom-role permissions are documented.
15. Spark's literal "Disabled everywhere" value (only "Enabled everywhere" and "Let organizations decide" are named).
16. Whether GHES **ever** had Copilot — current non-availability is confirmed verbatim plus 404s, but no deprecation notice exists.
17. **Copilot SDK policy governance** — server-to-server auth says "organization policy required" without naming the policy.
18. Any toggles on `github.com/settings/copilot/features` beyond Memory and default billing entity.

**Blocking behaviour**
19. **Numeric rate limits**, 429 semantics, backoff intervals, concurrency caps.
20. Whether session/weekly usage limits still apply post-1-June-2026, and at what values.
21. Exact responsible-AI refusal strings.
22. Model-unavailable-in-region behaviour.
23. IP allow list interaction with Copilot.
24. SAML/SSO session-expiry error strings.
25. PAT/OAuth scope errors specific to Copilot.
26. Code review diff / file-size caps; the indexing-incomplete error state.
27. Numeric size limits for **Copilot Spaces** and **agent skills**.
28. **Forks and archived repositories** — genuinely undocumented for cloud agent, code review, indexing, and content-exclusion inheritance. (Inference only: archived repos are read-only so an agent that must push cannot operate — **GitHub does not state this**.)

---

# APPENDIX 3 — Master link index

**Billing and credits**
- https://docs.github.com/en/copilot/concepts/billing/usage-based-billing-for-individuals
- https://docs.github.com/en/copilot/concepts/billing/budgets-for-usage-based-billing
- https://docs.github.com/en/copilot/reference/copilot-billing/models-and-pricing
- https://docs.github.com/en/copilot/reference/copilot-billing/azure-billing
- https://docs.github.com/en/billing/concepts/budgets-and-alerts
- https://docs.github.com/en/billing/reference/billing-reports
- https://docs.github.com/en/billing/reference/azure-billing
- https://docs.github.com/en/rest/billing/usage
- https://docs.github.com/en/copilot/how-tos/manage-and-track-spending/monitor-ai-usage
- https://docs.github.com/en/copilot/tutorials/budgets/getting-started-with-budget-controls
- https://docs.github.com/en/copilot/tutorials/budgets/optimizing-your-budget-configuration
- https://docs.github.com/en/copilot/tutorials/optimize-ai-usage
- https://docs.github.com/en/billing/concepts/product-billing/github-actions

**Plans, models, limits**
- https://docs.github.com/en/copilot/get-started/plans
- https://docs.github.com/en/copilot/reference/ai-models/supported-models
- https://docs.github.com/en/copilot/concepts/models/auto-model-selection
- https://docs.github.com/en/copilot/concepts/models/default-availability
- https://docs.github.com/en/copilot/concepts/models/fallback-and-lts-models
- https://docs.github.com/en/copilot/concepts/models/bring-your-own-key
- https://docs.github.com/en/copilot/concepts/usage-limits
- https://docs.github.com/en/copilot/reference/copilot-feature-matrix

**Policies**
- https://docs.github.com/en/copilot/concepts/policies
- https://docs.github.com/en/copilot/reference/enterprise-administrators/policy-conflicts
- https://docs.github.com/en/copilot/reference/supported-surfaces-for-policies
- https://docs.github.com/en/copilot/reference/enterprise-administrators/enterprise-managed-settings
- https://docs.github.com/en/copilot/reference/enterprise-administrators/mcp-private-registry-enforcement
- https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-for-enterprise/manage-enterprise-policies
- https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-for-organization/manage-policies
- https://docs.github.com/en/copilot/how-tos/manage-your-account/manage-policies
- https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-for-enterprise/manage-availability-of-default-models
- https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-for-organization/manage-default-models
- https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-for-enterprise/enable-custom-models
- https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-for-enterprise/manage-access/grant-access
- https://docs.github.com/en/copilot/tutorials/roll-out-at-scale/govern-at-scale/establish-ai-managers

**Agents**
- https://docs.github.com/en/copilot/concepts/agents/cloud-agent/about-cloud-agent
- https://docs.github.com/en/copilot/concepts/agents/cloud-agent/access-management
- https://docs.github.com/en/copilot/concepts/agents/agent-apps
- https://docs.github.com/en/copilot/concepts/agents/copilot-memory
- https://docs.github.com/en/copilot/concepts/agents/about-agent-skills
- https://docs.github.com/en/copilot/concepts/agents/hooks
- https://docs.github.com/en/copilot/reference/hooks-reference
- https://docs.github.com/en/copilot/reference/custom-agents-configuration
- https://docs.github.com/en/copilot/reference/customization-cheat-sheet
- https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-for-enterprise/manage-agents/enable-copilot-cloud-agent
- https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-for-enterprise/manage-agents/enable-copilot-code-review
- https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-for-enterprise/manage-agents/block-agentic-features
- https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-for-enterprise/manage-agents/configure-enterprise-managed-settings
- https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-for-organization/add-copilot-cloud-agent
- https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-for-organization/configure-runner-for-coding-agent
- https://docs.github.com/en/rest/copilot/copilot-cloud-agent-management

**Content, context, security**
- https://docs.github.com/en/copilot/how-tos/configure-content-exclusion/exclude-content-from-copilot
- https://docs.github.com/en/rest/copilot/copilot-content-exclusion-management
- https://docs.github.com/en/copilot/reference/code-review-excluded-files
- https://docs.github.com/en/copilot/concepts/completions/code-referencing
- https://docs.github.com/en/copilot/concepts/completions/code-suggestions
- https://docs.github.com/en/copilot/concepts/context/repository-indexing
- https://docs.github.com/en/copilot/concepts/context/spaces
- https://docs.github.com/en/copilot/concepts/context/mcp
- https://docs.github.com/en/copilot/responsible-use/chat
- https://docs.github.com/en/copilot/responsible-use/agents
- https://docs.github.com/en/site-policy/other-site-policies/github-and-trade-controls

**Network and troubleshooting**
- https://docs.github.com/en/copilot/reference/copilot-allowlist-reference
- https://docs.github.com/en/copilot/concepts/network-settings
- https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-for-enterprise/manage-access/manage-network-access
- https://docs.github.com/en/copilot/how-tos/troubleshoot-copilot/troubleshoot-common-issues
- https://docs.github.com/en/copilot/how-tos/troubleshoot-copilot/troubleshoot-network-errors
- https://docs.github.com/en/copilot/how-tos/troubleshoot-copilot/view-logs
- https://docs.github.com/en/copilot/how-tos/use-copilot-agents/cloud-agent/troubleshoot-cloud-agent
- https://docs.github.com/en/copilot/how-tos/configure-personal-settings/authenticate-to-ghecom
- https://githubstatus.com

**Audit and metrics**
- https://docs.github.com/en/enterprise-cloud@latest/admin/monitoring-activity-in-your-enterprise/reviewing-audit-logs-for-your-enterprise/audit-log-events-for-your-enterprise
- https://docs.github.com/en/copilot/reference/enterprise-administrators/agentic-audit-log-events
- https://docs.github.com/en/copilot/reference/enterprise-administrators/agent-session-filters
- https://docs.github.com/en/rest/copilot/copilot-usage-metrics
- https://docs.github.com/en/copilot/reference/metrics-data

**SDK**
- https://docs.github.com/en/copilot/how-tos/copilot-sdk
- https://docs.github.com/en/copilot/how-tos/copilot-sdk/auth/authenticate
- https://docs.github.com/en/copilot/how-tos/copilot-sdk/auth/byok
- https://docs.github.com/en/copilot/how-tos/copilot-sdk/features/usage-and-billing

**Client settings**
- https://code.visualstudio.com/docs/agents/reference/ai-settings

**Changelog (JS-rendered; use the RSS feed)**
- https://github.blog/changelog/2026-08-28-upcoming-changes-to-github-copilot-policies-and-billing/
- https://github.blog/changelog/feed/
- https://github.blog/news-insights/company-news/changes-to-github-copilot-individual-plans/

---

*Compiled from docs.github.com, code.visualstudio.com and github.blog. Every rule above carries its source; where GitHub publishes no source, the item is listed in Appendix 2 rather than asserted.*

