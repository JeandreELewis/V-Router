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
    public Task SaveAsync(CncProject project, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(destination);
        Validate(project);
        project.Version = CncProject.CurrentVersion;
        return JsonSerializer.SerializeAsync(destination, project, Options, cancellationToken);
    }
    public async Task<CncProject> LoadAsync(Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var json = await JsonDocument.ParseAsync(source, cancellationToken: cancellationToken);
        if (!json.RootElement.TryGetProperty("version", out var versionElement)) throw new InvalidDataException("Project version is missing.");
        var version = versionElement.GetInt32();
        if (version < 1) throw new InvalidDataException($"Project version {version} is invalid.");
        if (version > CncProject.CurrentVersion) throw new NotSupportedException($"Project version {version} is newer than supported version {CncProject.CurrentVersion}.");
        if (version <= 1)
        {
            var document = json.RootElement.Deserialize<CncCad.Core.Documents.CadDocument>(Options) ?? throw new InvalidDataException("Project document is empty.");
            var legacy = new CncProject { Version = CncProject.CurrentVersion, Document = document, CamJob = new() { SafetySetupIncomplete = true } };
            Validate(legacy);
            return legacy;
        }
        var project = json.RootElement.Deserialize<CncProject>(Options) ?? throw new InvalidDataException("Project is empty.");
        Validate(project);
        project.Version = CncProject.CurrentVersion;
        if (version <= 3)
        {
            project.Version = CncProject.CurrentVersion;
            project.CamJob.SafetySetupIncomplete = true;
        }
        return project;
    }

    private static void Validate(CncProject project)
    {
        if (project.Document is null) throw new InvalidDataException("Project document is missing.");
        if (project.Document.Job is null) throw new InvalidDataException("Project job settings are missing.");
        if (project.Document.Layers is null || project.Document.Layers.Count == 0) throw new InvalidDataException("Project must contain at least one layer.");
        if (project.Document.Entities is null) throw new InvalidDataException("Project entity collection is missing.");
        if (project.CamJob is null || project.CamJob.Operations is null || project.CamJob.Fixtures is null) throw new InvalidDataException("Project CAM data is incomplete.");
        if (project.Nesting is null || project.Nesting.Parts is null) throw new InvalidDataException("Project nesting data is incomplete.");
        project.PreviewSettings = (project.PreviewSettings ?? CncCad.Core.Preview.ProjectPreviewSettings.LegacyDefault).Normalize();
    }
}
