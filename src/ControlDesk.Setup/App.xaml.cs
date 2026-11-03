using System.Windows;

namespace ControlDesk.Setup;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Ensure single instance
        var mutex = new System.Threading.Mutex(true, "Global\\ControlDesk_Setup_SingleInstance_Mutex", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Setup is already running.", "ControlDesk Setup", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
    }
}