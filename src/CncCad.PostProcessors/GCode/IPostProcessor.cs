using System.Text.Json;
using System.Text.Json.Serialization;
using CncCad.Cam.Safety;
using CncCad.Cam.Toolpaths;
using CncCad.Core.Documents;
using CncCad.PostProcessors.Models;

namespace CncCad.PostProcessors.GCode;

[Flags]
public enum PostCapability { None=0, Arcs=1, Dwell=2, ManualToolChange=4, AutomaticToolChange=8 }
public enum PostArcMode { IncrementalIj, AbsoluteIj, Unsupported }
public enum PostCommentStyle { Parentheses, Semicolon }
public enum PostToolChangeBehavior { Unsupported, ManualPause, AutomaticM6 }
public enum PostCertificationStatus { Uncertified, CertificationPending, Certified }
public enum PostExportScope { Combined, SeparateByTool }
public enum PostDiagnosticSeverity { Warning, Error }

public sealed record PostDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required ControllerFamily ControllerFamily { get; init; }
    public string Extension { get; init; } = ".nc";
    public int Precision { get; init; } = 3;
    public bool EmitUnitCode { get; init; } = true;
    public PostArcMode ArcMode { get; init; } = PostArcMode.IncrementalIj;
    public PostCommentStyle CommentStyle { get; init; } = PostCommentStyle.Parentheses;
    public PostToolChangeBehavior ToolChangeBehavior { get; init; } = PostToolChangeBehavior.AutomaticM6;
    public PostCapability Capabilities { get; init; } = PostCapability.Arcs|PostCapability.Dwell|PostCapability.AutomaticToolChange;
    public IReadOnlyList<string> HeaderLines { get; init; } = ["G90","G17","G94","G40","G49","G80"];
    public IReadOnlyList<string> FooterLines { get; init; } = ["M30"];
    public bool IsBuiltIn { get; init; }
    public PostCertificationStatus CertificationStatus { get; init; } = PostCertificationStatus.Uncertified;
}

public sealed record PostProcessRequest(ToolpathProgram Program,MachineProfile MachineSnapshot,MeasurementUnit OutputUnit,PostDefinition PostDefinition,PostExportScope ExportScope=PostExportScope.Combined);
public sealed record GeneratedPostFile(string FileName,string Extension,string Content,int? ToolNumber=null);
public sealed record PostProcessDiagnostic(PostDiagnosticSeverity Severity,string Code,string Message);
public sealed record PostProcessResult(IReadOnlyList<GeneratedPostFile> Files,IReadOnlyList<PostProcessDiagnostic> Diagnostics)
{
    public bool Success=>Files.Count>0&&Diagnostics.All(x=>x.Severity!=PostDiagnosticSeverity.Error);
    public IReadOnlyList<PostProcessDiagnostic> Errors=>Diagnostics.Where(x=>x.Severity==PostDiagnosticSeverity.Error).ToArray();
    public IReadOnlyList<PostProcessDiagnostic> Warnings=>Diagnostics.Where(x=>x.Severity==PostDiagnosticSeverity.Warning).ToArray();
}

public interface IPostProcessor
{
    PostDefinition Definition { get; }
    PostProcessResult Process(PostProcessRequest request);
}

public sealed record PostProcessorDescriptor(string Id,string DisplayName,IReadOnlyList<string> SupportedExtensions,ControllerFamily ControllerFamily,PostCertificationStatus CertificationStatus,bool IsBuiltIn,PostCapability Capabilities);

public interface IPostProcessorRegistry
{
    IReadOnlyList<PostProcessorDescriptor> Descriptors { get; }
    IPostProcessor GetRequired(string id);
}

public sealed class PostProcessorRegistry : IPostProcessorRegistry
{
    private readonly IReadOnlyDictionary<string,IPostProcessor> processors;
    public PostProcessorRegistry(IEnumerable<IPostProcessor> processors)
    {
        this.processors=processors.ToDictionary(processor=>processor.Definition.Id,StringComparer.OrdinalIgnoreCase);
        if(this.processors.Count==0)throw new ArgumentException("At least one postprocessor must be registered.",nameof(processors));
    }
    public IReadOnlyList<PostProcessorDescriptor> Descriptors=>processors.Values.Select(processor=>new PostProcessorDescriptor(processor.Definition.Id,processor.Definition.DisplayName,[processor.Definition.Extension],processor.Definition.ControllerFamily,processor.Definition.CertificationStatus,processor.Definition.IsBuiltIn,processor.Definition.Capabilities)).ToArray();
    public IPostProcessor GetRequired(string id)=>processors.TryGetValue(NormalizeLegacyId(id),out var processor)?processor:throw new KeyNotFoundException($"Postprocessor '{id}' is not installed.");
    private static string NormalizeLegacyId(string id)=>id.Equals("uccnc-mm",StringComparison.OrdinalIgnoreCase)?"uccnc":id;
}

public static class CommunityPostDefinitionLoader
{
    private static readonly JsonSerializerOptions Options=new(){PropertyNameCaseInsensitive=true,Converters={new JsonStringEnumConverter()}};
    public sealed record LoadResult(IReadOnlyList<PostDefinition> Definitions,IReadOnlyList<string> Errors);

    public static IReadOnlyList<PostDefinition> Load(string directory)
    {
        var result=LoadWithDiagnostics(directory);
        if(result.Errors.Count>0)throw new InvalidDataException(string.Join(Environment.NewLine,result.Errors));
        return result.Definitions;
    }

    public static LoadResult LoadWithDiagnostics(string directory)
    {
        if(!Directory.Exists(directory))return new([],[]);
        var posts=new List<PostDefinition>();
        var errors=new List<string>();
        foreach(var file in Directory.EnumerateFiles(directory,"*.json",SearchOption.TopDirectoryOnly).Order())
        {
            try
            {
                var definition=JsonSerializer.Deserialize<PostDefinition>(File.ReadAllText(file),Options)??throw new InvalidDataException($"Community post '{file}' is empty.");
                Validate(definition,file);
                posts.Add(definition with{IsBuiltIn=false,CertificationStatus=PostCertificationStatus.Uncertified,ControllerFamily=ControllerFamily.Community});
            }
            catch(Exception exception) when(exception is JsonException or IOException or InvalidDataException)
            {
                errors.Add($"{Path.GetFileName(file)}: {exception.Message}");
            }
        }
        return new(posts,errors);
    }
    private static void Validate(PostDefinition post,string source)
    {
        if(string.IsNullOrWhiteSpace(post.Id)||!post.Id.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_'))throw new InvalidDataException($"Community post '{source}' has an invalid ID.");
        if(string.IsNullOrWhiteSpace(post.DisplayName)||post.Precision is <0 or >6||!post.Extension.StartsWith('.')||post.HeaderLines.Concat(post.FooterLines).Any(x=>x.Length>120||x.Contains('\n')||x.Contains('\r')))throw new InvalidDataException($"Community post '{source}' does not match the declarative post schema.");
    }
}
