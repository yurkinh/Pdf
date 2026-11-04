using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Melville.Pdf.Maui.Sample.Spike;

public sealed class SpikeViewModel : INotifyPropertyChanged
{
    private bool isRunning;
    private ImageSource? preview;

    public ObservableCollection<SpikeLine> Results { get; } = [];

    public bool IsRunning
    {
        get => isRunning;
        private set => Set(ref isRunning, value);
    }

    /// <summary>Page 1 of the first document, rendered by the existing Skia renderer.</summary>
    public ImageSource? Preview
    {
        get => preview;
        private set => Set(ref preview, value);
    }

    public async Task RunAsync()
    {
        if (IsRunning) return;
        IsRunning = true;
        Results.Clear();
        try
        {
            foreach (var line in SpikeRunner.RunCanvasChecks()) Results.Add(line);
            var (core, png) = await Task.Run(SpikeRunner.RunCoreChecksAsync);
            foreach (var line in core) Results.Add(line);
            if (png is not null) Preview = ImageSource.FromStream(() => new MemoryStream(png));
        }
        finally
        {
            IsRunning = false;
            Console.WriteLine("[SPIKE] DONE");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
