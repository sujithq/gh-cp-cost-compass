using CopilotUsageSimulator.Engine.Configuration;
using CopilotUsageSimulator.Engine.Simulation;

namespace CopilotUsageSimulator.Engine;

public interface ICopilotUsageSimulationEngine
{
    EngineConfiguration Configuration { get; }

    SimulationResult Simulate(SimulationScenario scenario);
}
