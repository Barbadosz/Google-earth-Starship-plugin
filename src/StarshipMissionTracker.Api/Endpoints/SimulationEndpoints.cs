using Microsoft.Extensions.Options;
using StarshipMissionTracker.Api.Dtos;
using StarshipMissionTracker.Api.Http;
using StarshipMissionTracker.Core.Exceptions;
using StarshipMissionTracker.Core.Interfaces;
using StarshipMissionTracker.Core.Models;
using StarshipMissionTracker.Telemetry.Simulation;

namespace StarshipMissionTracker.Api.Endpoints;

/// <summary>
/// Simulation status and control (plan section 4, N-34). GET <c>/api/simulation</c> is open; start, stop, PUT speed,
/// pause and resume are loopback only and need <c>Content-Type: application/json</c> (403 / 415). Each returns the
/// status JSON (<see cref="SimulationStatusDto"/>). Controller errors map to 400 (argument), 404 (unknown mission) and
/// 409 (<see cref="SimulationStateException"/>, wrong state); any other exception is an internal error (500).
/// </summary>
public static class SimulationEndpoints
{
    public const string BasePath = "/api/simulation";

    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapGet(BasePath, (ISimulationControl control) => ApiErrors.Ok(SimulationStatusDto.From(control.GetStatus())));

        Mutating(app.MapPost(BasePath + "/start", StartAsync));
        Mutating(app.MapPost(BasePath + "/stop", (HttpContext context, ISimulationControl control) =>
            RunAsync(context, "stop", () => control.StopAsync(context.RequestAborted))));
        Mutating(app.MapPut(BasePath + "/speed", SetSpeedAsync));
        Mutating(app.MapPost(BasePath + "/pause", (HttpContext context, ISimulationControl control) =>
            RunAsync(context, "pause", () => control.PauseAsync(context.RequestAborted))));
        Mutating(app.MapPost(BasePath + "/resume", (HttpContext context, ISimulationControl control) =>
            RunAsync(context, "resume", () => control.ResumeAsync(context.RequestAborted))));
        return app;
    }

    /// <summary>Loopback check first (403), then the JSON content type (415).</summary>
    internal static RouteHandlerBuilder Mutating(RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<LoopbackOnlyFilter>().AddEndpointFilter<JsonContentTypeFilter>();

    /// <summary>Body <c>{"mission":"demo-ift","speed":10,"countdownSeconds":10}</c>; speed and countdown are optional.</summary>
    private static async Task<IResult> StartAsync(HttpContext context, ISimulationControl control, IOptions<SimulationOptions> options)
    {
        var (body, error) = await JsonBody.ReadObjectAsync(context.Request, context.RequestAborted).ConfigureAwait(false);
        if (error is not null)
        {
            return Rejected(context, "start", error, "invalid body");
        }

        var (mission, missionError) = JsonBody.TryGetString(body, "mission", required: true);
        var (speed, speedError) = JsonBody.TryGetNumber(body, "speed", required: false);
        var (countdown, countdownError) = JsonBody.TryGetNumber(body, "countdownSeconds", required: false);
        var reason = missionError ?? speedError ?? countdownError;
        if (reason is null && !InputRules.IsValidMissionCode(mission))
        {
            reason = InputRules.MissionCodeRule;
        }

        var speedValue = speed ?? options.Value.DefaultSpeed;
        if (reason is null && !SimulationSpeedLimits.IsValid(speedValue))
        {
            reason = SpeedRule(speedValue);
        }

        var countdownValue = countdown ?? InputRules.DefaultCountdownSeconds;
        if (reason is null && countdownValue is < 0 or > InputRules.MaxCountdownSeconds)
        {
            reason = $"'countdownSeconds' must be within [0, {InputRules.MaxCountdownSeconds}], got {countdownValue}.";
        }

        if (reason is not null)
        {
            return Rejected(context, "start", ApiErrors.BadRequest(reason), reason);
        }

        var request = new SimulationStartRequest(mission!, speedValue, countdownValue);
        return await RunAsync(context, "start", () => control.StartAsync(request, context.RequestAborted)).ConfigureAwait(false);
    }

    /// <summary>Body <c>{"speed":2.5}</c>: a finite JSON number in [Min, Max]; no clamping.</summary>
    private static async Task<IResult> SetSpeedAsync(HttpContext context, ISimulationControl control)
    {
        var (body, error) = await JsonBody.ReadObjectAsync(context.Request, context.RequestAborted).ConfigureAwait(false);
        if (error is not null)
        {
            return Rejected(context, "speed", error, "invalid body");
        }

        var (speed, reason) = JsonBody.TryGetNumber(body, "speed", required: true);
        if (reason is null && !SimulationSpeedLimits.IsValid(speed!.Value))
        {
            reason = SpeedRule(speed.Value);
        }

        if (reason is not null)
        {
            return Rejected(context, "speed", ApiErrors.BadRequest(reason), reason);
        }

        return await RunAsync(context, "speed", () => control.SetSpeedAsync(speed!.Value, context.RequestAborted)).ConfigureAwait(false);
    }

    /// <summary>Calls the controller and maps its exceptions (see the class remarks).</summary>
    private static async Task<IResult> RunAsync(HttpContext context, string action, Func<Task<SimulationStatus>> call)
    {
        try
        {
            var status = await call().ConfigureAwait(false);
            Logger(context).LogInformation(
                "Simulation {Action} accepted: state {State}, mission {MissionCode}, speed {Speed}x, paused {Paused}",
                action, status.State, status.MissionCode, status.Speed, status.IsPaused);
            return ApiErrors.Ok(SimulationStatusDto.From(status));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Rejected(context, action, ApiErrors.BadRequest(Message(ex)), Message(ex));
        }
        catch (KeyNotFoundException ex)
        {
            return Rejected(context, action, ApiErrors.NotFound(ex.Message), ex.Message);
        }
        catch (SimulationStateException ex)
        {
            return Rejected(context, action, ApiErrors.Conflict(ex.Message), ex.Message);
        }
    }

    private static IResult Rejected(HttpContext context, string action, IResult result, string reason)
    {
        Logger(context).LogWarning("Simulation {Action} rejected: {Reason}", action, reason);
        return result;
    }

    private static string SpeedRule(double speed) =>
        FormattableString.Invariant($"'speed' must be within [{SimulationSpeedLimits.Min}, {SimulationSpeedLimits.Max}], got {speed}.");

    /// <summary>The exception message without the "(Parameter ...)" / "Actual value ..." suffixes.</summary>
    private static string Message(ArgumentException ex)
    {
        var text = ex.Message;
        var cut = text.IndexOf(" (Parameter", StringComparison.Ordinal);
        return cut > 0 ? text[..cut] : text;
    }

    private static ILogger Logger(HttpContext context) =>
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SimulationEndpoints));
}
