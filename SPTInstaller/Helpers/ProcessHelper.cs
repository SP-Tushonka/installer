using System.Diagnostics;
using Serilog;
using System.Text;
using System.Threading;
using SPTInstaller.Models;

namespace SPTInstaller.Helpers;

public static class ProcessHelper
{
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

}