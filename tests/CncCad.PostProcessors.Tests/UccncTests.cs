using System.Globalization;
using CncCad.Cam.Toolpaths;
using CncCad.Core.Geometry;
using CncCad.PostProcessors.Models;
using CncCad.PostProcessors.Uccnc;

namespace CncCad.PostProcessors.Tests;

public class UccncTests
{
    [Fact] public void GeneratesInvariantSafeTerminatedProgram()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new("de-DE");
            var operation = new ToolpathOperation { Name = "Test", Moves = [new SpindleStart(18000), new RapidMove(new(10.5, 2.25), 5), new LinearMove(new Point2D(100, 2.25), -3, 1800), new SpindleStop()] };
            var code = new UccncPostProcessor().Generate(new ToolpathProgram { Operations = [operation] }, new MachineProfile { SafeZMm = 5 });
            Assert.Contains("G21", code); Assert.Contains("G90", code); Assert.Contains("M3 S18000", code); Assert.Contains("X10.500 Y2.250 Z5.000", code); Assert.DoesNotContain("10,500", code); Assert.EndsWith("M30\n", code);
            Assert.Contains("G0 X0.000 Y0.000",code);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
    [Fact]
    public void WorkDatum_Offsets_Xy_And_BedReferenced_Z()
    {
        var operation=new ToolpathOperation{Name="Datum",Moves=[new RapidMove(new(310,195),5),new LinearMove(new(320,205),-18,1000)]};var machine=new MachineProfile{SafeZMm=5,WorkZeroDocumentXmm=310,WorkZeroDocumentYmm=195,WorkZeroZOffsetMm=18};
        var code=new UccncPostProcessor().Generate(new ToolpathProgram{Operations=[operation]},machine);Assert.Contains("G0 X0.000 Y0.000 Z23.000",code);Assert.Contains("G1 X10.000 Y10.000 Z0.000",code);
    }
}
