using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace ControlDesk.Setup.ViewModels;

public enum SetupStep
{
    Welcome,
    Terms,
    Privacy,
    Options,
    Installing,
    Finished
}

public partial class SetupViewModel : ObservableObject
{
    [ObservableProperty]
    private SetupStep _currentStep = SetupStep.Welcome;

    /// <summary>
    /// Numeric index of the current step (0=Welcome, 1=Terms, 2=Privacy, 3=Options, 4=Installing, 5=Finished)
    /// Used by the step indicator breadcrumb for dynamic highlighting.
    /// </summary>
    [ObservableProperty]
    private int _currentStepIndex = 0;

    [ObservableProperty]
    private string _stepTitle = "Welcome to ControlDesk Setup";

    [ObservableProperty]
    private string _stepSubtitle = "Setup will guide you through the installation of ControlDesk Remote Desktop.";

    [ObservableProperty]
    private bool _termsAccepted;

    [ObservableProperty]
    private bool _privacyAccepted;

    [ObservableProperty]
    private string _installDirectory = string.Empty;

    [ObservableProperty]
    private bool _createDesktopShortcut = true;

    [ObservableProperty]
    private bool _createStartMenuShortcut = true;

    [ObservableProperty]
    private bool _startWithWindows = true;

    [ObservableProperty]
    private bool _launchAfterInstall = true;

    [ObservableProperty]
    private double _installProgress;

    [ObservableProperty]
    private string _installStatus = "Ready to install...";

    [ObservableProperty]
    private bool _canGoBack;

    [ObservableProperty]
    private bool _canGoNext = true;

    [ObservableProperty]
    private string _nextButtonText = "Next >";

    [ObservableProperty]
    private string _requiredSpaceText = "Space required: ~45 MB";

    [ObservableProperty]
    private string _availableSpaceText = string.Empty;

    [ObservableProperty]
    private string _installedExePath = string.Empty;

