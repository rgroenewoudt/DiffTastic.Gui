using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace DiffTastic.Gui.Tests;

public static class Screenshots
{
    /// <summary>Folder for PNGs; override with DIFFTASTIC_SCREENSHOTS.</summary>
    public static string Folder { get; } = Environment.GetEnvironmentVariable("DIFFTASTIC_SCREENSHOTS")
        ?? Path.Combine(Path.GetTempPath(), "difftastic-screenshots");

    public static string Save(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, name + ".png");
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered.");
        frame.Save(path, PngBitmapEncoderOptions.Default);
        return path;
    }
}
