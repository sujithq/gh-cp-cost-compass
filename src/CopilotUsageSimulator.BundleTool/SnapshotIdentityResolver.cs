using CopilotUsageSimulator.Engine.Guardrails;

namespace CopilotUsageSimulator.BundleTool;

public sealed record ResolvedInventory(BillingContext Billing, AttributionInput Attribution, string PlanId);

public sealed class SnapshotIdentityResolver
{
    public ResolvedInventory Resolve(EnterpriseSnapshot snapshot, ImportOverrides overrides)
    {
        Validate(snapshot, overrides);
        var timestamp = snapshot.CaptureCompletedAt.ToUniversalTime();
        var cycleStart = new DateTimeOffset(timestamp.Year, timestamp.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var costCenters = snapshot.CostCenters.Where(center => ImportChecks.Same(center.State, "active")).ToArray();
        var seats = new List<EffectiveSeatAssignment>();
        AttributionInput? selectedAttribution = null;
        string? selectedPlan = null;
        var logins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var grants in snapshot.Seats.GroupBy(seat => seat.UserId).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var loginNames = grants.Select(grant => grant.UserLogin.ToLowerInvariant()).Distinct().ToArray();
            ImportChecks.Require(loginNames.Length == 1 && logins.Add(loginNames[0]),
                "conflicting-user-identity", $"Seat identity '{grants.Key}' has conflicting or reused logins.");
            var login = loginNames[0];
            var confirmation = ImportChecks.Find(overrides.Seats, grants.Key);
            var plans = grants.Select(grant => grant.PlanType?.ToLowerInvariant()).Distinct().ToArray();
            var plan = confirmation?.PlanId?.ToLowerInvariant() ?? (plans.Length == 1 ? plans[0] : null);
            ImportChecks.Require(plan is "business" or "enterprise", "seat-plan-unresolved",
                $"Confirm seats.{grants.Key}.planId: the effective plan for '{login}' is missing, unknown, or conflicting.");

            var organizations = grants.Where(grant => grant.Organization is not null)
                .Select(grant => grant.Organization!.ToLowerInvariant()).Distinct()
                .Order(StringComparer.Ordinal).ToArray();
            var selectedOrganization = confirmation?.LicensingOrganization;
            if (selectedOrganization is not null)
            {
                ImportChecks.Require(organizations.Contains(selectedOrganization, StringComparer.OrdinalIgnoreCase),
                    "licensing-source-unobserved", $"seats.{grants.Key}.licensingOrganization must match an observed license grant.");
            }

            var direct = costCenters.Where(center => center.Resources.Any(resource =>
                    ResourceKind(resource.Type) == "user" && ImportChecks.Same(resource.Name, login)))
                .Select(center => new EffectiveCostCenterAssignment
                {
                    CostCenterId = center.Id, EffectiveFrom = timestamp
                }).ToArray();
            var teamAssignments = new List<EffectiveTeamCostCenterAssignment>();
            foreach (var center in costCenters)
            {
                foreach (var resource in center.Resources.Where(resource => ResourceKind(resource.Type) == "team"))
                {
                    var candidates = snapshot.Teams.Where(team => ImportChecks.Same(team.Slug, resource.Name) ||
                        ImportChecks.Same(team.Id, resource.Name) || ImportChecks.Same(team.Name, resource.Name)).ToArray();
                    ImportChecks.Require(candidates.Length == 1, "team-unresolved",
                        $"Cost center '{center.Id}' references a missing or ambiguous enterprise team '{resource.Name}'.");
                    var team = candidates[0];
                    if (team.Members.Contains(login, StringComparer.OrdinalIgnoreCase))
                    {
                        teamAssignments.Add(new EffectiveTeamCostCenterAssignment
                        {
                            TeamId = team.Id, CostCenterId = center.Id,
                            TeamCreatedAt = team.CreatedAt, EffectiveFrom = timestamp
                        });
                    }
                }
            }

            var attribution = new AttributionInput
            {
                UserId = login,
                LicensingOrganizationIds = organizations,
                CycleSelectedLicensingOrganizationId = selectedOrganization,
                DirectAssignments = direct,
                TeamAssignments = teamAssignments.DistinctBy(team => (team.TeamId, team.CostCenterId)).ToArray(),
                OrganizationAssignments = costCenters.SelectMany(center => center.Resources
                    .Where(resource => ResourceKind(resource.Type) == "organization")
                    .Select(resource => new EffectiveOrganizationCostCenterAssignment
                    {
                        OrganizationId = resource.Name, CostCenterId = center.Id, EffectiveFrom = timestamp
                    })).DistinctBy(assignment => (assignment.OrganizationId, assignment.CostCenterId)).ToArray()
            };
            var resolved = new AttributionResolver().Resolve(attribution, timestamp);
            ImportChecks.Require(resolved.Outcome == GuardrailOutcome.Passed, "attribution-unresolved",
                $"Cannot resolve '{login}': {resolved.Explanation} Confirm the cycle-selected licensing organization when needed.");
            seats.Add(new EffectiveSeatAssignment
            {
                UserId = login, PlanId = plan!, CostCenterId = resolved.CostCenterId, EffectiveFrom = timestamp
            });
            if (ImportChecks.Same(login, snapshot.SelectedUser))
            {
                selectedAttribution = attribution;
                selectedPlan = plan;
            }
        }

        ImportChecks.Require(selectedAttribution is not null, "selected-user-missing",
            $"'{snapshot.SelectedUser}' has no reconciled Copilot seat in this snapshot.");
        return new ResolvedInventory(new BillingContext
        {
            BillingEntityId = snapshot.Enterprise,
            CycleStart = cycleStart,
            CycleEnd = cycleStart.AddMonths(1),
            SeatAssignments = seats.OrderBy(seat => seat.UserId, StringComparer.Ordinal).ToArray()
        }, selectedAttribution!, selectedPlan!);
    }

