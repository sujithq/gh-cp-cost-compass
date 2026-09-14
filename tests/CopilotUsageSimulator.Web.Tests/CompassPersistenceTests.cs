using CopilotUsageSimulator.Web.Services;
using Microsoft.JSInterop;

namespace CopilotUsageSimulator.Web.Tests;

public sealed class CompassPersistenceTests
{
    [Fact]
    public async Task CompassSaveAndExportNeverOverwriteTheLegacySlot()
    {
        var js = new MemoryBrowserRuntime();
        js.Storage["copilot-usage-simulator.state.v1"] = "legacy state";
        var persistence = new CompassBrowserPersistence(js);

        await persistence.SaveAsync("portable bundle");
        var loaded = await persistence.LoadAsync();
        await persistence.ExportAsync("portable bundle");

        Assert.Equal("portable bundle", loaded);
        Assert.Equal("legacy state", js.Storage["copilot-usage-simulator.state.v1"]);
        Assert.Equal("github-copilot-cost-compass.json", js.DownloadName);
        Assert.Equal("portable bundle", js.DownloadContent);
    }

    [Fact]
    public async Task MissingSaveIsDistinctFromAnError()
    {
        var persistence = new CompassBrowserPersistence(new MemoryBrowserRuntime());

        Assert.Null(await persistence.LoadAsync());
    }

    [Fact]
    public async Task StorageAndDownloadFailuresAreSurfaced()
    {
        var persistence = new CompassBrowserPersistence(new MemoryBrowserRuntime { Fail = true });

        Assert.Contains("unavailable", (await Assert.ThrowsAsync<BrowserPersistenceException>(
            () => persistence.SaveAsync("bundle"))).Message);
        Assert.Contains("could not be read", (await Assert.ThrowsAsync<BrowserPersistenceException>(
            () => persistence.LoadAsync())).Message);
        Assert.Contains("could not be downloaded", (await Assert.ThrowsAsync<BrowserPersistenceException>(
            () => persistence.ExportAsync("bundle"))).Message);
    }

    private static ValueTask<TValue> Dispatch<TValue>(MemoryBrowserRuntime runtime, string identifier, object?[]? args)
    {
        if (runtime.Fail) throw new JSException("Browser feature unavailable.");
        object? value = null;
        switch (identifier)
        {
            case "localStorage.setItem":
                runtime.Storage[(string)args![0]!] = (string)args[1]!;
                break;
            case "localStorage.getItem":
                value = runtime.Storage.GetValueOrDefault((string)args![0]!);
                break;
            case "simulator.downloadText":
                runtime.DownloadName = (string)args![0]!;
                runtime.DownloadContent = (string)args[1]!;
                break;
            default:
                throw new JSException($"Unexpected browser call: {identifier}");
        }
        return ValueTask.FromResult(value is null ? default! : (TValue)value);
    }

    private sealed class MemoryBrowserRuntime : IJSRuntime
    {
        public Dictionary<string, string> Storage { get; } = [];
        public string? DownloadName { get; set; }
        public string? DownloadContent { get; set; }
        public bool Fail { get; init; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            Dispatch<TValue>(this, identifier, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            Dispatch<TValue>(this, identifier, args);
    }
}
