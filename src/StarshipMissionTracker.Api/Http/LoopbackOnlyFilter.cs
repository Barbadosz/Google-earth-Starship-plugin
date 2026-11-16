using System.Net;
using Microsoft.Extensions.Options;
using StarshipMissionTracker.Api.Options;

namespace StarshipMissionTracker.Api.Http;

/// <summary>
/// Mutating endpoints only: rejects with 403 a caller that is not on this computer, and a request whose <c>Host</c>
/// header is not a local name (<see cref="LocalHostRule"/>, DNS rebinding). Fails closed: an unknown remote address
/// (null) or a missing Host is rejected too.
/// </summary>
public sealed class LoopbackOnlyFilter(ILogger<LoopbackOnlyFilter> logger, IOptions<HostFilterOptions> hostOptions) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var remote = http.Connection.RemoteIpAddress;
        if (!IsLoopback(remote))
        {
            logger.LogWarning(
                "Rejected {Method} {Path}: caller {RemoteIp} is not loopback", http.Request.Method, http.Request.Path, remote?.ToString() ?? "(unknown)");
            return ApiErrors.Result(StatusCodes.Status403Forbidden, "forbidden", "Only allowed from this computer (loopback).");
        }

        if (!LocalHostRule.IsAllowed(http.Request.Host, hostOptions.Value.AllowedHostNames))
        {
            logger.LogWarning(
                "Rejected {Method} {Path}: Host {Host} is not a local host name (possible DNS rebinding)",
                http.Request.Method, http.Request.Path, LocalHostRule.ForLog(http.Request.Host));
            return ApiErrors.Result(
                StatusCodes.Status403Forbidden, "forbidden", "Only allowed through a local address (localhost, 127.0.0.1 or [::1]).");
        }

        return await next(context).ConfigureAwait(false);
    }

    public static bool IsLoopback(IPAddress? address)
    {
        if (address is null)
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return IPAddress.IsLoopback(address);
    }
}
