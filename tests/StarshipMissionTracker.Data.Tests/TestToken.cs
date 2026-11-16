global using static StarshipMissionTracker.Data.Tests.TestToken;

namespace StarshipMissionTracker.Data.Tests;

/// <summary>The xUnit per-test cancellation token (xUnit1051), short enough to pass at every async call.</summary>
internal static class TestToken
{
    public static CancellationToken Ct => TestContext.Current.CancellationToken;
}
