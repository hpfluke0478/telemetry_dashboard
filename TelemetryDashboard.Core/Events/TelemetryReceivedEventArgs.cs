using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Core.Events;

public sealed class TelemetryReceivedEventArgs : EventArgs
{
    public FlightTelemetry Telemetry { get; }

    public TelemetryReceivedEventArgs(FlightTelemetry telemetry)
    {
        Telemetry = telemetry;
    }
}