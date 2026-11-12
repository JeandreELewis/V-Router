using CncCad.Cam.Operations;
using CncCad.Cam.Nesting;
using CncCad.Core.Documents;
using CncCad.Core.Preview;

namespace CncCad.IO.Projects;

public sealed class CncProject
{
    public const int CurrentVersion = 8;
    public int Version { get; set; } = CurrentVersion;
    public CadDocument Document { get; set; } = new();
    public CamJob CamJob { get; set; } = new();
    public NestingJobDefinition Nesting { get; set; } = new();
    public ProjectPreviewSettings PreviewSettings { get; set; } = ProjectPreviewSettings.LegacyDefault;
}
