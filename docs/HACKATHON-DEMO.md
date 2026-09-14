# Hackathon Jury Demo

Maximum duration: 2 minutes

## 0:00-0:15 - Open the application

Open the Copilot Usage Simulator.

> This is the Copilot Usage Simulator. It predicts Copilot AI-credit and GitHub Actions costs, applies enterprise guardrails, and explains whether a request will run or be blocked.

## 0:15-0:40 - Run a standard simulation

1. Select **Cloud agent**.
2. Select **Apply overrides and simulate**.
3. Point to the cost summary and **Allowed** decision.

> We start from a realistic workload. The simulator calculates included and metered AI credits, Actions cost, and billing attribution using a deterministic, reusable .NET engine.

## 0:40-1:10 - Show explainable blocking

1. Select **User-level budget exceeded**.
2. Select **Apply overrides and simulate**.
3. Point to **Why it stopped** and the first failed check.

> The same engine evaluates guardrails in their required order. This request is blocked by the user-level budget. Instead of showing only an error, the application identifies the first failing gate, configured limit, current consumption, and projected balance.

## 1:10-1:40 - Fix a configuration problem

1. Select **Paid usage not authorized for GitHub Copilot**.
2. Point to **Authorized product IDs**, which contains `github-actions` while the request uses `github-copilot`.
3. Change the value to `github-copilot`.
4. Select **Apply overrides and simulate**.
5. Point to the new decision and the GitHub Docs links.

> Administrators can test configuration problems safely. Here, paid usage is enabled, but the simulated authorization scope does not include GitHub Copilot. After correcting the configuration, the applicability block is removed. Each control links to the relevant GitHub documentation.

## 1:40-2:00 - Close

> The key value is explainability: teams can model Copilot and Actions usage, understand costs before deployment, reproduce blocked scenarios, and validate policy changes without affecting production. The engine is UI-independent, so the same deterministic behavior can support additional clients.

## Rehearsal Notes

- Open the application and position it at **Start From** before the timer begins.
- Keep the browser zoom and window size fixed.
- Do not open the scenario JSON during the timed demo.
- If time is short, omit the sentence about the UI-independent engine.