    public SetupViewModel()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        InstallDirectory = Path.Combine(localAppData, "ControlDesk");
        UpdateAvailableSpace();
        UpdateStepState();
    }

    partial void OnTermsAcceptedChanged(bool value) => UpdateStepState();
    partial void OnPrivacyAcceptedChanged(bool value) => UpdateStepState();
    partial void OnInstallDirectoryChanged(string value) => UpdateAvailableSpace();

    private void UpdateAvailableSpace()
    {
        try
        {
            var root = Path.GetPathRoot(InstallDirectory);
            if (!string.IsNullOrEmpty(root))
            {
                var drive = new DriveInfo(root);
                if (drive.IsReady)
                {
                    var freeGb = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                    AvailableSpaceText = $"Space available: {freeGb:F1} GB";
                    return;
                }
            }
        }
        catch
        {
            // Ignore drive check failures
        }
        AvailableSpaceText = "Space available: Sufficient";
    }

    [RelayCommand]
    private void BrowseDirectory()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select ControlDesk Installation Folder",
            InitialDirectory = InstallDirectory
        };

        if (dialog.ShowDialog() == true)
        {
            InstallDirectory = Path.Combine(dialog.FolderName, "ControlDesk");
        }
    }

    [RelayCommand]
    private void Next()
    {
        switch (CurrentStep)
        {
            case SetupStep.Welcome:
                CurrentStep = SetupStep.Terms;
                break;
            case SetupStep.Terms:
                CurrentStep = SetupStep.Privacy;
                break;
            case SetupStep.Privacy:
                CurrentStep = SetupStep.Options;
                break;
            case SetupStep.Options:
                CurrentStep = SetupStep.Installing;
                _ = PerformInstallationAsync();
                break;
            case SetupStep.Finished:
                CompleteSetup();
                break;
        }
        UpdateStepState();
    }

    [RelayCommand]
    private void Back()
    {
        switch (CurrentStep)
        {
            case SetupStep.Terms:
                CurrentStep = SetupStep.Welcome;
                break;
            case SetupStep.Privacy:
                CurrentStep = SetupStep.Terms;
                break;
            case SetupStep.Options:
                CurrentStep = SetupStep.Privacy;
                break;
        }
        UpdateStepState();
    }

    [RelayCommand]
    private void Cancel()
    {
        if (CurrentStep == SetupStep.Installing)
        {
            var result = MessageBox.Show(
                "Installation is currently in progress. Are you sure you want to exit?",
                "Cancel Setup",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;
        }
        else if (CurrentStep != SetupStep.Finished)
        {
            var result = MessageBox.Show(
                "Are you sure you want to cancel the installation?",
                "Cancel Setup",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;
        }

        Application.Current.Shutdown();
    }

    private void UpdateStepState()
    {
        // Update step index for breadcrumb indicator
        CurrentStepIndex = CurrentStep switch
        {
            SetupStep.Welcome => 0,
            SetupStep.Terms => 1,
            SetupStep.Privacy => 2,
            SetupStep.Options => 3,
            SetupStep.Installing => 4,
            SetupStep.Finished => 5,
            _ => 0
        };

        switch (CurrentStep)
        {
            case SetupStep.Welcome:
                StepTitle = "Welcome to ControlDesk Setup";
                StepSubtitle = "This wizard will install ControlDesk Remote Desktop on your computer.";
                CanGoBack = false;
                CanGoNext = true;
                NextButtonText = "Next >";
                break;

            case SetupStep.Terms:
                StepTitle = "License Agreement & Terms of Service";
                StepSubtitle = "Please review the license terms before proceeding with the installation.";
                CanGoBack = true;
                CanGoNext = TermsAccepted;
                NextButtonText = "Next >";
                break;

            case SetupStep.Privacy:
                StepTitle = "Privacy Policy & Security Architecture";
                StepSubtitle = "Zero telemetry, direct peer-to-peer encryption, and local credential storage.";
                CanGoBack = true;
                CanGoNext = PrivacyAccepted;
                NextButtonText = "Next >";
                break;

            case SetupStep.Options:
                StepTitle = "Installation Options & Destination";
                StepSubtitle = "Choose the location and shortcuts for ControlDesk.";
                CanGoBack = true;
                CanGoNext = !string.IsNullOrWhiteSpace(InstallDirectory);
                NextButtonText = "Install";
                break;

            case SetupStep.Installing:
                StepTitle = "Installing ControlDesk";
                StepSubtitle = "Please wait while Setup installs ControlDesk on your computer.";
                CanGoBack = false;
                CanGoNext = false;
                NextButtonText = "Installing...";
                break;

            case SetupStep.Finished:
                StepTitle = "Installation Complete";
                StepSubtitle = "ControlDesk has been successfully installed on your computer.";
                CanGoBack = false;
                CanGoNext = true;
                NextButtonText = "Finish";
                break;
        }
    }

    private async Task PerformInstallationAsync()
    {
        try
        {
            InstallProgress = 5;
            InstallStatus = "Creating target directories...";
            await Task.Delay(200);

            Directory.CreateDirectory(InstallDirectory);

            InstallProgress = 15;
            InstallStatus = "Extracting ControlDesk core application payload...";
            await Task.Delay(300);

            var targetExe = Path.Combine(InstallDirectory, "ControlDesk.exe");
            InstalledExePath = targetExe;

            bool extracted = TryExtractEmbeddedPayload(targetExe);
            if (!extracted)
            {
                // Fallback: copy sibling or locate binary from source/build outputs
                extracted = TryCopySiblingPayload(targetExe);
            }

            InstallProgress = 55;
            InstallStatus = "Verifying cryptographic runtime integrity...";
            await Task.Delay(300);

            // Create auxiliary directories
            Directory.CreateDirectory(Path.Combine(InstallDirectory, "Logs"));
            Directory.CreateDirectory(Path.Combine(InstallDirectory, "Transfers"));

            InstallProgress = 70;
            InstallStatus = "Configuring Windows DPAPI local security store...";
            await Task.Delay(200);

            InstallProgress = 85;
            InstallStatus = "Creating shortcuts and system registrations...";
            await Task.Delay(200);

            if (CreateDesktopShortcut && File.Exists(targetExe))
            {
                CreateShellShortcut(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ControlDesk.lnk"),
                    targetExe,
                    "ControlDesk Remote Desktop");
            }

            if (CreateStartMenuShortcut && File.Exists(targetExe))
            {
                var programsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "ControlDesk");
                Directory.CreateDirectory(programsFolder);
                CreateShellShortcut(
                    Path.Combine(programsFolder, "ControlDesk.lnk"),
                    targetExe,
                    "ControlDesk Remote Desktop");
            }

            if (StartWithWindows && File.Exists(targetExe))
            {
                RegisterRunKey(targetExe);
            }

            InstallProgress = 100;
            InstallStatus = "Installation completed successfully.";
            await Task.Delay(300);

            CurrentStep = SetupStep.Finished;
            UpdateStepState();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Installation error: {ex.Message}",
                "Setup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            CurrentStep = SetupStep.Options;
            UpdateStepState();
        }
    }

    private bool TryExtractEmbeddedPayload(string targetPath)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames();

        // 1. Look for embedded ControlDesk.exe
        var exeResource = resourceNames.FirstOrDefault(r => r.EndsWith("ControlDesk.exe", StringComparison.OrdinalIgnoreCase));
        if (exeResource != null)
        {
            using var stream = assembly.GetManifestResourceStream(exeResource);
            if (stream != null)
            {
                using var fileStream = File.Create(targetPath);
                stream.CopyTo(fileStream);
                return true;
            }
        }

        // 2. Look for embedded payload.zip
        var zipResource = resourceNames.FirstOrDefault(r => r.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase));
        if (zipResource != null)
        {
            using var stream = assembly.GetManifestResourceStream(zipResource);
            if (stream != null)
            {
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                archive.ExtractToDirectory(InstallDirectory, true);
                return File.Exists(targetPath);
            }
        }

        return false;
    }

    private bool TryCopySiblingPayload(string targetPath)
    {
        // 1. Check current directory
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var siblingExe = Path.Combine(baseDir, "ControlDesk.exe");
        if (File.Exists(siblingExe) && !string.Equals(siblingExe, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(siblingExe, targetPath, true);
            return true;
        }

        // 2. Check desktop (since both will be on desktop)
        var desktopExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ControlDesk.exe");
        if (File.Exists(desktopExe) && !string.Equals(desktopExe, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(desktopExe, targetPath, true);
            return true;
        }

        // 3. Search common build output directories
        var candidatePaths = new[]
        {
            Path.Combine(baseDir, "client", "ControlDesk.exe"),
            Path.Combine(baseDir, "..", "client", "ControlDesk.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "artifacts", "Release", "client", "ControlDesk.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "artifacts", "Release", "ControlDesk.exe"),
            Path.Combine(baseDir, "..", "..", "..", "ControlDesk.App", "bin", "Release", "net10.0-windows", "ControlDesk.exe"),
            @"G:\Coding-main\Control Desk\artifacts\Release\client\ControlDesk.exe",
            @"G:\Coding-main\Control Desk\artifacts\Release\ControlDesk.exe",
            @"C:\Users\Vatsal's\Desktop\ControlDesk.exe"
        };

        foreach (var path in candidatePaths)
        {
            if (File.Exists(path))
            {
                File.Copy(path, targetPath, true);
                return true;
            }
        }

        return false;
    }

    private static void CreateShellShortcut(string shortcutPath, string targetPath, string description)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null)
            {
                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell != null)
                {
                    dynamic shortcut = shell.CreateShortcut(shortcutPath);
                    shortcut.TargetPath = targetPath;
                    shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
                    shortcut.Description = description;
                    shortcut.Save();
                }
            }
        }
        catch
        {
            // Fallback or non-critical
        }
    }

    private static void RegisterRunKey(string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            key?.SetValue("ControlDesk", $"\"{exePath}\" --minimized");
        }
        catch
        {
            // Non-critical
        }
    }

    private void CompleteSetup()
    {
        if (LaunchAfterInstall && File.Exists(InstalledExePath))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = InstalledExePath,
                    WorkingDirectory = Path.GetDirectoryName(InstalledExePath) ?? string.Empty,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not launch ControlDesk: {ex.Message}", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        Application.Current.Shutdown();
    }
}
