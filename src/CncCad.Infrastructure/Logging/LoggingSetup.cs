using Serilog;

namespace CncCad.Infrastructure.Logging;

public static class LoggingSetup
{
    public static ILogger Create(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        return new LoggerConfiguration().MinimumLevel.Information().WriteTo.File(Path.Combine(dataDirectory, "cnccad-.log"), rollingInterval: RollingInterval.Day).CreateLogger();
    }
}
