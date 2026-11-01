using ReactiveUI;

namespace SPTInstaller.Models;

/// <summary>
/// One of the patcher's counters, such as "New Files to Add", shown as a row while patching
/// </summary>
public class PatchRow(string name, int total) : ReactiveObject
{
    public string Name { get; } = name;

    private string _text = total == 0 ? "none" : $"0 / {total:N0}";

    public string Text
    {
        get => _text;
        private set => this.RaiseAndSetIfChanged(ref _text, value);
    }

    // A kind the patch has none of starts finished
    private int _percent = total == 0 ? 100 : 0;

    public int Percent
    {
        get => _percent;
        private set => this.RaiseAndSetIfChanged(ref _percent, value);
    }

    public bool Completed => Percent == 100;

    public void Update(int remaining)
    {
        if (total == 0)
        {
            return;
        }

        var done = total - remaining;
        Text = $"{done:N0} / {total:N0}";
        Percent = done * 100 / total;
        this.RaisePropertyChanged(nameof(Completed));
    }
}
