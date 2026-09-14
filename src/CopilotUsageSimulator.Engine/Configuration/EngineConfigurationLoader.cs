using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CopilotUsageSimulator.Engine.Configuration;

public static class EngineConfigurationLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static EngineConfiguration Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    public static EngineConfiguration Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var configuration = JsonSerializer.Deserialize<EngineConfiguration>(stream, SerializerOptions)
            ?? throw new ConfigurationException("The configuration document is empty.");
        EngineConfigurationValidator.Validate(configuration);
        return configuration;
    }

    public static EngineConfiguration LoadDefault()
    {
        var assembly = typeof(EngineConfigurationLoader).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("Configuration.default-catalog.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new ConfigurationException($"Embedded configuration '{resourceName}' was not found.");
        return Load(stream);
    }
}
