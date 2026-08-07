using System.Linq;
using System.Reactive;
using Avalonia;
using ReactiveUI.Avalonia;
using ReactiveUI;
using Serilog;
using Splat;
using SPTInstaller.Controllers;
using SPTInstaller.CustomControls;
using SPTInstaller.Helpers;
using SPTInstaller.Installer_Tasks;
using SPTInstaller.Installer_Tasks.PreChecks;
using SPTInstaller.Interfaces;
using SPTInstaller.Models;
using SPTInstaller.ViewModels;

namespace SPTInstaller;

internal class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Installer closed unexpectedly");
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        // Register all the things
        // Regestering as base classes so ReactiveUI works correctly. Doesn't seem to like the interfaces :(
        ServiceHelper.Register<InternalData>();

#if !TEST
        ServiceHelper.Register<PreCheckBase, GameInstalledPreCheck>();
        ServiceHelper.Register<PreCheckBase, NetFramework472PreCheck>();
        ServiceHelper.Register<PreCheckBase, DotnetRuntimePreCheck>();
        ServiceHelper.Register<PreCheckBase, FreeSpacePreCheck>();
        ServiceHelper.Register<PreCheckBase, GameLauncherPreCheck>();

        ServiceHelper.Register<InstallerTaskBase, InitializationTask>();
        ServiceHelper.Register<InstallerTaskBase, ReleaseCheckTask>();
        ServiceHelper.Register<InstallerTaskBase, DownloadTask>();
        ServiceHelper.Register<InstallerTaskBase, CopyClientTask>();
        ServiceHelper.Register<InstallerTaskBase, SetupClientTask>();
#else
        for (int i = 0; i < 5; i++)
        {
            Locator.CurrentMutable.RegisterConstant<InstallerTaskBase>(TestTask.FromRandomName());
        }

        Locator.CurrentMutable.RegisterConstant<PreCheckBase>(
            TestPreCheck.FromRandomName(StatusSpinner.SpinnerState.OK)
        );
        Locator.CurrentMutable.RegisterConstant<PreCheckBase>(
            TestPreCheck.FromRandomName(StatusSpinner.SpinnerState.Warning)
        );
        Locator.CurrentMutable.RegisterConstant<PreCheckBase>(
            TestPreCheck.FromRandomName(StatusSpinner.SpinnerState.Error)
        );
#endif
        // need the interfaces for the controller and splat won't resolve them since we need to base classes in avalonia (what a mess), so doing it manually here
        var tasks =
            Locator.Current.GetServices<InstallerTaskBase>().ToArray() as IProgressableTask[];
        var preChecks = Locator.Current.GetServices<PreCheckBase>().ToArray() as IPreCheck[];

        var installer = new InstallController(tasks, preChecks);

        // manually register install controller
        Locator.CurrentMutable.RegisterConstant(installer);

        return AppBuilder.Configure<App>()
            .UseReactiveUI(builder => builder
                .WithExceptionHandler(Observer.Create<Exception>(ex => Log.Error(ex, "An application exception occurred")))
                .RegisterView<Views.OverviewView, OverviewViewModel>()
                .RegisterView<Views.PreChecksView, PreChecksViewModel>()
                .RegisterView<Views.InstallPathSelectionView, InstallPathSelectionViewModel>()
                .RegisterView<Views.InstallView, InstallViewModel>()
                .RegisterView<Views.InstallerUpdateView, InstallerUpdateViewModel>()
                .RegisterView<Views.MessageView, MessageViewModel>())
            .UsePlatformDetect()
            .LogToTrace();
    }
}
