using System.Text.Json;

namespace CncCad.Infrastructure.Settings;

public sealed record RecentProject(string Path, DateTimeOffset LastOpenedUtc)
{
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
}

public interface IRecentProjectStore
{
    Task<IReadOnlyList<RecentProject>> GetAsync(CancellationToken cancellationToken = default);
    Task AddAsync(string path, CancellationToken cancellationToken = default);
    Task RemoveAsync(string path, CancellationToken cancellationToken = default);
    async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        foreach (var project in await GetAsync(cancellationToken)) await RemoveAsync(project.Path, cancellationToken);
    }
}

public sealed class JsonRecentProjectStore(string settingsPath) : IRecentProjectStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public async Task<IReadOnlyList<RecentProject>> GetAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(settingsPath)) return [];
        await using var stream = File.OpenRead(settingsPath);
        return await JsonSerializer.DeserializeAsync<List<RecentProject>>(stream, Options, cancellationToken) ?? [];
    }

    public async Task AddAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var projects = (await GetAsync(cancellationToken)).Where(p => !string.Equals(p.Path, fullPath, StringComparison.OrdinalIgnoreCase)).ToList();
        projects.Insert(0, new(fullPath, DateTimeOffset.UtcNow));
        await WriteAsync(projects.Take(10), cancellationToken);
    }

    public async Task RemoveAsync(string path, CancellationToken cancellationToken = default) =>
        await WriteAsync((await GetAsync(cancellationToken)).Where(p => !string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase)), cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken = default) => WriteAsync([], cancellationToken);

    private async Task WriteAsync(IEnumerable<RecentProject> projects, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        await using var stream = File.Create(settingsPath);
        await JsonSerializer.SerializeAsync(stream, projects, Options, cancellationToken);
    }
}
