using CopilotUsageSimulator.Engine.Guardrails;

namespace CopilotUsageSimulator.BundleTool.Tests;

public sealed class SnapshotIdentityTests
{
    private readonly SnapshotIdentityResolver _resolver = new();

    [Fact]
    public void DuplicateGrantsDoNotInflateSeatInventory()
    {
        var snapshot = ImportFixture.Snapshot();
        snapshot = snapshot with { Seats = [.. snapshot.Seats, snapshot.Seats[0] with { UserLogin = "ALICE" }] };

        var inventory = _resolver.Resolve(snapshot, ImportFixture.Overrides());

        Assert.Equal(2, inventory.Billing.SeatAssignments.Count);
        Assert.Equal("alice", inventory.Attribution.UserId);
        Assert.All(inventory.Billing.SeatAssignments, seat => Assert.Equal("cc-engineering", seat.CostCenterId));
    }

    [Fact]
    public void MultipleLicensingOrganizationsRequireConfirmation()
    {
        var snapshot = ImportFixture.Snapshot();
        snapshot = snapshot with { Seats = [.. snapshot.Seats, snapshot.Seats[0] with { Organization = "research" }] };

        var error = Assert.Throws<ImportException>(() => _resolver.Resolve(snapshot, ImportFixture.Overrides()));
        Assert.Equal("attribution-unresolved", error.Code);

        var overrides = ImportFixture.Overrides() with
        {
            Seats = new Dictionary<string, SeatConfirmation> { ["101"] = new() { LicensingOrganization = "engineering" } }
        };
        var result = _resolver.Resolve(snapshot, overrides);
        Assert.Equal("engineering", result.Attribution.CycleSelectedLicensingOrganizationId);
        Assert.Equal(2, result.Billing.SeatAssignments.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unknown")]
    [InlineData("pro")]
    public void UnknownPlansNeverBecomeBusinessSeats(string? plan)
    {
        var snapshot = ImportFixture.Snapshot();
        snapshot = snapshot with { Seats = [snapshot.Seats[0] with { PlanType = plan }, snapshot.Seats[1]] };

        Assert.Equal("seat-plan-unresolved", Assert.Throws<ImportException>(() =>
            _resolver.Resolve(snapshot, ImportFixture.Overrides())).Code);
    }

    [Fact]
    public void InaccessibleConfigurationIsNotAnEmptyConfiguration()
    {
        var snapshot = ImportFixture.Snapshot();
        snapshot = snapshot with { Sources = snapshot.Sources.Select(source => source.Dataset == "cost-centers" ? source with { Complete = false } : source).ToArray() };

        var error = Assert.Throws<ImportException>(() => _resolver.Resolve(snapshot, ImportFixture.Overrides()));

        Assert.Equal("collection-incomplete", error.Code);
        Assert.Equal(3, error.ExitCode);
    }

    [Fact]
    public void DirectAssignmentTakesPrecedenceOverOldestTeam()
    {
        var snapshot = ImportFixture.Snapshot();
        snapshot = snapshot with
        {
            CostCenters = [.. snapshot.CostCenters, new()
            {
                Id = "cc-team", Name = "Team", State = "active", AiCreditPoolEnabled = false,
                Resources = [new() { Type = "EnterpriseTeam", Name = "ent:team" }]
            }],
            Teams = [new() { Id = "team-1", Name = "Team", Slug = "ent:team", CreatedAt = ImportFixture.Timestamp.AddMonths(-1), Members = ["alice", "bob"] }]
        };

        var result = _resolver.Resolve(snapshot, ImportFixture.Overrides());

        Assert.Equal("cc-engineering", result.Billing.SeatAssignments.Single(seat => seat.UserId == "alice").CostCenterId);
        Assert.Equal("cc-team", result.Billing.SeatAssignments.Single(seat => seat.UserId == "bob").CostCenterId);
        Assert.Equal(AttributionRule.DirectUser, new AttributionResolver().Resolve(result.Attribution, ImportFixture.Timestamp).Rule);
    }

    [Fact]
    public void UnknownOverrideTargetsAreRejected()
    {
        var overrides = ImportFixture.Overrides() with
        {
            Seats = new Dictionary<string, SeatConfirmation> { ["not-observed"] = new() { PlanId = "enterprise" } }
        };

        Assert.Equal("unknown-override-target", Assert.Throws<ImportException>(() =>
            _resolver.Resolve(ImportFixture.Snapshot(), overrides)).Code);
    }
}