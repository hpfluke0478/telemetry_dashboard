namespace TelemetryDashboard.Core.Models;

public sealed record FlightPathPoint
(
    DateTimeOffset Timestamp,
    double Latitude,
    double Longitude,
    double Altitude
);