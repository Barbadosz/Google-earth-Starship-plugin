using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StarshipMissionTracker.Core.Models;

namespace StarshipMissionTracker.Telemetry.Simulation;

/// <summary>
/// Drives <see cref="SimulationController.TickAsync"/> every <see cref="SimulationOptions.TickMilliseconds"/> (TimeProvider
/// based, so tests with a manual clock drive it too). Exceptions are caught and logged; the loop never dies.
/// </summary>
public sealed class SimulationHostedService : BackgroundService
{
    private readonly SimulationController _controller;
    private readonly SimulationOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SimulationHostedService> _logger;

    public SimulationHostedService(
        SimulationController controller,
        SimulationOptions options,
        TimeProvider timeProvider,
        ILogger<SimulationHostedService> logger)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (options.TickMilliseconds < 1)
        {
            throw new ArgumentException($"Simulation:TickMilliseconds must be >= 1 (was {options.TickMilliseconds}).", nameof(options));
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await AutoStartAsync(stoppingToken).ConfigureAwait(false);

            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.TickMilliseconds), _timeProvider);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await _controller.TickAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Simulation loop: unexpected error in tick; continuing");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task AutoStartAsync(CancellationToken cancellationToken)
    {
        if (!_options.AutoStart)
        {
            return;
        }

        try
        {
            var request = new SimulationStartRequest(_options.AutoStartMissionCode, _options.DefaultSpeed, _options.AutoStartCountdownSeconds);
            await _controller.StartAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Simulation auto-start failed for mission '{MissionCode}'", _options.AutoStartMissionCode);
        }
    }
}
