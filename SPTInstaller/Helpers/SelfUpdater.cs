using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace SPTInstaller.Helpers;

/// <summary>
/// Swaps the running installer for a downloaded one. Windows lets a running exe be renamed but not overwritten,
/// so the old one steps aside under a new name and the new one takes its place.
/// </summary>
public static class SelfUpdater
{
    private static string? ExePath => Environment.ProcessPath;

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    /// <summary>
    /// Starts the installer again and exits once its window is up. Windows only lets a new process take the
    /// foreground when the one in front allows it, so this one stays open until the new window can come forward.
    /// </summary>
    public static async Task HandOverAsync(ProcessStartInfo start)
    {
        var process = Process.Start(start);

        if (process != null && OperatingSystem.IsWindows())
        {
            AllowSetForegroundWindow(process.Id);

            for (var waited = 0; waited < 15000 && !process.HasExited; waited += 100)
            {
                process.Refresh();

                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    break;
                }

                await Task.Delay(100);
            }
        }

        Environment.Exit(0);
    }

    /// <summary>
    /// Deletes what the previous version left behind, in the background. The previous version only exits once
    /// this one's window is up, so waiting for it here would hold that window back.
    /// </summary>
    public static void CleanUpPrevious() => Task.Run(CleanUp);

    private static void CleanUp()
    {
        if (ExePath == null)
        {
            return;
        }

        // Older versions also wrote their PowerShell scripts into the cache
        string[] leftovers =
        [
            ExePath + ".old", ExePath + ".new",
            .. new[] { "update.ps1", "add_shortcuts.ps1", "desktop_shortcuts.ps1" }.Select(script => Path.Join(DownloadCacheHelper.CachePath, script)),
        ];

        foreach (var file in leftovers.Where(File.Exists))
        {
            // The previous version can take up to the 15 second hand over to exit
            for (var attempt = 0; attempt < 40; attempt++)
            {
                try
                {
                    File.Delete(file);
                    Log.Information("Removed {file} left by the last update", Path.GetFileName(file));
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Thread.Sleep(500);
                }
            }
        }
    }

    /// <summary>
    /// Puts <paramref name="newInstaller"/> in place of the running exe and starts it with the same arguments
    /// </summary>
    /// <returns>Null when the new installer was started, otherwise why it could not be</returns>
    public static async Task<string?> ReplaceAndRestart(FileInfo newInstaller)
    {
        if (ExePath == null)
        {
            return "Could not find where the installer is running from.";
        }

        var old = ExePath + ".old";
        var staged = ExePath + ".new";

        try
        {
            // Staged next to the exe so the swap below is a rename on the same drive, which cannot half finish
            File.Copy(newInstaller.FullName, staged, true);
            FileHelper.MakeExecutable(staged);
            File.Delete(old);
            File.Move(ExePath, old);

            try
            {
                File.Move(staged, ExePath);
            }
            catch
            {
                File.Move(old, ExePath);
                throw;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(ex, "Could not replace the installer at {path}", ExePath);

            try
            {
                File.Delete(staged);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                Log.Warning(cleanup, "Could not remove {file}", staged);
            }

            return $"The installer can't update itself in {Path.GetDirectoryName(ExePath)}. " +
                   "Move it to a folder you can write to, like Downloads or Desktop, or download the new version yourself.";
        }

        Log.Information("Installer replaced, starting the new version");

        var start = new ProcessStartInfo(ExePath) { UseShellExecute = false };

        foreach (var arg in Environment.GetCommandLineArgs().Skip(1))
        {
            start.ArgumentList.Add(arg);
        }

        await HandOverAsync(start);

        return null;
    }
}
