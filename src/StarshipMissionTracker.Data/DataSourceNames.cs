namespace StarshipMissionTracker.Data;

/// <summary>Names of the data sources created by the importer (unique in the DataSources table).</summary>
public static class DataSourceNames
{
    public const string PlannedTrajectoryFile = "Planned trajectory file";
    public const string InternalSimulator = Core.Models.SimulatorSource.Name;
}
