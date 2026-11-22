using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(DiffTastic.Gui.Tests.TestAppBuilder))]

namespace DiffTastic.Gui.Tests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        Localization.Apply("en");
        return AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
