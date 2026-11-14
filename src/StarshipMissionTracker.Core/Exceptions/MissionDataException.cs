namespace StarshipMissionTracker.Core.Exceptions;

/// <summary>Invalid or missing mission/trajectory input (file, format, missing T0).</summary>
public sealed class MissionDataException : Exception
{
    public MissionDataException(string message)
        : base(message)
    {
    }

    public MissionDataException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
