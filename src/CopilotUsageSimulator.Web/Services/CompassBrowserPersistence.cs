using Microsoft.JSInterop;

namespace CopilotUsageSimulator.Web.Services;

public sealed class CompassBrowserPersistence(IJSRuntime js)
{
    public const string StorageKey = "github-copilot-cost-compass.bundle.v1";

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
