using System.Text.Json;

namespace CncCad.IO.Projects;

public interface IProjectSerializer
{
    Task SaveAsync(CncProject project, Stream destination, CancellationToken cancellationToken = default);
    Task<CncProject> LoadAsync(Stream source, CancellationToken cancellationToken = default);
}

public sealed class JsonProjectSerializer : IProjectSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.General) { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        return options;
    }
    public Task SaveAsync(CncProject project, Stream destination, CancellationToken cancellationToken = default) => JsonSerializer.SerializeAsync(destination, project, Options, cancellationToken);
    public async Task<CncProject> LoadAsync(Stream source, CancellationToken cancellationToken = default)
    {
        using var json = await JsonDocument.ParseAsync(source, cancellationToken: cancellationToken);
        if (!json.RootElement.TryGetProperty("version", out var versionElement)) throw new InvalidDataException("Project version is missing.");
        var version = versionElement.GetInt32();
        if (version > CncProject.CurrentVersion) throw new NotSupportedException($"Project version {version} is newer than supported version {CncProject.CurrentVersion}.");
        if (version <= 1)
        {
            var document = json.RootElement.Deserialize<CncCad.Core.Documents.CadDocument>(Options) ?? throw new InvalidDataException("Project document is empty.");
            return new CncProject { Version = CncProject.CurrentVersion, Document = document, CamJob = new() { SafetySetupIncomplete = true } };
        }
        var project = json.RootElement.Deserialize<CncProject>(Options) ?? throw new InvalidDataException("Project is empty.");
        project.Version = CncProject.CurrentVersion;
        if (version <= 3)
        {
            project.Version = CncProject.CurrentVersion;
            project.CamJob.SafetySetupIncomplete = true;
        }
        return project;
    }
}
