using Serilog;
using SPTInstaller.CustomControls;
using SPTInstaller.Helpers;
using SPTInstaller.Installer_Tasks.PreChecks;
using SPTInstaller.Interfaces;
using SPTInstaller.Models;
using SPTInstaller.Models.ReleaseInfo;
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

    public IReadOnlyList<IPreCheck> PreChecks => _preChecks;
    
    public InstallController(IProgressableTask[] tasks, IPreCheck[] preChecks = null)
    {
        _tasks = tasks;
        _preChecks = preChecks;
        
        foreach (var check in _preChecks)
        {
            check.ReeevaluationRequested += OnReevaluationRequested;
        }
    }

    private void OnReevaluationRequested(object? sender, EventArgs e)
    {
        if (sender is not IPreCheck preCheck)
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
    }

    /// <summary>
    /// Swaps the runtime checks for the ones a release needs. Returns false when it already needs the same ones.
    /// </summary>
    public bool SetRuntimeRequirements(IReadOnlyList<RuntimeRequirement> requirements)
    {
        requirements = [.. requirements.Select(RuntimePreCheck.ForThisPlatform)];

        var current = _preChecks.OfType<RuntimePreCheck>().ToList();

        if (current.Select(check => Key(check.Requirement)).SequenceEqual(requirements.Select(Key)))
        {
            return false;
        }

        foreach (var check in current)
        {
            check.ReeevaluationRequested -= OnReevaluationRequested;
        }

        var others = _preChecks.Where(check => check is not RuntimePreCheck).ToList();

        // Runtimes go before the free space check, where the fixed .NET checks used to be
        var insertAt = others.FindIndex(check => check is FreeSpacePreCheck);
        IPreCheck[] added = [.. requirements.Select(requirement => new RuntimePreCheck(requirement))];

        foreach (var check in added)
        {
            check.ReeevaluationRequested += OnReevaluationRequested;
        }

        others.InsertRange(insertAt < 0 ? others.Count : insertAt, added);
        _preChecks = [.. others];

        Log.Information("Runtime checks: {runtimes}", string.Join(", ", requirements.Select(Key)));
        return true;

        static string Key(RuntimeRequirement requirement) => $"{requirement.Identifier} {requirement.MinimumVersion}";
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
            // The release can swap the runtime checks while this waits, so it runs the list it started with
            var preChecks = _preChecks;

            foreach (var check in preChecks.Where(check => check.State is not (StatusSpinner.SpinnerState.OK
                         or StatusSpinner.SpinnerState.Warning or StatusSpinner.SpinnerState.Error)))
            {
                check.State = StatusSpinner.SpinnerState.Pending;
            }

            foreach (var check in preChecks)
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

        DownloadCacheHelper.RemoveInstallFolderDownloads();
        
        return Result.FromSuccess("Install Complete. Happy Playing!");
    }
}