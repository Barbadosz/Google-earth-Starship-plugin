namespace StarshipMissionTracker.Core.Enums;

/// <summary>Supported flight events (spec section 3.6). Not every event applies to every mission.</summary>
public enum EventType
{
    Liftoff,
    MaxQ,
    Meco,
    StageSeparation,
    BoosterBoostback,
    BoosterLandingBurn,
    BoosterLanding,
    StarshipEngineCutoff,
    Coast,
    Reentry,
    LandingBurn,
    SplashdownOrLanding
}
