using System.Windows;
using TelemetryDashboard.App.ViewModels;

namespace TelemetryDashboard.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;
    }
}