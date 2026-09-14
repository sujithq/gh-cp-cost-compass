using System.Text.Json;
using System.Text.Json.Serialization;
using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Bundles;

public sealed class ScenarioJson
{
    private readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string Serialize(SimulationScenario scenario) =>
        JsonSerializer.Serialize(scenario, _options);

    public SimulationScenario Deserialize(string json)
    {
        var scenario = JsonSerializer.Deserialize<SimulationScenario>(json, _options)
            ?? throw new JsonException("The scenario document is empty.");
        SimulationScenarioValidator.Validate(scenario);
        return scenario;
    }

    public string SerializeConfiguration(EngineConfiguration configuration) =>
        JsonSerializer.Serialize(configuration, _options);

    public EngineConfiguration DeserializeConfiguration(string json)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        return EngineConfigurationLoader.Load(stream);
    }
}