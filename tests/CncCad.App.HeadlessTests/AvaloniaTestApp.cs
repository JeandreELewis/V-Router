using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

[assembly: AvaloniaTestApplication(typeof(CncCad.App.HeadlessTests.TestAvaloniaAppBuilder))]

namespace CncCad.App.HeadlessTests;

public static class TestAvaloniaAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()=>AppBuilder.Configure<TestApplication>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public sealed class TestApplication:Application;
