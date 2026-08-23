using CncCad.Cam.Toolpaths;
using CncCad.PostProcessors.Models;

namespace CncCad.PostProcessors.GCode;

public interface IPostProcessor
{
    string Name { get; }
    string FileExtension { get; }
    string Generate(ToolpathProgram program, MachineProfile machine);
}
