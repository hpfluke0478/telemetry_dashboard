using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;
using TelemetryDashboard.App.ViewModels;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Infrastructure.Telemetry;

namespace TelemetryDashboard.App;

public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<SimulatedTelemetrySource>();

                services.AddSingleton<ITelemetrySource>(provider =>
                    provider.GetRequiredService<SimulatedTelemetrySource>());

                services.AddSingleton<ISimulationControl>(provider =>
                    provider.GetRequiredService<SimulatedTelemetrySource>());

                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            await _host.StartAsync();

            var mainWindow =
                _host.Services.GetRequiredService<MainWindow>();

            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Application startup failed:\n\n{ex.Message}",
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown();
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            var telemetrySource =
                _host.Services.GetRequiredService<ITelemetrySource>();

            await telemetrySource.StopAsync();
            await _host.StopAsync();
        }
        finally
        {
            _host.Dispose();
            base.OnExit(e);
        }
    }
}