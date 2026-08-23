using CncCad.Cam.Operations;
using CncCad.Cam.Nesting;
using CncCad.Core.Documents;

namespace CncCad.IO.Projects;

public sealed class CncProject
{
    public const int CurrentVersion = 7;
    public int Version { get; set; } = CurrentVersion;
    public CadDocument Document { get; set; } = new();
    public CamJob CamJob { get; set; } = new();
    public NestingJobDefinition Nesting { get; set; } = new();
}
