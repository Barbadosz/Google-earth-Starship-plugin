namespace StarshipMissionTracker.Api.Services;

/// <summary>
/// Thrown by <see cref="PlannedDocumentCache"/> when the loaded planned mission's T0 differs from the header it was
/// requested for (the T0 changed between the two reads). <see cref="KmlDocumentService"/> retries once with a fresh header.
/// </summary>
public sealed class MissionChangedException : Exception
{
    public MissionChangedException(string message)
        : base(message)
    {
    }

    public MissionChangedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
