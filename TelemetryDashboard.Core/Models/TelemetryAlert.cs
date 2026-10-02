namespace TelemetryDashboard.Core.Models;

public sealed record TelemetryAlert(
    DateTimeOffset Timestamp,
    AlertSeverity Severity,
    string Message
);