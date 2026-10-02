namespace TelemetryDashboard.Core.Models
{
    public sealed record FlightTelemetry
    (
        DateTimeOffset Timestamp,
        double Latitude,
        double Longitude,
        double Altitude,
        double AirSpeed,
        double GroundSpeed,
        double HeadingDegrees,
        double VerticalSpeed,
        double BatteryCharge,
        double LinkQuality,
        int GpsSatellites,
        FlightMode FlightMode,
        bool IsArmed
    );
}