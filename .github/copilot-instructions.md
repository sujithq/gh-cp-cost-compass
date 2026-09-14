# Repository-wide Copilot instructions

Apply these instructions to every task in this repository.

## Project context

- This is a .NET 11 solution for simulating GitHub Copilot usage, guardrails, allocation, and GitHub Actions charges.
- `src/CopilotUsageSimulator.Common` owns shared metadata, documentation contracts, and stable identifiers.
- `src/CopilotUsageSimulator.Engine` is a deterministic, UI-independent simulation library. It must not depend on web, presentation, browser, or persistence concerns.
- `src/CopilotUsageSimulator.Web` is the first client of the engine and is a standalone Blazor WebAssembly host. Additional engine clients are expected.
- Tests use xUnit and bUnit under the matching projects in `tests`.
- The SDK is pinned by `global.json` and installed locally under `.dotnet`. On Windows, invoke it with `.\.dotnet\dotnet.exe`.

## Package sources

- For local npm commands that access a registry, use `--registry=https://packagefeedproxy.microsoft.io/npm/`.
- GitHub Copilot coding agent (CCA) runs may use their default npm registry configuration.
- Do not commit a repository `.npmrc` that redirects CCA to the local npm proxy.

## Engineering rules

- Inspect the current working tree, including uncommitted changes, before making or reviewing changes.
- Preserve user changes and never revert unrelated work.
- Make focused changes that solve the root cause without modifying unrelated code.
- Preserve deterministic simulation behavior, ordered explanations, stable identifiers, first-failing-gate semantics, and projected balances.
- Keep guardrail applicability, entitlement calculations, and state transitions based on the same selected entity identity.
- Keep as much business logic as possible in the engine so every client receives the same behavior. Keep only rendering, browser storage, UI state, and client-specific orchestration in the web project.
- Do not solve reusable domain requirements only in the Blazor client. Expose client-neutral engine contracts or services instead.
- Reuse existing calculators, applicability resolvers, adapters, metadata keys, and validation boundaries instead of duplicating domain logic.
- Prefer small coordinators with explicit ordered stages over large workflows or abstractions that hide domain sequencing.
- Maintain nullable-reference and type safety. Do not suppress errors with broad catches, silent defaults, or unnecessary casts.
- Add or update tests for behavior changes and regressions. Include terminal and malformed-input paths when relevant.
- Update directly related documentation when public behavior or usage changes.

## Validation

- Use the smallest relevant test project first, then run the full suite when shared engine or contract behavior changes.
- Use Release configuration for final validation:

  ```powershell
  .\.dotnet\dotnet.exe test CopilotUsageSimulator.slnx --configuration Release
  .\.dotnet\dotnet.exe build CopilotUsageSimulator.slnx --configuration Release --no-restore
  ```

- Treat test failures, build errors, and new warnings as unresolved unless they are clearly pre-existing and unrelated.
- Run `git diff --check` before completing code changes.
- Documentation-only changes do not require build or test execution.

## Reviews

- Reviews are read-only unless the user explicitly asks for fixes, except for the required review-ledger update below.
- Review the latest working tree, including staged, unstaged, and untracked files.
- Assess maintainability across the whole solution, including project boundaries, shared contracts, engine behavior, every client, tests, configuration, and documentation. Do not limit the review to recently changed files.
- Treat business logic implemented or duplicated in a client as a maintainability risk when it could be owned by the reusable engine.
- Check that the engine remains client-neutral and that clients do not become required dependencies of engine behavior.
- Report only concrete issues that materially affect correctness, maintainability, extensibility, security, or regression risk.
- Always sort review findings and low-hanging-fruit tables by descending severity: Critical, High, Medium, Low. Within one severity, rank by impact and then implementation effort.
- Cite repository-relative paths and current line numbers.
- Whenever the user requests a review, update `docs/MAINTAINABILITY-REVIEW.md` in the same turn with the review date, scope, current findings, severity, effort, resolution status, ranked low-hanging fruit, dependencies, and validation baseline.
- Preserve useful resolved findings in the review ledger, add stable finding IDs for new issues, and revise or retire entries that no longer match the current implementation. Do not modify implementation code unless the user also asks for fixes.
- Follow any requested report format exactly and omit unrelated commentary.

## Responses and commits

- Summarize changed behavior and validation concisely.
- Do not create a commit unless the user explicitly requests one.
- End every implementation response with a suggested Conventional Commit message in the form `type(scope): description`.
- If the user requests a report-only response or another exact output format, follow that format instead of appending a commit message.
