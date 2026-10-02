using TelemetryDashboard.Core.Events;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Infrastructure.Telemetry;

public sealed class SimulatedTelemetrySource :
    ITelemetrySource,
    ISimulationControl
{
    private readonly Random _random = new();

    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _simulationTask;

    private double _simulationSeconds;
    private FlightMode _flightMode = FlightMode.Takeoff;

    private DateTimeOffset? _rxLossUntil;
    private DateTimeOffset? _linkDegradedUntil;
    private DateTimeOffset? _gpsLossUntil;
    private DateTimeOffset? _highDescentUntil;
    private DateTimeOffset? _overspeedUntil;

    private double? _batteryOverride;

    private double _reportedLatitude;
    private double _reportedLongitude;
    private bool _reportedPositionInitialized;

    private readonly double _homeLatitude = 45.5152;
    private readonly double _homeLongitude = -122.6784;

    private double _latitude = 45.5152;
    private double _longitude = -122.6784;
    private double _altitude = 4850;
    private double _airspeed = 72;
    private double _groundSpeed = 69;
    private double _heading = 270;
    private double _verticalSpeed = 120;
    private double _battery = 92;
    private double _linkQuality = 98;
    private int _gpsSatellites = 14;

    public event EventHandler<TelemetryReceivedEventArgs>? TelemetryReceived;

    public bool IsRunning { get; private set; }

    public Task StartAsync()
    {
        if (IsRunning)
            return Task.CompletedTask;

        _cancellationTokenSource = new CancellationTokenSource();

        IsRunning = true;

        _simulationTask = Task.Run(
            () => RunSimulationAsync(_cancellationTokenSource.Token));

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!IsRunning)
            return;

        _cancellationTokenSource?.Cancel();

        if (_simulationTask is not null)
        {
            try
            {
                await _simulationTask;
            }
            catch (OperationCanceledException)
            {
                // Expected during normal shutdown.
            }
        }

        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
        _simulationTask = null;

        IsRunning = false;
    }

    private async Task RunSimulationAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                UpdateSimulation();

                if (!_reportedPositionInitialized)
                {
                    _reportedLatitude = _latitude;
                    _reportedLongitude = _longitude;
                    _reportedPositionInitialized = true;
                }

                // GPS position only updates while GPS is available.
                if (!IsActive(_gpsLossUntil))
                {
                    _reportedLatitude = _latitude;
                    _reportedLongitude = _longitude;
                }

                var telemetry = new FlightTelemetry(
                    Timestamp: DateTimeOffset.UtcNow,
                    Latitude: _reportedLatitude,
                    Longitude: _reportedLongitude,
                    Altitude: _altitude,
                    AirSpeed: _airspeed,
                    GroundSpeed: _groundSpeed,
                    HeadingDegrees: _heading,
                    VerticalSpeed: _verticalSpeed,
                    BatteryCharge: _battery,
                    LinkQuality: _linkQuality,
                    GpsSatellites: IsActive(_gpsLossUntil)
                        ? 0
                        : _gpsSatellites,
                    FlightMode: _flightMode,
                    IsArmed: true
                );

                // Aircraft simulation keeps running, but packets stop
                // reaching the ViewModel during RX loss.
                if (!IsActive(_rxLossUntil))
                {
                    TelemetryReceived?.Invoke(
                        this,
                        new TelemetryReceivedEventArgs(telemetry));
                }

                await Task.Delay(100, cancellationToken);
            }
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void UpdateSimulation()
    {
        const double deltaTime = 0.1;

        _simulationSeconds += deltaTime;

        double phase = _simulationSeconds % 120;

        double targetAltitude;
        double targetAirspeed;
        double targetHeading;

        if (phase < 15)
        {
            _flightMode = FlightMode.Takeoff;

            targetAltitude = 4800;
            targetAirspeed = 55;
            targetHeading = 270;
        }
        else if (phase < 35)
        {
            _flightMode = FlightMode.Climb;

            targetAltitude = 5500;
            targetAirspeed = 70;
            targetHeading = 255;
        }
        else if (phase < 60)
        {
            _flightMode = FlightMode.Cruise;

            targetAltitude = 5500;
            targetAirspeed = 78;
            targetHeading = 225;
        }
        else if (phase < 90)
        {
            _flightMode = FlightMode.Loiter;

            targetAltitude = 5400;
            targetAirspeed = 65;

            // Continuously turn during loiter.
            targetHeading = NormalizeHeading(
                _heading + 2.0);
        }
        else if (phase < 110)
        {
            _flightMode = FlightMode.ReturnToHome;

            targetAltitude = 5000;
            targetAirspeed = 72;

            targetHeading = CalculateBearingToHome();
        }
        else
        {
            _flightMode = FlightMode.Landing;

            targetAltitude = 4700;
            targetAirspeed = 45;

            targetHeading = CalculateBearingToHome();
        }

        double previousAltitude = _altitude;

        _altitude = MoveTowards(
            _altitude,
            targetAltitude,
            4);

        _airspeed = MoveTowards(
            _airspeed,
            targetAirspeed,
            0.5);

        _groundSpeed = MoveTowards(
            _groundSpeed,
            _airspeed - 3,
            0.5);

        _heading = MoveHeadingTowards(
            _heading,
            targetHeading,
            1.2);

        _verticalSpeed =
            (_altitude - previousAltitude)
            / deltaTime
            * 60;

        _battery = Math.Max(
            0,
            _battery - 0.002);

        _linkQuality = Math.Clamp(
            _linkQuality + RandomDelta(0.35),
            85,
            100);

        _gpsSatellites = _random.Next(11, 16);

        UpdatePosition(deltaTime);
        ApplyInjectedConditions();
    }

    private void ApplyInjectedConditions()
    {
        // Link degradation
        if (IsActive(_linkDegradedUntil))
        {
            _linkQuality = MoveTowards(
                _linkQuality,
                45.0,
                2.0);
        }
        else
        {
            _linkQuality = MoveTowards(
                _linkQuality,
                96.0,
                0.5);
        }

        // Battery override
        if (_batteryOverride.HasValue)
        {
            _battery = MoveTowards(
                _battery,
                _batteryOverride.Value,
                2.0);
        }

        // High descent
        if (IsActive(_highDescentUntil))
        {
            _verticalSpeed = -2200;

            // 0.1-second simulation step:
            // ft/min -> feet per 0.1 sec
            _altitude +=
                (_verticalSpeed / 60.0) * 0.1;
        }

        // Overspeed
        if (IsActive(_overspeedUntil))
        {
            _airspeed = MoveTowards(
                _airspeed,
                110.0,
                2.0);

            _groundSpeed = MoveTowards(
                _groundSpeed,
                106.0,
                2.0);
        }
    }

    private static double MoveTowards(
        double current,
        double target,
        double maximumDelta)
    {
        if (Math.Abs(target - current) <= maximumDelta)
            return target;

        return current +
            Math.Sign(target - current) * maximumDelta;
    }

    private static double MoveHeadingTowards(
        double current,
        double target,
        double maximumDelta)
    {
        double difference =
            (target - current + 540) % 360 - 180;

        if (Math.Abs(difference) <= maximumDelta)
            return NormalizeHeading(target);

        return NormalizeHeading(
            current +
            Math.Sign(difference) * maximumDelta);
    }

    private void UpdatePosition(double deltaTime)
    {
        double metersPerSecond =
            _groundSpeed * 0.514444;

        double distance =
            metersPerSecond * deltaTime;

        double headingRadians =
            _heading * Math.PI / 180.0;

        double north =
            Math.Cos(headingRadians) * distance;

        double east =
            Math.Sin(headingRadians) * distance;

        _latitude += north / 110540.0;

        double metersPerDegreeLongitude =
            111320.0 *
            Math.Cos(_latitude * Math.PI / 180.0);

        _longitude +=
            east / metersPerDegreeLongitude;
    }

    private double CalculateBearingToHome()
    {
        double lat1 =
            _latitude * Math.PI / 180.0;

        double lat2 =
            _homeLatitude * Math.PI / 180.0;

        double deltaLongitude =
            (_homeLongitude - _longitude)
            * Math.PI / 180.0;

        double y =
            Math.Sin(deltaLongitude) *
            Math.Cos(lat2);

        double x =
            Math.Cos(lat1) * Math.Sin(lat2) -
            Math.Sin(lat1) *
            Math.Cos(lat2) *
            Math.Cos(deltaLongitude);

        double bearing =
            Math.Atan2(y, x) *
            180.0 / Math.PI;

        return NormalizeHeading(bearing);
    }

    private double RandomDelta(double magnitude)
    {
        return (_random.NextDouble() * 2 - 1) * magnitude;
    }

    private static double NormalizeHeading(double heading)
    {
        heading %= 360;

        if (heading < 0)
            heading += 360;

        return heading;
    }

    private static bool IsActive(DateTimeOffset? until)
    {
        return until.HasValue &&
            DateTimeOffset.UtcNow < until.Value;
    }

    public void InjectRxLoss(TimeSpan duration)
    {
        _rxLossUntil = DateTimeOffset.UtcNow + duration;
    }

    public void InjectLinkDegradation(TimeSpan duration)
    {
        _linkDegradedUntil = DateTimeOffset.UtcNow + duration;
    }

    public void SetLowBattery()
    {
        _batteryOverride = 25.0;
    }

    public void SetCriticalBattery()
    {
        _batteryOverride = 10.0;
    }

    public void InjectGpsLoss(TimeSpan duration)
    {
        _gpsLossUntil = DateTimeOffset.UtcNow + duration;
    }

    public void InjectHighDescent(TimeSpan duration)
    {
        _highDescentUntil = DateTimeOffset.UtcNow + duration;
    }

    public void InjectOverspeed(TimeSpan duration)
    {
        _overspeedUntil = DateTimeOffset.UtcNow + duration;
    }

    public void RestoreNominal()
    {
        _rxLossUntil = null;
        _linkDegradedUntil = null;
        _gpsLossUntil = null;
        _highDescentUntil = null;
        _overspeedUntil = null;
        _batteryOverride = null;
    }
}