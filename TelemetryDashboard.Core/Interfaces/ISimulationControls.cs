namespace TelemetryDashboard.Core.Interfaces;

public interface ISimulationControl
{
    void InjectRxLoss(TimeSpan duration);
    void InjectLinkDegradation(TimeSpan duration);

    void SetLowBattery();
    void SetCriticalBattery();

    void InjectGpsLoss(TimeSpan duration);
    void InjectHighDescent(TimeSpan duration);
    void InjectOverspeed(TimeSpan duration);

    void RestoreNominal();
}