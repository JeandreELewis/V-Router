using System.Text.Json;

namespace CncCad.Infrastructure.Settings;

public enum AccentPreference { CncBlue, System }
public enum StartupWorkspace { Design, Toolpaths }
public enum WorkspaceMode { Startup, Design, Toolpaths }
public enum ViewportLayout { Design2D, Preview3D, HorizontalSplit, VerticalSplit }

public sealed record ApplicationPreferences
{
    public StartupWorkspace StartupWorkspace { get; init; } = StartupWorkspace.Design;
    public bool ShowGrid { get; init; } = true;
    public double GridSpacingMm { get; init; } = 10;
    public bool SnappingEnabled { get; init; } = true;
    public bool SnapToGrid { get; init; } = true;
    public bool SnapToEndpoints { get; init; } = true;
    public bool SnapToMidpoints { get; init; } = true;
    public bool SnapToCenters { get; init; } = true;
    public bool ShowToolpaths { get; init; } = true;
    public AccentPreference Accent { get; init; } = AccentPreference.CncBlue;
    public bool ConfirmBeforeReplacingUnsavedJob { get; init; } = true;
    public WorkspaceMode LastWorkspaceMode { get; init; } = WorkspaceMode.Startup;
    public ViewportLayout ViewportLayout { get; init; } = ViewportLayout.Design2D;

    public static ApplicationPreferences Defaults { get; } = new();
}

public interface IApplicationPreferencesStore
{
    Task<ApplicationPreferences> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ApplicationPreferences preferences, CancellationToken cancellationToken = default);
    Task ResetAsync(CancellationToken cancellationToken = default);
}

public sealed class JsonApplicationPreferencesStore(string settingsPath) : IApplicationPreferencesStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public async Task<ApplicationPreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(settingsPath)) return ApplicationPreferences.Defaults;
        try
        {
            await using var stream = File.OpenRead(settingsPath);
            var settings = await JsonSerializer.DeserializeAsync<ApplicationPreferences>(stream, Options, cancellationToken);
            return Normalize(settings ?? ApplicationPreferences.Defaults);
        }
        catch (JsonException) { return ApplicationPreferences.Defaults; }
        catch (IOException) { return ApplicationPreferences.Defaults; }
    }

    public async Task SaveAsync(ApplicationPreferences preferences, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var temporary = settingsPath + ".tmp";
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, Normalize(preferences), Options, cancellationToken);
        File.Move(temporary, settingsPath, true);
    }

    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(settingsPath)) File.Delete(settingsPath);
        return Task.CompletedTask;
    }

    private static ApplicationPreferences Normalize(ApplicationPreferences value) =>
        value with { GridSpacingMm = double.IsFinite(value.GridSpacingMm) ? Math.Clamp(value.GridSpacingMm, .1, 1000) : 10 };
}
