using System.Collections.Generic;
using System.Linq;
using Avalonia;
using ReactiveUI;
using Serilog;
using System.Globalization;
using System.Reflection;
using SPTInstaller.Helpers;
using SPTInstaller.Models;

namespace SPTInstaller.ViewModels;

public class MainWindowViewModel : ReactiveObject, IActivatableViewModel, IScreen
{
    public RoutingState Router { get; } = new();
    public ViewModelActivator Activator { get; } = new();

    private string _title;

    public string Title
    {
        get => _title;
        set => this.RaiseAndSetIfChanged(ref _title, value);
    }

    public string Version { get; }

    private IReadOnlyList<StepItem> _steps = [];

    public IReadOnlyList<StepItem> Steps
    {
        get => _steps;
        set => this.RaiseAndSetIfChanged(ref _steps, value);
    }

    public MainWindowViewModel(string installPath)
    {
        var data = ServiceHelper.Get<InternalData>() ?? throw new Exception("failed to get interanl data");

        Version = $"v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown"}{(data.DebugMode ? " debug" : "")}";

        Title =
            $"{(data.DebugMode ? "-debug-" : "")} SPT Installer {"v" + Assembly.GetExecutingAssembly().GetName()?.Version?.ToString() ?? "--unknown version--"}";

        Log.Information($"========= {Title} Started =========");
        Log.Information(Environment.OSVersion.VersionString);

        var uiCulture = CultureInfo.InstalledUICulture;

        Log.Information("System Language: {iso} - {name}", uiCulture.TwoLetterISOLanguageName, uiCulture.DisplayName);

        Router.CurrentViewModel.Subscribe(screen =>
        {
            if (screen is ViewModelBase current)
            {
                Steps = Enum.GetValues<InstallerStep>().Select(step => new StepItem(StepName(step),
                    step == current.Step, step < current.Step, step == current.Step && current.StepFailed)).ToList();
            }
        });
        Router.Navigate.Execute(new InstallerUpdateViewModel(this, installPath)).Subscribe();
    }

    private static string StepName(InstallerStep step) => step switch
    {
        InstallerStep.Start => "Start",
        InstallerStep.Folder => "Folder",
        InstallerStep.Checks => "Checks",
        _ => "Install",
    };

    public void CloseCommand()
    {
        if (Application.Current.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktopApp)
        {
            desktopApp.MainWindow.Close();
        }
    }

    public void MinimizeCommand()
    {
        if (Application.Current.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktopApp)
        {
            desktopApp.MainWindow.WindowState = Avalonia.Controls.WindowState.Minimized;
        }
    }
}
