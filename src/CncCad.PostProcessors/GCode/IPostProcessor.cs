using CncCad.Cam.Toolpaths;
using CncCad.PostProcessors.Models;

namespace CncCad.PostProcessors.GCode;

public interface IPostProcessor
{
    string Id { get; }
    string Name { get; }
    string FileExtension { get; }
    string Generate(ToolpathProgram program, MachineProfile machine);
}

public sealed record PostProcessorDescriptor(string Id,string DisplayName,IReadOnlyList<string> SupportedExtensions,IReadOnlyList<string> CompatibleMachineKinds,IReadOnlyDictionary<string,string> Options);

public interface IPostProcessorRegistry
{
    IReadOnlyList<PostProcessorDescriptor> Descriptors { get; }
    IPostProcessor GetRequired(string id);
}

public sealed class PostProcessorRegistry : IPostProcessorRegistry
{
    private readonly IReadOnlyDictionary<string,IPostProcessor> _processors;
    public PostProcessorRegistry(IEnumerable<IPostProcessor> processors)
    {
        _processors=processors.ToDictionary(processor=>processor.Id,StringComparer.OrdinalIgnoreCase);
        if(_processors.Count==0)throw new ArgumentException("At least one postprocessor must be registered.",nameof(processors));
    }
    public IReadOnlyList<PostProcessorDescriptor> Descriptors=>_processors.Values.Select(processor=>new PostProcessorDescriptor(processor.Id,processor.Name,[processor.FileExtension],["3-axis-router"],new Dictionary<string,string>())).ToArray();
    public IPostProcessor GetRequired(string id)=>_processors.TryGetValue(id,out var processor)?processor:throw new KeyNotFoundException($"Postprocessor '{id}' is not installed.");
}
