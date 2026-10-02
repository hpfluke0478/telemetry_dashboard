using TelemetryDashboard.Infrastructure.Telemetry;

namespace TelemetryDashboard.Tests;

public class SimulatedTelemetrySourceTests
{
    [Fact]
    public async Task StartAsync_SetsIsRunningTrue()
    {
        var source = new SimulatedTelemetrySource();

        await source.StartAsync();

        Assert.True(source.IsRunning);

        await source.StopAsync();
    }

    [Fact]
    public async Task StopAsync_SetsIsRunningFalse()
    {
        var source = new SimulatedTelemetrySource();

        await source.StartAsync();
        await source.StopAsync();

        Assert.False(source.IsRunning);
    }

    [Fact]
    public async Task StartAsync_ProducesTelemetry()
    {
        var source = new SimulatedTelemetrySource();

        var received = new TaskCompletionSource<bool>();

        source.TelemetryReceived += (_, _) =>
        {
            received.TrySetResult(true);
        };

        await source.StartAsync();

        var completed = await Task.WhenAny(
            received.Task,
            Task.Delay(1000));

        await source.StopAsync();

        Assert.Same(received.Task, completed);
        Assert.True(received.Task.Result);
    }

        [Fact]
    public async Task GpsLoss_ReportsZeroSatellites()
    {
        var source = new SimulatedTelemetrySource();

        var received =
            new TaskCompletionSource<int>();

        source.TelemetryReceived += (_, e) =>
        {
            received.TrySetResult(
                e.Telemetry.GpsSatellites);
        };

        source.InjectGpsLoss(
            TimeSpan.FromSeconds(1));

        await source.StartAsync();

        var completed = await Task.WhenAny(
            received.Task,
            Task.Delay(1000));

        await source.StopAsync();

        Assert.Same(received.Task, completed);
        Assert.Equal(0, await received.Task);
    }

    [Fact]
    public async Task RxLoss_StopsTelemetryWithoutStoppingSource()
    {
        var source = new SimulatedTelemetrySource();

        int receivedCount = 0;

        var resumed =
            new TaskCompletionSource<bool>();

        source.TelemetryReceived += (_, _) =>
        {
            Interlocked.Increment(
                ref receivedCount);

            resumed.TrySetResult(true);
        };

        source.InjectRxLoss(
            TimeSpan.FromMilliseconds(400));

        await source.StartAsync();

        await Task.Delay(200);

        Assert.True(source.IsRunning);
        Assert.Equal(0, receivedCount);

        var completed = await Task.WhenAny(
            resumed.Task,
            Task.Delay(1500));

        await source.StopAsync();

        Assert.Same(resumed.Task, completed);
        Assert.True(receivedCount > 0);
    }
}
    