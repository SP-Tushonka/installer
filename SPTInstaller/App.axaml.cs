using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Serilog;
using SPTInstaller.ViewModels;
using SPTInstaller.Views;
using SPTInstaller.Helpers;
using SPTInstaller.Models;

namespace SPTInstaller;

public partial class App : Application
{
    public static string LogPath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "spt-installer", "spt-installer.log");
    public static string LogDebugPath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "spt-installer", "spt-installer-debug.log");
    
    public static void ReLaunch(bool debug, string installPath = "")
    {
        var installerPath = Environment.ProcessPath ??
                            throw new InvalidOperationException("Could not locate the running installer.");
        var start = new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = false
        };
        if (debug) start.ArgumentList.Add("debug");
        if (!string.IsNullOrEmpty(installPath)) start.ArgumentList.Add($"installPath={installPath}");
        Process.Start(start);
        
        Environment.Exit(0);
    }
    
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo
            .File(path: LogPath,
                restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information)
            .CreateLogger();

        DownloadCacheHelper.ClearCacheOnVersionChange(
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown");

        DownloadCacheHelper.ClearPartialDownloads();
    }
    
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var data = ServiceHelper.Get<InternalData>() ?? throw new Exception("failed to get internal data");
            
            data.DebugMode = false;
            var providedPath = "";
            
            if (desktop.Args != null)
            {
                data.DebugMode = desktop.Args.Any(x => x.ToLower() == "debug");
                var installPath = desktop.Args.FirstOrDefault(x => x.StartsWith("installPath=", StringComparison.CurrentCultureIgnoreCase));
                
                providedPath = installPath != null && installPath.Contains('=') ? installPath?.Split('=')[1] ?? "" : "";
            }
            
            if (data.DebugMode)
            {
                Log.CloseAndFlush();
                
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo
                    .File(path: LogDebugPath,
                        restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Debug)
                    .CreateLogger();
                
                Trace.Listeners.Add(new SerilogTraceListener.SerilogTraceListener());
                
                Log.Debug("TraceListener is registered");
            }
            
            var viewModel = new MainWindowViewModel(providedPath);
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
                ViewModel = viewModel,
            };
        }
        
        base.OnFrameworkInitializationCompleted();
    }
}
