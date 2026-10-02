using TelemetryDashboard.Core.Events;

namespace TelemetryDashboard.Core.Interfaces;

public interface ITelemetrySource
{
    event EventHandler<TelemetryReceivedEventArgs>? TelemetryReceived;

    bool IsRunning { get; }
    Task StartAsync();
    Task StopAsync();
}