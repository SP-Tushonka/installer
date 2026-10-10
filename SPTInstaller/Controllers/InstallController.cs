using Serilog;
using SPTInstaller.CustomControls;
using SPTInstaller.Interfaces;
using SPTInstaller.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SPTInstaller.Controllers;

public class InstallController
{
    public event EventHandler RecheckRequested = delegate { };
    public event EventHandler<IProgressableTask> TaskChanged = delegate { };
    
    private bool _installRunning = false;
    private readonly SemaphoreSlim _preCheckRun = new(1, 1);
    private IPreCheck[] _preChecks { get; set; }
    private IProgressableTask[] _tasks { get; set; }
    
    public InstallController(IProgressableTask[] tasks, IPreCheck[] preChecks = null)
    {
        _tasks = tasks;
        _preChecks = preChecks;
        
        foreach (var check in _preChecks)
        {
            check.ReeevaluationRequested += (s, _) =>
            {
                if (s is not IPreCheck preCheck)
                {
                    return;
                }
                
                Log.Information($"{preCheck.Name}: requested re-evaluation");
                
                if (_installRunning)
                {
                    Log.Warning("Install is running, re-evaluation denied (how did you do this?)");
                    return;
                }
                
                RecheckRequested?.Invoke(this, null);
            };
        }
    }
    
    public async Task<IResult> RunPreChecks()
    {
        // Picking a version reruns the checks, which can start while the screen's first run is still going
        await _preCheckRun.WaitAsync();

        try
        {
            Log.Information("-<>--<>- Running PreChecks -<>--<>-");
            var requiredResults = new List<IResult>();

            // A check that already has a result keeps showing it, so a rerun does not flash the details pane
            foreach (var check in _preChecks.Where(check => check.State is not (StatusSpinner.SpinnerState.OK
                         or StatusSpinner.SpinnerState.Warning or StatusSpinner.SpinnerState.Error)))
            {
                check.State = StatusSpinner.SpinnerState.Pending;
            }

            foreach (var check in _preChecks)
            {
                var result = await check.RunCheck();

                Log.Information(
                    $"PreCheck: {check.Name} ({(check.IsRequired ? "Required" : "Optional")}) -> {(result.Succeeded ? (check.State == StatusSpinner.SpinnerState.Warning ? "Passed with a warning" : "Passed") : "Failed")}\nDetail: {check.PreCheckDetails.Replace("**", "").ReplaceLineEndings(" ")}");

                if (check.IsRequired)
                {
                    requiredResults.Add(result);
                }
            }

            if (requiredResults.Any(result => !result.Succeeded))
            {
                return Result.FromError("Some required checks have failed");
            }

            return Result.FromSuccess();
        }
        finally
        {
            _preCheckRun.Release();
        }
    }
    
    public async Task<IResult> RunTasks()
    {
        _installRunning = true;
        
        Log.Information("-<>--<>- Running Installer Tasks -<>--<>-");
        
        foreach (var task in _tasks)
        {
            TaskChanged?.Invoke(null, task);
            
            var result = await task.RunAsync();
            
            if (!result.Succeeded) return result;
        }
        
        return Result.FromSuccess("Install Complete. Happy Playing!");
    }
}