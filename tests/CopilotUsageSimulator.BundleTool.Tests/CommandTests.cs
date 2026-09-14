using System.Text;
using System.Text.Json.Nodes;
using CopilotUsageSimulator.Bundles;

namespace CopilotUsageSimulator.BundleTool.Tests;

public sealed class CommandTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"compass-tool-tests-{Guid.NewGuid():N}");

    public CommandTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task CreateThenValidateProducesImportableV1Bundle()
    {
        var inputs = await WriteInputs();
        var target = Path.Combine(_directory, "compass.json");
        var error = new StringWriter();
        var output = new StringWriter();
        var app = new BundleToolApplication();

        var code = await app.RunAsync(["create", .. inputs, "--output", target], output, error);

        Assert.Equal(0, code);
        Assert.Empty(output.ToString());
        Assert.True(File.Exists(target + ".report.json"));
        var validation = BundleInspector.Validate(await File.ReadAllTextAsync(target));
        Assert.Equal("Blocked", validation.PreviewDecision);
        var report = ImportJson.Read<ImportReport>(await File.ReadAllTextAsync(target + ".report.json"));
        Assert.All(validation.Diagnostics, diagnostic => Assert.Contains(diagnostic, report.Diagnostics));
        Assert.Equal(0, await app.RunAsync(["validate", target], output, error));
        Assert.Contains("\"previewDecision\": \"Blocked\"", output.ToString());
    }

    [Fact]
    public async Task MissingDataPreservesExistingBundleAndWritesFailureReport()
    {
        var inputs = await WriteInputs(ImportFixture.Overrides() with { EnterprisePoolConsumedCredits = null });
        var target = Path.Combine(_directory, "compass.json");
        await File.WriteAllTextAsync(target, "existing bundle");

        var code = await new BundleToolApplication().RunAsync(["create", .. inputs, "--output", target, "--overwrite"], new StringWriter(), new StringWriter());

        Assert.Equal(4, code);
        Assert.Equal("existing bundle", await File.ReadAllTextAsync(target));
        Assert.Contains("missing-financial-value", await File.ReadAllTextAsync(target + ".report.json"));
    }

    [Fact]
    public async Task FingerprintFailureHasIncompatibleBundleExitCodeAndPreservesInput()
    {
        var inputs = await WriteInputs();
        var output = new StringWriter();
        var app = new BundleToolApplication();
        Assert.Equal(0, await app.RunAsync(["create", .. inputs, "--output", "-"], output, new StringWriter()));
        var bundle = JsonNode.Parse(output.ToString())!;
        bundle["catalogSha256"] = new string('0', 64);
        var path = Path.Combine(_directory, "mismatched.json");
        var before = bundle.ToJsonString();
        await File.WriteAllTextAsync(path, before);
        var error = new StringWriter();

        Assert.Equal(4, await app.RunAsync(["validate", path], new StringWriter(), error));
        Assert.Contains("bundle-incompatible", error.ToString());
        Assert.Equal(before, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task JsonSyntaxFailureHasInvalidInputExitCode()
    {
        var path = Path.Combine(_directory, "malformed.json");
        await File.WriteAllTextAsync(path, "{");

        Assert.Equal(2, await new BundleToolApplication().RunAsync(["validate", path], new StringWriter(), new StringWriter()));
    }

    [Fact]
    public async Task StdoutIsOnlyBundleJson()
    {
        var inputs = await WriteInputs();
        var output = new StringWriter();

        Assert.Equal(0, await new BundleToolApplication().RunAsync(["create", .. inputs, "--output", "-"], output, new StringWriter()));
        Assert.Equal("Blocked", BundleInspector.Validate(output.ToString()).PreviewDecision);
    }

    [Fact]
    public async Task RefusesToOverwriteInputsEvenWhenOverwriteRequested()
    {
        var inputs = await WriteInputs();
        var before = await File.ReadAllTextAsync(inputs[1]);

        var code = await new BundleToolApplication().RunAsync(["create", .. inputs, "--output", inputs[1], "--overwrite"], new StringWriter(), new StringWriter());

        Assert.Equal(2, code);
        Assert.Equal(before, await File.ReadAllTextAsync(inputs[1]));
    }

    [Fact]
    public async Task RefusesExistingOutputWithoutExplicitOverwrite()
    {
        var inputs = await WriteInputs();
        var target = Path.Combine(_directory, "compass.json");
        await File.WriteAllTextAsync(target, "existing");

        Assert.Equal(5, await new BundleToolApplication().RunAsync(["create", .. inputs, "--output", target], new StringWriter(), new StringWriter()));
        Assert.Equal("existing", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task CancelledWriteDoesNotReplacePreviousOutput()
    {
        var path = Path.Combine(_directory, "atomic.json");
        await File.WriteAllTextAsync(path, "previous");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ImportFiles.WriteAtomicAsync(path, "next", true, cancellation.Token));

        Assert.Equal("previous", await File.ReadAllTextAsync(path));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task MultibyteInputLimitCountsBytesNotCharacters()
    {
        var path = Path.Combine(_directory, "unicode.json");
        await File.WriteAllTextAsync(path, new string('\u00e9', 20), new UTF8Encoding(false));

        Assert.Equal("input-too-large", (await Assert.ThrowsAsync<ImportException>(() =>
            ImportFiles.ReadAsync(path, 25, CancellationToken.None))).Code);
    }

    [Fact]
    public async Task UnknownArgumentsDoNotEchoPossibleSecrets()
    {
        var error = new StringWriter();

        Assert.Equal(2, await new BundleToolApplication().RunAsync(["create", "--token", "pretend-secret"], new StringWriter(), error));
        Assert.DoesNotContain("pretend-secret", error.ToString());
    }

    [Fact]
    public async Task CollectionUsesNamedEnvironmentTokenAndReturnsReplayableJson()
    {
        var output = new StringWriter();
        var names = new List<string>();
        var app = new BundleToolApplication(name => { names.Add(name); return "test-token"; },
            () => new HttpClient(new GitHubCollectorTests.FixtureHandler()), new GitHubCollectorTests.FixedClock());

        Assert.Equal(0, await app.RunAsync(["collect", "--enterprise", "example", "--user", "alice", "--all-seat-usage", "--token-env", "COMPASS_READ_TOKEN", "--output", "-"], output, new StringWriter()));
        Assert.Equal(["COMPASS_READ_TOKEN"], names);
        var snapshot = ImportJson.Read<EnterpriseSnapshot>(output.ToString());
        Assert.Equal("alice", snapshot.SelectedUser);
        Assert.Equal(3, snapshot.UsageReports.Count(report => report.Kind == "ai-credit"));
        Assert.DoesNotContain("test-token", output.ToString());
    }

    [Fact]
    public async Task IncompleteCollectionStillSavesItsCoverageEvidence()
    {
        var target = Path.Combine(_directory, "partial.json");
        var app = new BundleToolApplication(_ => "test-token",
            () => new HttpClient(new GitHubCollectorTests.FixtureHandler { FailedPath = "/enterprises/example/teams/ent:developers/memberships" }),
            new GitHubCollectorTests.FixedClock());

        Assert.Equal(3, await app.RunAsync(["collect", "--enterprise", "example", "--user", "alice", "--output", target], new StringWriter(), new StringWriter()));
        var snapshot = ImportJson.Read<EnterpriseSnapshot>(await File.ReadAllTextAsync(target));
        Assert.False(snapshot.Sources.Single(source => source.Dataset == "teams").Complete);
        Assert.Contains("source-incomplete", await File.ReadAllTextAsync(target + ".report.json"));
    }

    [Fact]
    public async Task MissingCredentialsNeverStartsHttpCollection()
    {
        var app = new BundleToolApplication(_ => null, () => throw new InvalidOperationException("HTTP must not start."));
        var output = new StringWriter();

        Assert.Equal(3, await app.RunAsync(["collect", "--enterprise", "example", "--user", "alice", "--output", "-"], output, new StringWriter()));
        Assert.Empty(output.ToString());
    }

    [Fact]
    public async Task UnwritableReportDoesNotReplaceExistingBundle()
    {
        var inputs = await WriteInputs();
        var target = Path.Combine(_directory, "compass.json");
        await File.WriteAllTextAsync(target, "existing");
        var invalidParent = Path.Combine(_directory, "not-a-directory");
        await File.WriteAllTextAsync(invalidParent, "file");

        var result = await new BundleToolApplication().RunAsync(["create", .. inputs, "--output", target, "--report", Path.Combine(invalidParent, "report.json"), "--overwrite"], new StringWriter(), new StringWriter());

        Assert.Equal(5, result);
        Assert.Equal("existing", await File.ReadAllTextAsync(target));
    }

    private async Task<string[]> WriteInputs(ImportOverrides? overrides = null)
    {
        var snapshot = Path.Combine(_directory, "snapshot.json");
        var workload = Path.Combine(_directory, "workload.json");
        var confirmations = Path.Combine(_directory, "overrides.json");
        await File.WriteAllTextAsync(snapshot, ImportJson.Write(ImportFixture.Snapshot()));
        await File.WriteAllTextAsync(workload, ImportJson.Write(ImportFixture.Workload()));
        await File.WriteAllTextAsync(confirmations, ImportJson.Write(overrides ?? ImportFixture.Overrides()));
        return ["--snapshot", snapshot, "--workload", workload, "--overrides", confirmations];
    }

    public void Dispose() => Directory.Delete(_directory, true);
}