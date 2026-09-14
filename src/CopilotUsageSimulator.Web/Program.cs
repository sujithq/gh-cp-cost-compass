using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using CopilotUsageSimulator.Engine;
using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Simulation;
using CopilotUsageSimulator.Web;
using CopilotUsageSimulator.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
var configuration = EngineConfigurationLoader.LoadDefault();
builder.Services.AddSingleton(configuration);
builder.Services.AddSingleton<ICopilotUsageSimulationEngine>(
    new CopilotUsageSimulationEngine(configuration));
builder.Services.AddSingleton<SimulationSessionRunner>();
builder.Services.AddSingleton<ScenarioJson>();
builder.Services.AddSingleton<WorkloadEditorAdapter>();
builder.Services.AddSingleton<AttributionEditorAdapter>();
builder.Services.AddSingleton<EconomicEditorAdapter>();
builder.Services.AddSingleton<RuntimeEditorAdapter>();
builder.Services.AddSingleton<ActionsEditorAdapter>();
builder.Services.AddSingleton<ScenarioEditorAdapter>();
builder.Services.AddScoped<BrowserScenarioPersistence>();
builder.Services.AddScoped<HomePageModel>();
builder.Services.AddSingleton<CompassBundleCodec>();
builder.Services.AddScoped<CompassBrowserPersistence>();
builder.Services.AddScoped<CompassPageModel>();

await builder.Build().RunAsync();
