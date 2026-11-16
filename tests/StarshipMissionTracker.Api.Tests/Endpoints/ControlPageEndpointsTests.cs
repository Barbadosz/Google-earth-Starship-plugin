using System.Net;
using StarshipMissionTracker.Api.Tests.Support;

namespace StarshipMissionTracker.Api.Tests.Endpoints;

public class ControlPageEndpointsTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/control")]
    [InlineData("/control/")]
    public async Task Get_RedirectsToControlPage(string path)
    {
        await using var app = await TestApp.StartAsync();

        using var response = await app.SendAsync(HttpMethod.Get, path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/control/index.html", response.Headers.Location!.OriginalString);
    }
}
