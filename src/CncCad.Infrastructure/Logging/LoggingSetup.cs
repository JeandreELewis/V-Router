using Serilog;

namespace CncCad.Infrastructure.Logging;

public static class LoggingSetup
{
    public static ILogger Create(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        return new LoggerConfiguration().MinimumLevel.Information().WriteTo.File(Path.Combine(dataDirectory, "v-router-.log"), rollingInterval: RollingInterval.Day).CreateLogger();
    }

    private static int initialized;
    public static void InitializeLocalCrashLogging(string dataDirectory)
    {
        if(Interlocked.Exchange(ref initialized,1)!=0)return;
        Log.Logger=Create(dataDirectory);
        AppDomain.CurrentDomain.UnhandledException+=(_,args)=>Log.Fatal(args.ExceptionObject as Exception,"Unhandled application exception");
        TaskScheduler.UnobservedTaskException+=(_,args)=>{Log.Error(args.Exception,"Unobserved background task exception");args.SetObserved();};
        Log.Information("V-Router started. Logs remain local; telemetry is disabled.");
    }
}
