using System;
using System.Windows;
using ControlDesk.App.Services;
using ControlDesk.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ControlDesk.App.Views;

/// <summary>
/// Main window code-behind - wires up DataContext and WindowLifecycleManager.
/// </summary>
public partial class MainWindow : Window
{
    private readonly WindowLifecycleManager _windowLifecycleManager;
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel, WindowLifecycleManager windowLifecycleManager)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _windowLifecycleManager = windowLifecycleManager;

        DataContext = _viewModel;
        _windowLifecycleManager.RegisterMainWindow(this);

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Ensure view model is initialized
        _viewModel.InitializeAsync();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        // Cleanup is handled by WindowLifecycleManager
    }

    /// <summary>
    /// Bring window to front and focus when activated from tray or single-instance.
    /// </summary>
    public new void Activate()
    {
        base.Activate();
        Show();
        WindowState = WindowState.Normal;
        Focus();
    }
}