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
        Assert.Empty(await persistence.LoadClientProfilesAsync());
    }

    [Fact]
    public async Task ClientProfilesUseASeparateMultiProfileSlot()
    {
        var js = new MemoryBrowserRuntime();
        var persistence = new CompassBrowserPersistence(js);
        const string first = "{\"profile\":\"client-a-old\"}";
        const string newerFirst = "{\"profile\":\"client-a-new\"}";
        const string second = "{\"profile\":\"client-b\"}";
        var firstCapture = DateTimeOffset.Parse("2026-09-14T10:00:00Z");
        var firstIntegrity = new string('a', 64);
        var newerFirstIntegrity = new string('b', 64);
        var secondIntegrity = new string('c', 64);

        await persistence.SaveAsync("scenario save");
        Assert.Equal(ClientProfileSaveStatus.Saved,
            await persistence.SaveClientProfileAsync("a", firstCapture, firstIntegrity, first, [], []));
        Assert.Equal(ClientProfileSaveStatus.Saved,
            await persistence.SaveClientProfileAsync("b", firstCapture, secondIntegrity, second, [], []));
        var firstKeys = ProfileKeys(js, "a");
        Assert.Equal(ClientProfileSaveStatus.Saved,
            await persistence.SaveClientProfileAsync(
                "a", firstCapture.AddHours(2), newerFirstIntegrity, newerFirst, firstKeys, firstKeys));
        Assert.Equal(ClientProfileSaveStatus.Retry,
            await persistence.SaveClientProfileAsync("a", firstCapture.AddHours(1), firstIntegrity, first, [], []));

        Assert.Equal("scenario save", await persistence.LoadAsync());
        Assert.Equal([newerFirst, second], (await persistence.LoadClientProfilesAsync())
            .Select(record => record.Document));
        Assert.Equal(3, js.Storage.Count);

        await persistence.RemoveClientProfileAsync("a");

        Assert.Equal([second], (await persistence.LoadClientProfilesAsync()).Select(record => record.Document));
        Assert.Equal("scenario save", await persistence.LoadAsync());
    }

    [Fact]
    public async Task StorageAndDownloadFailuresAreSurfaced()
    {
        var persistence = new CompassBrowserPersistence(new MemoryBrowserRuntime { Fail = true });

        Assert.Contains("unavailable", (await Assert.ThrowsAsync<BrowserPersistenceException>(
            () => persistence.SaveAsync("bundle"))).Message);
        Assert.Contains("could not be read", (await Assert.ThrowsAsync<BrowserPersistenceException>(
            () => persistence.LoadAsync())).Message);
        Assert.Contains("could not be saved", (await Assert.ThrowsAsync<BrowserPersistenceException>(
            () => persistence.SaveClientProfileAsync(
                "id", DateTimeOffset.UtcNow, new string('a', 64), "profile", [], []))).Message);
        Assert.Contains("could not be read", (await Assert.ThrowsAsync<BrowserPersistenceException>(
            () => persistence.LoadClientProfilesAsync())).Message);
        Assert.Contains("could not be removed", (await Assert.ThrowsAsync<BrowserPersistenceException>(
            () => persistence.RemoveClientProfileAsync("id"))).Message);
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
            case "costCompass.loadClientProfiles":
                var prefix = (string)args![0]!;
                value = runtime.Storage.Where(item => item.Key.StartsWith(prefix, StringComparison.Ordinal))
                    .OrderBy(item => item.Key, StringComparer.Ordinal)
                    .Select(item => new ClientProfileBrowserRecord { Key = item.Key, Document = item.Value }).ToArray();
                break;
            case "costCompass.saveClientProfile":
                var storagePrefix = (string)args![0]!;
                var profileId = (string)args[1]!;
                var capturedAtTicks = (string)args[2]!;
                var profileIntegrity = (string)args[3]!;
                var document = (string)args[4]!;
                var profilePrefix = $"{storagePrefix}{profileId}.";
                var expectedKeys = ((IEnumerable<string>)args[5]!).Order(StringComparer.Ordinal).ToArray();
                var removeKeys = (IEnumerable<string>)args[6]!;
                var currentKeys = runtime.Storage.Keys.Where(key => key.StartsWith(profilePrefix, StringComparison.Ordinal))
                    .Order(StringComparer.Ordinal).ToArray();
                var candidateKey = $"{profilePrefix}{capturedAtTicks}.{profileIntegrity}";
                if (!currentKeys.SequenceEqual(expectedKeys, StringComparer.Ordinal)) value = "retry";
                else
                {
                    runtime.Storage[candidateKey] = document;
                    foreach (var key in removeKeys.Where(key => key != candidateKey)) runtime.Storage.Remove(key);
                    value = "saved";
                }
                break;
            case "costCompass.removeClientProfile":
                var profileStoragePrefix = (string)args![0]!;
                foreach (var key in runtime.Storage.Keys.Where(key =>
                    key.StartsWith(profileStoragePrefix, StringComparison.Ordinal)).ToArray())
                    runtime.Storage.Remove(key);
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

    private static string[] ProfileKeys(MemoryBrowserRuntime runtime, string id) =>
        runtime.Storage.Keys.Where(key => key.StartsWith(
            $"{CompassBrowserPersistence.ClientProfileStoragePrefix}{id}.", StringComparison.Ordinal)).ToArray();

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
