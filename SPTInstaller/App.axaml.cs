using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Serilog;
using SPTInstaller.ViewModels;
using SPTInstaller.Views;
using System.Text;
using SPTInstaller.Helpers;
using SPTInstaller.Models;

namespace SPTInstaller;

public partial class App : Application
{
    public static string LogPath = Path.Join(Path.GetTempPath(), "spt-installer", "spt-installer.log");
    public static string LogDebugPath = Path.Join(Path.GetTempPath(), "spt-installer", "spt-installer-debug.log");
    
    public static void ReLaunch(bool debug, string installPath = "")
    {
        // The exe can be renamed, for example "SPTInstaller (1).exe" from a second download
        var installerPath = Environment.ProcessPath ?? Path.Join(AppContext.BaseDirectory, "SPTInstaller.exe");
        
        var args = new StringBuilder()
            .Append(debug ? "debug " : "")
            .Append(!string.IsNullOrEmpty(installPath) ? $"installPath=\"{installPath}\"" : "")
            .ToString();
        
        _ = SelfUpdater.HandOverAsync(new ProcessStartInfo()
        {
            FileName = installerPath,
            Arguments = args
        });
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
        SelfUpdater.CleanUpPrevious();
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