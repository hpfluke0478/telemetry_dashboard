using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TelemetryDashboard.Core.Events;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const int MaxFlightPathPoints = 300;
    private const int FlightPathSampleInterval = 5;

    private readonly ITelemetrySource _telemetrySource;
    private readonly ISimulationControl _simulationControl;
    private readonly DispatcherTimer _rxAgeTimer;

    private int _telemetrySampleCount;
    private bool _homeLocationSet;

    private HealthLevel? _previousBatteryHealth;
    private HealthLevel? _previousLinkHealth;
    private HealthLevel? _previousGpsHealth;

    private bool _isRxLossActive;
    private bool _isLinkInterferenceActive;
    private bool _isBatteryInterferenceActive;
    private bool _isGpsInterferenceActive;
    private bool _isHighDescentActive;
    private bool _isOverspeedActive;

    public ObservableCollection<TelemetryAlert> Alerts { get; } = new();
    public ObservableCollection<FlightPathPoint> FlightPath { get; } = new();

    public bool IsRxLossActive
    {
        get => _isRxLossActive;
        private set => SetProperty(ref _isRxLossActive, value);
    }

    public bool IsLinkInterferenceActive
    {
        get => _isLinkInterferenceActive;
        private set => SetProperty(ref _isLinkInterferenceActive, value);
    }

    public bool IsBatteryInterferenceActive
    {
        get => _isBatteryInterferenceActive;
        private set => SetProperty(ref _isBatteryInterferenceActive, value);
    }

    public bool IsGpsInterferenceActive
    {
        get => _isGpsInterferenceActive;
        private set => SetProperty(ref _isGpsInterferenceActive, value);
    }

    public bool IsHighDescentActive
    {
        get => _isHighDescentActive;
        private set => SetProperty(ref _isHighDescentActive, value);
    }

    public bool IsOverspeedActive
    {
        get => _isOverspeedActive;
        private set => SetProperty(ref _isOverspeedActive, value);
    }

    public string ConnectionStatus => IsConnected ? "ONLINE" : "OFFLINE";
    public string ArmStatus => IsArmed ? "ARMED" : "DISARMED";

    public bool IsTelemetryStale =>
        LastTelemetryTimestamp is null ||
        DateTimeOffset.UtcNow - LastTelemetryTimestamp.Value >
        TimeSpan.FromSeconds(2);

    public string RxStatus
    {
        get
        {
            if (!IsConnected)
                return "OFFLINE";

            if (LastTelemetryTimestamp is null)
                return "WAITING";

            var age =
                DateTimeOffset.UtcNow -
                LastTelemetryTimestamp.Value;

            if (age < TimeSpan.FromSeconds(0.5))
                return "LIVE";

            if (age < TimeSpan.FromSeconds(2))
                return $"{age.TotalSeconds:F1}s";

            return $"STALE {age.TotalSeconds:F1}s";
        }
    }

    public HealthLevel BatteryHealth =>
        Battery < 15
            ? HealthLevel.Critical
            : Battery <= 30
                ? HealthLevel.Caution
                : HealthLevel.Normal;

    public HealthLevel LinkHealth =>
        LinkQuality < 50
            ? HealthLevel.Critical
            : LinkQuality <= 75
                ? HealthLevel.Caution
                : HealthLevel.Normal;

    public HealthLevel GpsHealth =>
        GpsSatellites == 0
            ? HealthLevel.Critical
            : GpsSatellites < 8
                ? HealthLevel.Caution
                : HealthLevel.Normal;

    [ObservableProperty]
    private double _homeLatitude;

    [ObservableProperty]
    private double _homeLongitude;

    [ObservableProperty]
    private double _latitude;

    [ObservableProperty]
    private double _longitude;

    [ObservableProperty]
    private double _altitude;

    [ObservableProperty]
    private double _airspeed;

    [ObservableProperty]
    private double _groundSpeed;

    [ObservableProperty]
    private double _heading;

    [ObservableProperty]
    private double _verticalSpeed;

    [ObservableProperty]
    private double _battery;

    [ObservableProperty]
    private double _linkQuality;

    [ObservableProperty]
    private int _gpsSatellites;

    [ObservableProperty]
    private FlightMode _flightMode = FlightMode.Standby;

    [ObservableProperty]
    private bool _isArmed;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private DateTimeOffset? _lastTelemetryTimestamp;

    public MainViewModel(
        ITelemetrySource telemetrySource,
        ISimulationControl simulationControl)
    {
        _telemetrySource = telemetrySource;
        _simulationControl = simulationControl;

        _telemetrySource.TelemetryReceived += OnTelemetryReceived;

        _rxAgeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };

        _rxAgeTimer.Tick += (_, _) =>
        {
            OnPropertyChanged(nameof(RxStatus));
            OnPropertyChanged(nameof(IsTelemetryStale));
        };

        _rxAgeTimer.Start();
    }

    // Connection commands

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (_telemetrySource.IsRunning)
            return;

        LastTelemetryTimestamp = null;

        await _telemetrySource.StartAsync();

        IsConnected = true;

        AddAlert(
            AlertSeverity.Info,
            "TELEMETRY CONNECTED");
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (!_telemetrySource.IsRunning)
            return;

        await _telemetrySource.StopAsync();

        _simulationControl.RestoreNominal();
        ClearInterferenceIndicators();
        ResetHealthBaselines();

        IsConnected = false;
        FlightMode = FlightMode.Standby;
        IsArmed = false;
        LastTelemetryTimestamp = null;

        AddAlert(
            AlertSeverity.Info,
            "TELEMETRY DISCONNECTED");
    }

    // Simulation interrupt commands

    [RelayCommand]
    private async Task InjectRxLoss()
    {
        if (!_telemetrySource.IsRunning)
            return;

        AddAlert(
            AlertSeverity.Caution,
            "RX LOSS INJECTED");

        IsRxLossActive = true;

        _simulationControl.InjectRxLoss(
            TimeSpan.FromSeconds(5));

        await Task.Delay(TimeSpan.FromSeconds(5));

        IsRxLossActive = false;
    }

    [RelayCommand]
    private async Task InjectLinkDegradation()
    {
        if (!_telemetrySource.IsRunning)
            return;

        AddAlert(
            AlertSeverity.Caution,
            "LINK DEGRADATION INJECTED");

        IsLinkInterferenceActive = true;

        _simulationControl.InjectLinkDegradation(
            TimeSpan.FromSeconds(8));

        await Task.Delay(TimeSpan.FromSeconds(8));

        IsLinkInterferenceActive = false;
    }

    [RelayCommand]
    private async Task InjectGpsLoss()
    {
        if (!_telemetrySource.IsRunning)
            return;

        IsGpsInterferenceActive = true;

        _simulationControl.InjectGpsLoss(
            TimeSpan.FromSeconds(6));

        await Task.Delay(TimeSpan.FromSeconds(6));

        IsGpsInterferenceActive = false;
    }

    [RelayCommand]
    private async Task InjectHighDescent()
    {
        if (!_telemetrySource.IsRunning)
            return;

        AddAlert(
            AlertSeverity.Caution,
            "HIGH DESCENT INJECTED");

        IsHighDescentActive = true;

        _simulationControl.InjectHighDescent(
            TimeSpan.FromSeconds(5));

        await Task.Delay(TimeSpan.FromSeconds(5));

        IsHighDescentActive = false;
    }

    [RelayCommand]
    private async Task InjectOverspeed()
    {
        if (!_telemetrySource.IsRunning)
            return;

        AddAlert(
            AlertSeverity.Caution,
            "OVERSPEED INJECTED");

        IsOverspeedActive = true;

        _simulationControl.InjectOverspeed(
            TimeSpan.FromSeconds(5));

        await Task.Delay(TimeSpan.FromSeconds(5));

        IsOverspeedActive = false;
    }

    [RelayCommand]
    private void SetLowBattery()
    {
        if (!_telemetrySource.IsRunning)
            return;

        IsBatteryInterferenceActive = true;
        _simulationControl.SetLowBattery();
    }

    [RelayCommand]
    private void SetCriticalBattery()
    {
        if (!_telemetrySource.IsRunning)
            return;

        IsBatteryInterferenceActive = true;
        _simulationControl.SetCriticalBattery();
    }

    [RelayCommand]
    private void RestoreNominal()
    {
        _simulationControl.RestoreNominal();
        ClearInterferenceIndicators();
    }

    // Telemetry event handling

    private void OnTelemetryReceived(
        object? sender,
        TelemetryReceivedEventArgs e)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            UpdateTelemetry(e.Telemetry);
        });
    }

    private void UpdateTelemetry(FlightTelemetry telemetry)
    {
        Latitude = telemetry.Latitude;
        Longitude = telemetry.Longitude;
        Altitude = telemetry.Altitude;
        Airspeed = telemetry.AirSpeed;
        GroundSpeed = telemetry.GroundSpeed;
        Heading = telemetry.HeadingDegrees;
        VerticalSpeed = telemetry.VerticalSpeed;
        Battery = telemetry.BatteryCharge;
        LinkQuality = telemetry.LinkQuality;
        GpsSatellites = telemetry.GpsSatellites;
        FlightMode = telemetry.FlightMode;
        IsArmed = telemetry.IsArmed;
        LastTelemetryTimestamp = telemetry.Timestamp;

        EvaluateAlerts();
        UpdateFlightPath(telemetry);
    }

    // Generated-property change hooks

    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(ConnectionStatus));
        OnPropertyChanged(nameof(RxStatus));
    }

    partial void OnIsArmedChanged(bool value)
    {
        OnPropertyChanged(nameof(ArmStatus));
    }

    partial void OnLastTelemetryTimestampChanged(DateTimeOffset? value)
    {
        OnPropertyChanged(nameof(RxStatus));
        OnPropertyChanged(nameof(IsTelemetryStale));
    }

    partial void OnBatteryChanged(double value)
    {
        OnPropertyChanged(nameof(BatteryHealth));
    }

    partial void OnLinkQualityChanged(double value)
    {
        OnPropertyChanged(nameof(LinkHealth));
    }

    partial void OnGpsSatellitesChanged(int value)
    {
        OnPropertyChanged(nameof(GpsHealth));
    }

    // Alert handling

    private void EvaluateAlerts()
    {
        EvaluateHealthTransition(
            ref _previousBatteryHealth,
            BatteryHealth,
            normalMessage: "BATTERY LEVEL RECOVERED",
            cautionMessage: "BATTERY LOW",
            criticalMessage: "BATTERY CRITICAL");

        EvaluateHealthTransition(
            ref _previousLinkHealth,
            LinkHealth,
            normalMessage: "DATA LINK NOMINAL",
            cautionMessage: "DATA LINK DEGRADED",
            criticalMessage: "DATA LINK CRITICAL");

        EvaluateHealthTransition(
            ref _previousGpsHealth,
            GpsHealth,
            normalMessage: "GPS SIGNAL NOMINAL",
            cautionMessage: "GPS DEGRADED",
            criticalMessage: "GPS SIGNAL LOST");
    }

    private void EvaluateHealthTransition(
        ref HealthLevel? previous,
        HealthLevel current,
        string normalMessage,
        string cautionMessage,
        string criticalMessage)
    {
        if (previous is null)
        {
            previous = current;
            return;
        }

        if (current == previous.Value)
            return;

        var (severity, message) = current switch
        {
            HealthLevel.Normal =>
                (AlertSeverity.Info, normalMessage),

            HealthLevel.Caution =>
                (AlertSeverity.Caution, cautionMessage),

            HealthLevel.Critical =>
                (AlertSeverity.Critical, criticalMessage),

            _ => throw new ArgumentOutOfRangeException(
                nameof(current),
                current,
                null)
        };

        AddAlert(severity, message);
        previous = current;
    }

    private void AddAlert(
        AlertSeverity severity,
        string message)
    {
        Alerts.Insert(
            0,
            new TelemetryAlert(
                DateTimeOffset.Now,
                severity,
                message));

        while (Alerts.Count > 50)
            Alerts.RemoveAt(Alerts.Count - 1);
    }

    private void ResetHealthBaselines()
    {
        _previousBatteryHealth = null;
        _previousLinkHealth = null;
        _previousGpsHealth = null;
    }

    // Flight-path history

    private void UpdateFlightPath(FlightTelemetry telemetry)
    {
        if (!_homeLocationSet)
        {
            HomeLatitude = telemetry.Latitude;
            HomeLongitude = telemetry.Longitude;
            _homeLocationSet = true;
        }

        _telemetrySampleCount++;

        if (_telemetrySampleCount < FlightPathSampleInterval)
            return;

        _telemetrySampleCount = 0;

        FlightPath.Add(
            new FlightPathPoint(
                Timestamp: telemetry.Timestamp,
                Latitude: telemetry.Latitude,
                Longitude: telemetry.Longitude,
                Altitude: telemetry.Altitude));

        while (FlightPath.Count > MaxFlightPathPoints)
            FlightPath.RemoveAt(0);
    }

    // UI state helpers

    private void ClearInterferenceIndicators()
    {
        IsRxLossActive = false;
        IsLinkInterferenceActive = false;
        IsBatteryInterferenceActive = false;
        IsGpsInterferenceActive = false;
        IsHighDescentActive = false;
        IsOverspeedActive = false;
    }
}