using StarshipMissionTracker.Api.Tests.Wiring;

namespace StarshipMissionTracker.Api.Tests;

public class SmokeTests
{
    [Fact]
    public async Task Host_Starts_AndRootResponds()
    {
        // Temp database and log folder: the smoke test never touches data/starship.db or logs/ of the repository.
        await using var factory = new StarshipAppFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/", TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
    }
}
