using System.Globalization;
using Microsoft.JSInterop;

namespace CopilotUsageSimulator.Web.Services;

public sealed class CompassBrowserPersistence(IJSRuntime js)
{
    public const string StorageKey = "github-copilot-cost-compass.bundle.v1";
    public const string ClientProfileStoragePrefix = "github-copilot-cost-compass.client-profile.v1.";

    public async Task SaveAsync(string bundle)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", StorageKey, bundle);
        }
        catch (JSException exception)
        {
            throw new BrowserPersistenceException("Browser storage is unavailable. Export a bundle to keep this scenario.", exception);
        }
    }

    public async Task<string?> LoadAsync()
    {
        try
        {
            return await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        }
        catch (JSException exception)
        {
            throw new BrowserPersistenceException("The saved Compass scenario could not be read from browser storage.", exception);
        }
    }

    public async Task<ClientProfileSaveStatus> SaveClientProfileAsync(
        string id,
        DateTimeOffset capturedAt,
        string integritySha256,
        string profile,
        IReadOnlyList<string> expectedKeys,
        IReadOnlyList<string> removeKeys)
    {
        try
        {
            var status = await js.InvokeAsync<string>(
                "costCompass.saveClientProfile",
                ClientProfileStoragePrefix,
                id,
                capturedAt.UtcTicks.ToString("D19", CultureInfo.InvariantCulture),
                integritySha256,
                profile,
                expectedKeys,
                removeKeys);
            return status switch
            {
                "saved" => ClientProfileSaveStatus.Saved,
                "retry" => ClientProfileSaveStatus.Retry,
                _ => throw new JSException("Browser storage returned an unsupported client profile result.")
            };
        }
        catch (JSException exception)
        {
            throw new BrowserPersistenceException("Client profiles could not be saved in browser storage.", exception);
        }
    }

    public async Task<IReadOnlyList<ClientProfileBrowserRecord>> LoadClientProfilesAsync()
    {
        try
        {
            return await js.InvokeAsync<ClientProfileBrowserRecord[]>(
                "costCompass.loadClientProfiles", ClientProfileStoragePrefix) ?? [];
        }
        catch (JSException exception)
        {
            throw new BrowserPersistenceException("Client profiles could not be read from browser storage.", exception);
        }
    }

    public async Task RemoveClientProfileAsync(string id)
    {
        try
        {
            await js.InvokeVoidAsync(
                "costCompass.removeClientProfile", $"{ClientProfileStoragePrefix}{id}.", id);
        }
        catch (JSException exception)
        {
            throw new BrowserPersistenceException("The client profile could not be removed from browser storage.", exception);
        }
    }

    public static bool IsForProfile(ClientProfileBrowserRecord record, string id) =>
        record.Key.StartsWith(ProfilePrefix(id), StringComparison.Ordinal);

    public static bool Matches(ClientProfileBrowserRecord record, CompassClientProfile profile) =>
        string.Equals(record.Key, RecordKey(profile), StringComparison.Ordinal);

    public static string RecordKey(CompassClientProfile profile) =>
        $"{ProfilePrefix(profile.Id)}{profile.CapturedAt.UtcTicks.ToString("D19", CultureInfo.InvariantCulture)}.{profile.IntegritySha256}";

    private static string ProfilePrefix(string id) => $"{ClientProfileStoragePrefix}{id}.";

    public async Task ExportAsync(string bundle)
    {
        try
        {
            await js.InvokeVoidAsync("simulator.downloadText", "github-copilot-cost-compass.json", bundle);
        }
        catch (JSException exception)
        {
            throw new BrowserPersistenceException("The Compass bundle could not be downloaded.", exception);
        }
    }
}

public sealed record ClientProfileBrowserRecord
{
    public required string Key { get; init; }
    public required string Document { get; init; }
}

public enum ClientProfileSaveStatus
{
    Saved,
    Retry
}
