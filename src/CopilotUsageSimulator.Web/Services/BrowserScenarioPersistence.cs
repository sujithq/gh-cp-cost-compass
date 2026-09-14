using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace CopilotUsageSimulator.Web.Services;

public sealed class BrowserScenarioPersistence(IJSRuntime js)
{
    private const string StateStorageKey = "copilot-usage-simulator.state.v1";
    private const int StateVersion = 1;

    public async Task SaveAsync(
        string scenarioJson,
        string catalogJson,
        DisplayPreferences preferences)
    {
        var stateJson = JsonSerializer.Serialize(
            new BrowserScenarioStateEnvelope(
                StateVersion,
                scenarioJson,
                catalogJson,
                preferences));

        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", StateStorageKey, stateJson);
        }
        catch (JSException exception)
        {
            throw new BrowserPersistenceException("Browser storage is unavailable.", exception);
        }
    }

    public async Task<BrowserScenarioState?> LoadAsync()
    {
        try
        {
            var stateJson = await js.InvokeAsync<string?>("localStorage.getItem", StateStorageKey);
            if (string.IsNullOrWhiteSpace(stateJson))
            {
                return null;
            }

            var state = JsonSerializer.Deserialize<BrowserScenarioStateEnvelope>(stateJson)
                ?? throw new JsonException("The saved browser state is empty.");
            if (state.Version != StateVersion)
            {
                throw new JsonException($"Unsupported browser state version '{state.Version}'.");
            }

            if (string.IsNullOrWhiteSpace(state.ScenarioJson) ||
                string.IsNullOrWhiteSpace(state.CatalogJson) ||
                state.Preferences is null)
            {
                throw new JsonException("The saved browser state is incomplete.");
            }

            return new BrowserScenarioState(
                state.ScenarioJson,
                state.CatalogJson,
                state.Preferences);
        }
        catch (JSException exception)
        {
            throw new BrowserPersistenceException("Browser storage is unavailable.", exception);
        }
    }

    public async ValueTask ExportAsync(string scenarioJson)
    {
        try
        {
            await js.InvokeVoidAsync("simulator.downloadText", "copilot-simulation.json", scenarioJson);
        }
        catch (JSException exception)
        {
            throw new BrowserPersistenceException("The scenario could not be exported.", exception);
        }
    }

    public static async Task<string> ReadImportAsync(IBrowserFile file)
    {
        await using var stream = file.OpenReadStream(CompassBundleCodec.MaximumFileBytes);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private sealed record BrowserScenarioStateEnvelope(
        int Version,
        string ScenarioJson,
        string CatalogJson,
        DisplayPreferences? Preferences);
}

public sealed record BrowserScenarioState(
    string ScenarioJson,
    string? CatalogJson,
    DisplayPreferences? Preferences);

public sealed class BrowserPersistenceException(string message, JSException innerException)
    : Exception(message, innerException);
