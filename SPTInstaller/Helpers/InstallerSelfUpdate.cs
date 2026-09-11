using System.Diagnostics;
using SPTInstaller.Models;

namespace SPTInstaller.Helpers;

public static class InstallerSelfUpdate
{
    public static Result LaunchReplacement(string replacementPath)
    {
        try
        {
            var destination = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(destination))
                return Result.FromError("Could not locate the running installer.");

            PlatformOperations.Current.EnsureExecutable(replacementPath);
            var start = new ProcessStartInfo
            {
                FileName = replacementPath,
                UseShellExecute = false
            };
            start.ArgumentList.Add("--replace-installer");
            start.ArgumentList.Add(destination);
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            Process.Start(start);
            return Result.FromSuccess();
        }
        catch (Exception ex)
        {
            return Result.FromError(ex.Message);
        }
    }

    public static int ReplaceRunningInstaller(string destination, int processId)
    {
        try
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                process.WaitForExit(30_000);
                if (!process.HasExited) return 1;
            }
            catch (ArgumentException)
            {
                // The old installer already exited.
            }

            var source = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(source)) return 1;

            File.Copy(source, destination, true);
            PlatformOperations.Current.EnsureExecutable(destination);
            Process.Start(new ProcessStartInfo(destination) { UseShellExecute = false });
            return 0;
        }
        catch
        {
            return 1;
        }
    }
}
