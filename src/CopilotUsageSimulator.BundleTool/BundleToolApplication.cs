using System.CommandLine;
using System.Text.Json;
using CopilotUsageSimulator.Bundles;
using CopilotUsageSimulator.Engine;
using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.BundleTool;

public sealed class BundleToolApplication(
    Func<string, string?>? environment = null,
    Func<HttpClient>? httpFactory = null,
    TimeProvider? clock = null)
{
    private readonly Func<string, string?> _environment = environment ?? Environment.GetEnvironmentVariable;
    private readonly Func<HttpClient> _httpFactory = httpFactory ?? GitHubReadClient.CreateHttpClient;

    public async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken = default)
    {
        var root = new RootCommand("Create portable Cost Compass bundles without modifying GitHub or advancing simulation balances.");
        var collect = new Command("collect", "Collect a read-only GitHub Enterprise Cloud snapshot; incomplete sources remain explicitly marked.");
        collect.Options.Add(new Option<string>("--enterprise") { Required = true, Description = "Enterprise slug on github.com." });
        collect.Options.Add(new Option<string>("--user") { Required = true, Description = "Selected user's GitHub login; shared seats remain included." });
        collect.Options.Add(new Option<string>("--output") { Required = true, Description = "Snapshot file path, or '-' for JSON-only stdout." });
        collect.Options.Add(new Option<string>("--token-env") { Description = "Token environment variable name; otherwise GH_TOKEN, then GITHUB_TOKEN." });
        collect.Options.Add(new Option<string>("--report") { Description = "Coverage report path; defaults to <output>.report.json." });
        collect.Options.Add(new Option<bool>("--all-seat-usage") { Description = "Also collect enterprise aggregate and per-seat monthly AI-credit usage reports." });
        collect.Options.Add(new Option<bool>("--overwrite"));
        collect.SetAction((parse, token) => CollectAsync(parse, output, error, token));
        root.Subcommands.Add(collect);

        var create = new Command("create", "Create a Compass v1 bundle offline from a collected snapshot, workload and confirmations.");
        create.Options.Add(new Option<string>("--snapshot") { Required = true, Description = "Versioned enterprise snapshot JSON." });
        create.Options.Add(new Option<string>("--workload") { Required = true, Description = "Explicit model calls and optional Actions account assumptions." });
        create.Options.Add(new Option<string>("--overrides") { Description = "Typed confirmations for data the APIs cannot establish." });
        create.Options.Add(new Option<string>("--catalog") { Description = "Matching Engine reference catalog; defaults to the embedded catalog." });
        create.Options.Add(new Option<string>("--output") { Required = true, Description = "Bundle file path, or '-' for JSON-only stdout." });
        create.Options.Add(new Option<string>("--report") { Description = "Import report path; defaults to <output>.report.json." });
        create.Options.Add(new Option<bool>("--overwrite") { Description = "Replace existing output/report files, never inputs." });
        create.SetAction((parse, token) => CreateAsync(parse, output, error, token));
        root.Subcommands.Add(create);

        var validate = new Command("validate", "Validate a Compass bundle and report its immutable preview verdict.");
        var bundlePath = new Argument<string>("bundle");
        validate.Arguments.Add(bundlePath);
        validate.Options.Add(new Option<string>("--report") { Description = "Write the validation report to a file instead of stdout." });
        validate.Options.Add(new Option<bool>("--overwrite"));
        validate.SetAction((parse, token) => ExecuteAsync("validate", async () =>
        {
            var json = await ImportFiles.ReadAsync(parse.GetValue(bundlePath)!, CompassBundleCodec.MaximumFileBytes, token);
            var report = BundleInspector.Validate(json);
            return new CommandOutput(null, report, 0);
        }, null, parse.GetValue<string>("--report"), [parse.GetValue(bundlePath)], parse.GetValue<bool>("--overwrite"), output, error, token));
        root.Subcommands.Add(validate);

        var result = root.Parse(args.Length == 0 ? ["--help"] : args);
        if (result.Errors.Count > 0)
        {
            await error.WriteLineAsync("invalid-command: Invalid or missing options. Run 'compass-bundle <command> --help'.");
            return 2;
        }
        return await result.InvokeAsync(new InvocationConfiguration
        {
            Output = output, Error = error, EnableDefaultExceptionHandler = false
        }, cancellationToken);
    }

    private Task<int> CollectAsync(ParseResult parse, TextWriter output, TextWriter error, CancellationToken token)
    {
        var target = parse.GetValue<string>("--output")!;
        var reportPath = parse.GetValue<string>("--report") ?? (target == "-" ? null : target + ".report.json");
        return ExecuteAsync("collect", async () =>
        {
            var variable = parse.GetValue<string>("--token-env");
            if (variable is not null && !System.Text.RegularExpressions.Regex.IsMatch(variable, "^[A-Za-z_][A-Za-z0-9_]*$"))
                throw new ImportException("invalid-token-variable", "--token-env accepts an environment variable name, never an inline token.", 2);
            var credential = variable is not null ? _environment(variable) : _environment("GH_TOKEN");
            if (variable is null && string.IsNullOrWhiteSpace(credential)) credential = _environment("GITHUB_TOKEN");
            if (string.IsNullOrWhiteSpace(credential))
                throw new ImportException("token-missing", "Set GH_TOKEN, GITHUB_TOKEN or the named --token-env in your shell. Do not put credentials in command arguments or JSON.", 3);
            using var http = _httpFactory();
            var collected = await new GitHubSnapshotCollector(new GitHubReadClient(http, credential), clock)
                .CollectAsync(parse.GetValue<string>("--enterprise")!, parse.GetValue<string>("--user")!,
                    parse.GetValue<bool>("--all-seat-usage"), token);
            return new CommandOutput(ImportJson.Write(collected.Snapshot), collected.Report,
                collected.Snapshot.Sources.All(source => source.Complete) ? 0 : 3);
        }, target, reportPath, [], parse.GetValue<bool>("--overwrite"), output, error, token);
    }

    private static Task<int> CreateAsync(ParseResult parse, TextWriter output, TextWriter error, CancellationToken token)
    {
        var snapshotPath = parse.GetValue<string>("--snapshot")!;
        var workloadPath = parse.GetValue<string>("--workload")!;
        var overridePath = parse.GetValue<string>("--overrides");
        var catalogPath = parse.GetValue<string>("--catalog");
        var target = parse.GetValue<string>("--output")!;
        var reportPath = parse.GetValue<string>("--report") ?? (target == "-" ? null : target + ".report.json");
        return ExecuteAsync("create", async () =>
        {
            var snapshot = ImportJson.Read<EnterpriseSnapshot>(await ImportFiles.ReadAsync(snapshotPath, ImportFiles.MaximumSnapshotBytes, token));
            var workload = ImportJson.Read<WorkloadInput>(await ImportFiles.ReadAsync(workloadPath, CompassBundleCodec.MaximumFileBytes, token));
            var overrides = overridePath is null ? new ImportOverrides { SchemaVersion = 1 } :
                ImportJson.Read<ImportOverrides>(await ImportFiles.ReadAsync(overridePath, ImportFiles.MaximumSnapshotBytes, token));
            var catalog = catalogPath is null ? EngineConfigurationLoader.LoadDefault() :
                new ScenarioJson().DeserializeConfiguration(await ImportFiles.ReadAsync(catalogPath, CompassBundleCodec.MaximumFileBytes, token));
            var created = new SnapshotAssembler().Create(snapshot, workload, overrides, catalog);
            return new CommandOutput(created.Json, created.Report, 0);
        }, target, reportPath, [snapshotPath, workloadPath, overridePath, catalogPath], parse.GetValue<bool>("--overwrite"), output, error, token);
    }

    private static async Task<int> ExecuteAsync(
        string command, Func<Task<CommandOutput>> action, string? target, string? reportPath,
        IReadOnlyList<string?> inputs, bool overwrite, TextWriter output, TextWriter error, CancellationToken token)
    {
        var targetsChecked = false;
        var reportWritten = false;
        try
        {
            if (reportPath == "-") throw new ImportException("invalid-report-path", "Omit --report for console reports; --report requires a file path.", 2);
            ImportFiles.CheckTargets(inputs, [target, reportPath], overwrite);
            targetsChecked = true;
            var result = await action();
            if (reportPath is not null)
            {
                await ImportFiles.WriteAtomicAsync(reportPath, ImportJson.Write(result.Report), overwrite, token);
                reportWritten = true;
            }
            if (result.Json is not null)
            {
                if (target == "-") await output.WriteLineAsync(result.Json.AsMemory(), token);
                else await ImportFiles.WriteAtomicAsync(target!, result.Json, overwrite, token);
                foreach (var diagnostic in result.Report.Diagnostics.Where(issue => issue.Severity != "info"))
                    await error.WriteLineAsync($"{diagnostic.Code}: {diagnostic.Message}");
                await error.WriteLineAsync(command == "collect"
                    ? $"collect: snapshot saved{(result.ExitCode == 0 ? "" : " with incomplete sources")}; no Compass bundle was created."
                    : $"{command}: output validated; preview = {result.Report.PreviewDecision}.");
            }
            else if (reportPath is null)
            {
                await output.WriteLineAsync(ImportJson.Write(result.Report).AsMemory(), token);
            }
            return result.ExitCode;
        }
        catch (OperationCanceledException)
        {
            await error.WriteLineAsync("cancelled: File outputs were not replaced with partial JSON.");
            if (reportWritten && reportPath is not null)
                await WriteFailureReportAsync(command, "cancelled", "Cancelled before command completion.", reportPath, true, error);
            return 130;
        }
        catch (Exception exception) when (exception is ImportException or JsonException or ConfigurationException or SimulationException or OverflowException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            var (code, exitCode) = exception switch
            {
                ImportException import => (import.Code, import.ExitCode),
                JsonException => ("invalid-json", 2),
                ConfigurationException => ("invalid-catalog", 4),
                SimulationException simulation => (simulation.Code, 4),
                OverflowException => ("numeric-overflow", 4),
                IOException or UnauthorizedAccessException => ("output-failed", 5),
                _ => ("invalid-input", 2)
            };
            var message = exception is OverflowException ? "Numeric input exceeded the supported range." : exception.Message;
            await error.WriteLineAsync($"{code}: {message}");
            if (targetsChecked && reportPath is not null)
            {
                if (!await WriteFailureReportAsync(command, code, message, reportPath, overwrite || reportWritten, error)) return 5;
            }
            return exitCode;
        }
    }

    private static async Task<bool> WriteFailureReportAsync(string command, string code, string message, string reportPath, bool overwrite, TextWriter error)
    {
        try
        {
            var report = new ImportReport { Command = command, Diagnostics = [new("error", code, "input", message)] };
            await ImportFiles.WriteAtomicAsync(reportPath, ImportJson.Write(report), overwrite, CancellationToken.None);
            return true;
        }
        catch (Exception exception) when (exception is ImportException or IOException or UnauthorizedAccessException)
        {
            await error.WriteLineAsync("report-output-failed: The diagnostic report could not be written; the command did not complete.");
            return false;
        }
    }

    private sealed record CommandOutput(string? Json, ImportReport Report, int ExitCode);
}