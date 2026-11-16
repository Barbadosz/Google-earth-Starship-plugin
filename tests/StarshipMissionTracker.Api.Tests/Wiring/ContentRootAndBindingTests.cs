using System.Net;
using StarshipMissionTracker.Api.Options;
using StarshipMissionTracker.Api.Startup;

namespace StarshipMissionTracker.Api.Tests.Wiring;

/// <summary>Content root independent of the working directory, /assets traversal and non-loopback overrides (N-30 review round 1).</summary>
public class ContentRootAndBindingTests
{
    /// <summary>The Api project's own bin folder (the test bin folder has no project folder above it).</summary>
    private static string ApiBuildOutput()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StarshipMissionTracker.sln")))
        {
            dir = dir.Parent;
        }

        return Directory.GetDirectories(Path.Combine(dir!.FullName, "src", "StarshipMissionTracker.Api", "bin"), "net*", SearchOption.AllDirectories)[0];
    }

    [Fact]
    public void ContentRootLocator_FindsTheApiProject_FromTheBuildOutput_RegardlessOfCwd()
    {
        var root = ContentRootLocator.Find(ApiBuildOutput());

        Assert.NotNull(root);
        Assert.True(File.Exists(Path.Combine(root, "wwwroot", "control", "index.html")));
        Assert.True(File.Exists(Path.Combine(root, "appsettings.json")));
        // Found from the application folder only: the current directory is not consulted.
        Assert.Equal(root, ContentRootLocator.Find(Path.Combine(root, "wwwroot", "control")));
    }

    [Fact]
    public void ContentRootLocator_NoMatch_ReturnsNull()
    {
        var empty = Path.Combine(Path.GetTempPath(), "starship-noroot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            Assert.Null(ContentRootLocator.Find(empty));
            Assert.NotNull(ContentRootLocator.CheckControlPage(empty));
            Assert.NotNull(ContentRootLocator.CheckControlPage(null));
        }
        finally
        {
            Directory.Delete(empty);
        }
    }

    [Fact]
    public async Task RealHost_ServesControlPage_AndAppliesAllowedHosts()
    {
        await using var factory = new StarshipAppFactory();
        using var client = factory.CreateLocalClient();

        using var page = await client.GetAsync("/control/index.html", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);

        using var foreign = new HttpRequestMessage(HttpMethod.Get, "/control/index.html");
        foreign.Headers.Host = "evil.example";
        using var rejected = await client.SendAsync(foreign, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    [Theory]
    [InlineData("/assets/../appsettings.json")]
    [InlineData("/assets/%2e%2e/appsettings.json")]
    [InlineData("/assets/%2E%2E%2Fappsettings.json")]
    [InlineData("/assets/..%5Cappsettings.json")]
    [InlineData("/assets/")]
    [InlineData("/assets/models/")]
    [InlineData("/assets/no-such-file.xyz")]
    public async Task Assets_TraversalListingAndUnknownFiles_AreNotServed(string path)
    {
        await using var factory = new StarshipAppFactory();
        using var client = factory.CreateLocalClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("AllowedHosts", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Kestrel:Endpoints:Public:Url", "http://0.0.0.0:9090")]
    [InlineData("urls", "http://192.168.1.10:8080")]
    public async Task NonLoopbackOverrides_AreRejectedAtStartup(string key, string value)
    {
        await using var factory = new StarshipAppFactory(new Dictionary<string, string?> { [key] = value });

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("loopback", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnvironmentUrls_NonLoopback_AreRejectedAtStartup()
    {
        // ASPNETCORE_URLS is read into the "urls" setting by the host configuration; set it only for this host build.
        const string variable = "ASPNETCORE_URLS";
        var previous = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, "http://0.0.0.0:8080");
        try
        {
            await using var factory = new StarshipAppFactory();
            var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
            Assert.Contains("loopback", ex.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[abc]")]
    [InlineData("[127.0.0.1]")]
    public void HostFilterOptions_RejectBadBracketedEntries(string entry) =>
        Assert.NotNull(new HostFilterOptions { AllowedHostNames = ["localhost", entry] }.Validate());
}
