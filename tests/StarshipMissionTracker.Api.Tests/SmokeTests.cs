using Microsoft.AspNetCore.Mvc.Testing;

namespace StarshipMissionTracker.Api.Tests;

public class SmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SmokeTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Host_Starts_AndRootResponds()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/", TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
    }
}