    internal static string ResourceKind(string type) => type.ToLowerInvariant() switch
    {
        "user" => "user",
        "organization" => "organization",
        "repository" => "repository",
        "team" or "enterprise_team" or "enterprise-team" or "enterpriseteam" => "team",
        _ => throw new ImportException("resource-type-unsupported", $"Unsupported cost-center resource type '{type}'.")
    };

    private static void Validate(EnterpriseSnapshot snapshot, ImportOverrides overrides)
    {
        ImportChecks.Require(snapshot.Schema == EnterpriseSnapshot.Format && snapshot.SchemaVersion == 1 && overrides.SchemaVersion == 1,
            "schema-unsupported", "Expected enterprise snapshot and overrides schema version 1.");
        ImportChecks.Identifier(snapshot.Enterprise, "enterprise");
        ImportChecks.Identifier(snapshot.SelectedUser, "selectedUser");
        ImportChecks.Require(snapshot.CapturedAt != default && snapshot.CaptureCompletedAt >= snapshot.CapturedAt &&
            snapshot.CaptureCompletedAt.UtcDateTime.Year == snapshot.CapturedAt.UtcDateTime.Year &&
            snapshot.CaptureCompletedAt.UtcDateTime.Month == snapshot.CapturedAt.UtcDateTime.Month,
            "capture-period-invalid", "Capture must have a valid start/end within one UTC billing month.");
        ImportChecks.Items(snapshot.Sources, "sources");
        ImportChecks.Unique(snapshot.Sources.Select(source => source.Dataset), "sources.dataset");
        foreach (var dataset in new[] { "seats", "cost-centers", "teams", "budgets" })
        {
            if (snapshot.Sources.SingleOrDefault(source => source.Dataset == dataset)?.Complete != true)
            {
                throw new ImportException("collection-incomplete", $"The '{dataset}' collection is incomplete or inaccessible; absence is not an empty configuration.", 3);
            }
        }

        ImportChecks.Items(snapshot.Seats, "seats");
        ImportChecks.Items(snapshot.CostCenters, "costCenters");
        ImportChecks.Items(snapshot.Teams, "teams");
        ImportChecks.Items(snapshot.Budgets, "budgets");
        ImportChecks.Items(snapshot.UserBudgetStates, "userBudgetStates");
        ImportChecks.Items(snapshot.UsageReports, "usageReports");
        ImportChecks.Require(snapshot.UsageReports.GroupBy(report => (report.Kind.ToLowerInvariant(), report.User?.ToLowerInvariant())).All(group => group.Count() == 1),
            "duplicate-usage-report", "Usage evidence must contain at most one report per kind and user for the capture month.");
        ImportChecks.Unique(snapshot.CostCenters.Select(center => center.Id), "costCenters.id");
        ImportChecks.Unique(snapshot.Teams.Select(team => team.Id), "teams.id");
        ImportChecks.Unique(snapshot.Budgets.Select(budget => budget.Id), "budgets.id");
        foreach (var seat in snapshot.Seats)
        {
            ImportChecks.Identifier(seat.UserId, "seats.userId");
            ImportChecks.Identifier(seat.UserLogin, "seats.userLogin");
            if (seat.Organization is not null) ImportChecks.Identifier(seat.Organization, "seats.organization");
            ImportChecks.Require(seat.CreatedAt is null || seat.CreatedAt <= snapshot.CaptureCompletedAt,
                "seat-date-invalid", "Seat creation cannot occur after the capture ended.");
            ImportChecks.Require(seat.PendingCancellationDate is null ||
                seat.PendingCancellationDate > DateOnly.FromDateTime(snapshot.CaptureCompletedAt.UtcDateTime),
                "seat-cancellation-unresolved", $"Seat '{seat.UserId}' is pending cancellation at/before capture; reconcile effective access and entitlement first.");
        }
        foreach (var center in snapshot.CostCenters)
        {
            ImportChecks.Identifier(center.Name, "costCenters.name");
            ImportChecks.Require(center.State is not null && (ImportChecks.Same(center.State, "active") || ImportChecks.Same(center.State, "deleted")),
                "cost-center-state-unknown", $"Cost center '{center.Id}' has an unknown state.");
            ImportChecks.Items(center.Resources, "costCenters.resources");
            foreach (var resource in center.Resources)
            {
                ImportChecks.Identifier(resource.Name, "costCenters.resources.name");
                ResourceKind(ImportChecks.Identifier(resource.Type, "costCenters.resources.type"));
            }
        }
        foreach (var team in snapshot.Teams)
        {
            ImportChecks.Identifier(team.Slug, "teams.slug");
            ImportChecks.Identifier(team.Name, "teams.name");
            ImportChecks.Items(team.Members, "teams.members");
            ImportChecks.Unique(team.Members, "teams.members");
            ImportChecks.Require(team.CreatedAt != default && team.CreatedAt <= snapshot.CaptureCompletedAt,
                "team-date-unresolved", $"Team '{team.Id}' needs a creation date at or before capture for attribution precedence.");
        }
        foreach (var state in snapshot.UserBudgetStates.Concat(snapshot.EffectiveUserBudget is { } effective ? [effective] : []))
        {
            ImportChecks.Identifier(state.BudgetId, "userBudgetStates.budgetId");
            ImportChecks.Identifier(state.User, "userBudgetStates.user");
            ImportChecks.Require(snapshot.Budgets.Any(budget => ImportChecks.Same(budget.Id, state.BudgetId)),
                "user-budget-state-unobserved", "Per-user budget state must reference an observed budget definition.");
        }
        foreach (var report in snapshot.UsageReports)
        {
            ImportChecks.Require(report.Kind is "ai-credit" or "actions" &&
                report.Year == snapshot.CaptureCompletedAt.UtcDateTime.Year && report.Month == snapshot.CaptureCompletedAt.UtcDateTime.Month &&
                (report.Kind != "ai-credit" || report.User is null || snapshot.Seats.Any(seat => ImportChecks.Same(seat.UserLogin, report.User))),
                "usage-period-mismatch", "Usage evidence must refer to the snapshot billing month, enterprise aggregate, or an observed seated user.");
            ImportChecks.Require((report.ObservedAt is null || (report.ObservedAt >= snapshot.CapturedAt && report.ObservedAt <= snapshot.CaptureCompletedAt)) &&
                (report.ReportedThrough is null || report.ReportedThrough <= (report.ObservedAt ?? snapshot.CaptureCompletedAt)),
                "usage-cutoff-invalid", "A reporting cutoff cannot be newer than its observation, which must fall inside the capture interval.");
            ImportChecks.Items(report.Items, "usageReports.items");
        }
        ImportChecks.KnownOverrides(overrides.Seats, snapshot.Seats.Select(seat => seat.UserId), "seats");
        ImportChecks.KnownOverrides(overrides.IncludedControls, snapshot.CostCenters.Select(center => center.Id), "includedControls");
        ImportChecks.KnownOverrides(overrides.Budgets, snapshot.Budgets.Select(budget => budget.Id), "budgets");
    }
}