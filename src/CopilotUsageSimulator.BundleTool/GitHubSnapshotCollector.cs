using System.Text;
using System.Text.RegularExpressions;

namespace CopilotUsageSimulator.BundleTool;

public sealed record SnapshotCollection(EnterpriseSnapshot Snapshot, ImportReport Report);

public sealed class GitHubSnapshotCollector(GitHubReadClient client, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public Task<SnapshotCollection> CollectAsync(
        string enterprise, string user, CancellationToken cancellationToken = default) =>
        CollectAsync(enterprise, user, false, cancellationToken);

    public async Task<SnapshotCollection> CollectAsync(
        string enterprise, string user, bool collectAllSeatUsage, CancellationToken cancellationToken = default)
    {
        if (!Regex.IsMatch(enterprise, "^[A-Za-z0-9][A-Za-z0-9_-]{0,99}$") || !Regex.IsMatch(user, "^[A-Za-z0-9][A-Za-z0-9_-]{0,99}$"))
            throw new ImportException("invalid-github-identity", "Enterprise and user must be GitHub slugs, not URLs or paths.", 2);
        enterprise = enterprise.ToLowerInvariant();
        user = user.ToLowerInvariant();
        var started = _clock.GetUtcNow();
        var root = $"/enterprises/{Uri.EscapeDataString(enterprise)}";
        var billing = root + "/settings/billing";
        var sources = new List<SourceCoverage>();
        var seats = new List<SeatGrant>();
        var centers = new List<CostCenterObservation>();
        var teams = new List<TeamObservation>();
        var budgets = new List<BudgetObservation>();
        var states = new List<UserBudgetObservation>();
        var usage = new List<UsageObservation>();
        UserBudgetObservation? effective = null;

        async Task<bool> Capture(string dataset, string endpoint, Func<Task> read)
        {
            var before = client.CompletedPages;
            try
            {
                await read();
                sources.Add(new SourceCoverage { Dataset = dataset, Endpoint = endpoint, Complete = true, Pages = client.CompletedPages - before, HttpStatus = 200 });
                return true;
            }
            catch (ImportException exception)
            {
                sources.Add(new SourceCoverage
                {
                    Dataset = dataset, Endpoint = endpoint, Complete = false, Pages = client.CompletedPages - before,
                    HttpStatus = exception.HttpStatus, Problem = $"{exception.Code}: {exception.Message}"
                });
                return false;
            }
        }

        await Capture("seats", root + "/copilot/billing/seats", async () =>
        {
            var result = await client.ReadArrayAsync(root + "/copilot/billing/seats", "seats", cancellationToken);
            seats.AddRange(result.Items.Select(GitHubProjection.Seat).ToArray());
            if (seats.Select(seat => seat.UserId).Distinct(StringComparer.Ordinal).Count() != GitHubProjection.Integer(result.FirstPage, "total_seats"))
                throw new ImportException("seat-count-mismatch", "Unique assignee count disagrees with total_seats. Recollect instead of pruning or duplicating seats.", 3);
        });

        await Capture("cost-centers", billing + "/cost-centers", async () =>
        {
            var result = await client.ReadArrayAsync(billing + "/cost-centers", "costCenters", cancellationToken, paginate: false);
            var complete = true;
            foreach (var center in result.Items)
            {
                var id = GitHubProjection.Id(center, "id");
                if (ImportChecks.Same(GitHubProjection.Text(center, "state"), "deleted"))
                {
                    centers.Add(GitHubProjection.CostCenter(center, GitHubProjection.Array(center, "resources")));
                    continue;
                }
                var path = billing + "/cost-centers/" + Uri.EscapeDataString(id);
                complete &= await Capture($"cost-center:{id}", path, async () =>
                {
                    var detail = await client.ReadArrayAsync(path, "resources", cancellationToken);
                    if (!ImportChecks.Same(GitHubProjection.Id(detail.FirstPage, "id"), id))
                        throw new ImportException("cost-center-identity-mismatch", "Cost-center detail returned a different identity.", 3);
                    centers.Add(GitHubProjection.CostCenter(detail.FirstPage, detail.Items));
                });
            }
            if (!complete) throw new ImportException("cost-center-incomplete", "One or more cost-center resource collections failed.", 3);
            ImportChecks.Unique(centers.Select(center => center.Id), "costCenters.id");
        });

        await Capture("teams", root + "/teams", async () =>
        {
            var result = await client.ReadArrayAsync(root + "/teams", null, cancellationToken);
            var references = centers.Where(center => ImportChecks.Same(center.State, "active"))
                .SelectMany(center => center.Resources).Where(resource => SnapshotIdentityResolver.ResourceKind(resource.Type) == "team")
                .Select(resource => resource.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var selected = result.Items.Where(team => references.Any(reference => ImportChecks.Same(reference, GitHubProjection.Id(team, "id")) ||
                ImportChecks.Same(reference, GitHubProjection.Text(team, "slug")) || ImportChecks.Same(reference, GitHubProjection.Text(team, "name")))).ToArray();
            foreach (var reference in references)
            {
                if (selected.Count(team => ImportChecks.Same(reference, GitHubProjection.Id(team, "id")) ||
                    ImportChecks.Same(reference, GitHubProjection.Text(team, "slug")) || ImportChecks.Same(reference, GitHubProjection.Text(team, "name"))) != 1)
                    throw new ImportException("team-unresolved", "A cost-center enterprise team was missing or ambiguous in the visible team inventory.", 3);
            }
            var complete = true;
            foreach (var team in selected)
            {
                var id = GitHubProjection.Id(team, "id");
                var slug = GitHubProjection.Text(team, "slug");
                var path = root + "/teams/" + Uri.EscapeDataString(slug) + "/memberships";
                complete &= await Capture($"team-members:{id}", path, async () =>
                {
                    var members = await client.ReadArrayAsync(path, null, cancellationToken);
                    var logins = members.Items.Select(member => GitHubProjection.Text(member, "login").ToLowerInvariant()).ToArray();
                    ImportChecks.Unique(logins, "team.members");
                    teams.Add(new TeamObservation
                    {
                        Id = id, Slug = slug, Name = GitHubProjection.Text(team, "name"),
                        CreatedAt = GitHubProjection.Timestamp(team, "created_at") ?? throw new ImportException("team-date-missing", "Team creation date is needed for attribution.", 3),
                        Members = logins.Order(StringComparer.Ordinal).ToArray()
                    });
                });
            }
            if (!complete) throw new ImportException("team-members-incomplete", "One or more cost-center team membership collections failed.", 3);
        });

        await Capture("budgets", billing + "/budgets", async () =>
        {
            var result = await client.ReadArrayAsync(billing + "/budgets", "budgets", cancellationToken);
            budgets.AddRange(result.Items.Select(GitHubProjection.Budget).ToArray());
            ImportChecks.Unique(budgets.Select(budget => budget.Id), "budgets.id");
            if (GitHubProjection.Optional(result.FirstPage, "total_count").ValueKind != System.Text.Json.JsonValueKind.Undefined &&
                budgets.Count != GitHubProjection.Integer(result.FirstPage, "total_count"))
                throw new ImportException("budget-count-mismatch", "Collected budget count disagrees with the reported total.", 3);
        });

        if (budgets.Any(budget => budget.Scope is "user" or "multi_user_customer" or "multi_user_cost_center"))
        {
            var path = billing + "/budgets?user=" + Uri.EscapeDataString(user);
            await Capture("effective-user-budget", path, async () =>
            {
                var result = await client.ReadArrayAsync(path, "budgets", cancellationToken);
                if (!ImportChecks.Same(GitHubProjection.Text(result.FirstPage, "user"), user))
                    throw new ImportException("budget-user-mismatch", "Filtered budget response did not identify the selected user.", 3);
                var value = GitHubProjection.Optional(result.FirstPage, "effective_budget");
                if (value.ValueKind != System.Text.Json.JsonValueKind.Undefined)
                {
                    effective = new UserBudgetObservation
                    {
                        BudgetId = GitHubProjection.Id(value, "id"), User = user,
                        ConsumedAmount = GitHubProjection.Number(value, "consumed_amount"), TargetAmount = GitHubProjection.Number(value, "budget_amount")
                    };
                }
            });
        }

        foreach (var budget in budgets.Where(budget => budget.Scope is "multi_user_customer" or "multi_user_cost_center").OrderBy(budget => budget.Id, StringComparer.Ordinal))
        {
            var path = billing + "/budgets/" + Uri.EscapeDataString(budget.Id) + "/user-states?user=" + Uri.EscapeDataString(user);
            await Capture($"user-budget-state:{budget.Id}", path, async () =>
            {
                var result = await client.ReadArrayAsync(path, "user_states", cancellationToken);
                if (result.Items.Any(state => !ImportChecks.Same(GitHubProjection.Text(state, "user"), user)))
                    throw new ImportException("budget-user-mismatch", "Per-user state response included a different user; no unrelated user consumption was retained.", 3);
                if (result.Items.Count > 1) throw new ImportException("duplicate-user-budget-state", "Repeated selected-user state cannot establish a reliable balance.", 3);
                states.AddRange(result.Items.Select(state => new UserBudgetObservation
                {
                    BudgetId = budget.Id, User = user, ConsumedAmount = GitHubProjection.Number(state, "consumed_amount"),
                    TargetAmount = GitHubProjection.Number(state, "target_amount"), OverrideBudgetId = GitHubProjection.OptionalText(state, "override_budget_id")
                }));
            });
        }

        var period = $"year={started.Year}&month={started.Month}";
        var aiPath = billing + "/ai_credit/usage?" + period + "&user=" + Uri.EscapeDataString(user);
        await Capture("ai-credit-usage", aiPath, async () => usage.Add(GitHubProjection.Usage(
            await client.ReadObjectAsync(aiPath, cancellationToken), "ai-credit", enterprise, user, _clock.GetUtcNow())));
        if (collectAllSeatUsage)
        {
            var aggregatePath = billing + "/ai_credit/usage?" + period;
            await Capture("ai-credit-usage:enterprise", aggregatePath, async () => usage.Add(GitHubProjection.Usage(
                await client.ReadObjectAsync(aggregatePath, cancellationToken), "ai-credit", enterprise, null, _clock.GetUtcNow())));
            foreach (var seatUser in seats.Select(seat => seat.UserLogin).Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(login => !ImportChecks.Same(login, user)).Order(StringComparer.Ordinal))
            {
                var path = billing + "/ai_credit/usage?" + period + "&user=" + Uri.EscapeDataString(seatUser);
                await Capture($"ai-credit-usage:user:{seatUser}", path, async () => usage.Add(GitHubProjection.Usage(
                    await client.ReadObjectAsync(path, cancellationToken), "ai-credit", enterprise, seatUser, _clock.GetUtcNow())));
            }
        }
        var actionsPath = billing + "/usage/summary?" + period + "&product=actions";
        await Capture("actions-usage", actionsPath, async () => usage.Add(GitHubProjection.Usage(
            await client.ReadObjectAsync(actionsPath, cancellationToken), "actions", enterprise, null, _clock.GetUtcNow())));

        var ended = _clock.GetUtcNow();
        if (started.Year != ended.Year || started.Month != ended.Month || ended < started)
            sources.Add(new SourceCoverage { Dataset = "capture-period", Endpoint = "clock", Complete = false, Pages = 0, Problem = "Capture crossed a UTC billing boundary; recollect." });
        var snapshot = new EnterpriseSnapshot
        {
            Schema = EnterpriseSnapshot.Format, SchemaVersion = 1, Enterprise = enterprise, SelectedUser = user,
            ApiVersion = GitHubReadClient.ApiVersion, CapturedAt = started, CaptureCompletedAt = ended,
            Sources = sources.OrderBy(source => source.Dataset, StringComparer.Ordinal).ToArray(),
            Seats = seats.OrderBy(seat => seat.UserId, StringComparer.Ordinal).ThenBy(seat => seat.Organization, StringComparer.Ordinal).ThenBy(seat => seat.AssigningTeam, StringComparer.Ordinal).ToArray(),
            CostCenters = centers.OrderBy(center => center.Id, StringComparer.Ordinal).ToArray(),
            Teams = teams.OrderBy(team => team.Id, StringComparer.Ordinal).ToArray(),
            Budgets = budgets.OrderBy(budget => budget.Id, StringComparer.Ordinal).ToArray(),
            UserBudgetStates = states, EffectiveUserBudget = effective,
            UsageReports = usage.OrderBy(report => report.Kind, StringComparer.Ordinal)
                .ThenBy(report => report.User is null ? 0 : 1).ThenBy(report => report.User, StringComparer.Ordinal).ToArray()
        };
        if (Encoding.UTF8.GetByteCount(ImportJson.Write(snapshot)) > ImportFiles.MaximumSnapshotBytes)
            throw new ImportException("snapshot-too-large", "The complete snapshot exceeds 64 MiB; no inventory was pruned.", 3);
        return new SnapshotCollection(snapshot, new ImportReport
        {
            Command = "collect", Enterprise = enterprise, User = user, CapturedAt = ended,
            Diagnostics = [.. snapshot.Sources.Select(source => new ImportDiagnostic(source.Complete ? "info" : "error",
                source.Complete ? "source-collected" : "source-incomplete", source.Dataset,
                source.Complete ? $"Collected {source.Pages} response pages from {source.Endpoint}." : source.Problem!)),
                new("warning", "confirmations-required", "snapshot", "This is source evidence, not a complete simulation. Supply workload, pool consumption/entitlement, exclusions and any unresolved settings to create."),
                new("warning", "reported-not-live", "usageReports", "Reports cover the requested billing month. The APIs do not establish a current reporting cutoff; reportedThrough remains null.")]
        });
    }
}