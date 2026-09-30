using System.Diagnostics;
using Serilog;
using System.Text;
using System.Threading;
using SPTInstaller.Models;

namespace SPTInstaller.Helpers;

public enum PatcherExitCode
{
    ProgramClosed = 0,
    Success = 10,
    EftExeNotFound = 11,
    NoPatchFolder = 12,
    MissingFile = 13,
    MissingDir = 14,
    PatchFailed = 15
}

public static class ProcessHelper
{
    public static Result PatchClientFiles(FileInfo executable, DirectoryInfo workingDir)
    {
        if (!executable.Exists || !workingDir.Exists)
        {
            return Result.FromError(
                $"Could not find executable ({executable.Name}) or working directory ({workingDir.Name})");
        }
        
        return PlatformOperations.Current.RunPatcher(executable, workingDir);
    }
    
    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not open {url}", url);
        }
    }

    public static ReadProcessResult RunAndReadProcessOutputs(string fileName, string args, int timeout = 5000)
    {
        using var proc = new Process();
        
        proc.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();
        
        using var outputWaitHandle = new AutoResetEvent(false);
        using var errorWaitHandle = new AutoResetEvent(false);
        
        proc.OutputDataReceived += (s, e) =>
        {
            if (e.Data == null)
            {
                outputWaitHandle.Set();
            }
            else
            {
                outputBuilder.AppendLine(e.Data);
            }
        };
        
        proc.ErrorDataReceived += (s, e) =>
        {
            if (e.Data == null)
            {
                errorWaitHandle.Set();
            }
            else
            {
                errorBuilder.AppendLine(e.Data);
            }
        };
        
        try
        {
            proc.Start();
        }
        catch (Exception ex)
        {
            return ReadProcessResult.FromError(ex.Message);
        }
        
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        
        if (!proc.WaitForExit(timeout) || !outputWaitHandle.WaitOne(timeout) || !errorWaitHandle.WaitOne(timeout))
        {
            return ReadProcessResult.FromError("Process timed out");
        }
        
        return ReadProcessResult.FromSuccess(outputBuilder.ToString(), errorBuilder.ToString());
    }

    public static Result RunEmbeddedScript(string scriptName, params string[] arguments)
    {
        var scriptFileInfo = new FileInfo(Path.Join(DownloadCacheHelper.CachePath, scriptName));

        if (!FileHelper.StreamAssemblyResourceOut(scriptName, scriptFileInfo.FullName))
        {
            return Result.FromError($"Failed to prepare script file {scriptName}");
        }

        if (!File.Exists(scriptFileInfo.FullName))
        {
            return Result.FromError($"Script file {scriptName} not found");
        }

        var processInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            CreateNoWindow = true,
            ArgumentList = {"-ExecutionPolicy", "Bypass", "-File", $"{scriptFileInfo.FullName}"}
        };

        foreach (var arg in arguments)
        {
            processInfo.ArgumentList.Add(arg);
        }

        Process.Start(processInfo);

        return Result.FromSuccess();
    }
}
