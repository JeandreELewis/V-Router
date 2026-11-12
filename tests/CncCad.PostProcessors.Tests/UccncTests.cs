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
            Assert.Contains("G21", code); Assert.Contains("G90", code); Assert.Contains("G94", code); Assert.Contains("G40", code); Assert.Contains("G49", code); Assert.Contains("G80", code); Assert.Contains("M3 S18000", code); Assert.Contains("X10.500 Y2.250 Z5.000", code); Assert.DoesNotContain("10,500", code); Assert.EndsWith("M30\n", code);
            Assert.Contains("G0 X0.000 Y0.000",code);
            Assert.True(code.LastIndexOf("M5", StringComparison.Ordinal) < code.LastIndexOf("G0 X0.000 Y0.000", StringComparison.Ordinal));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
    [Fact]
    public void WorkDatum_Offsets_Xy_And_BedReferenced_Z()
    {
        var operation=new ToolpathOperation{Name="Datum",Moves=[new RapidMove(new(310,195),5),new LinearMove(new(320,205),-18,1000)]};var machine=new MachineProfile{SafeZMm=5,WorkZeroDocumentXmm=310,WorkZeroDocumentYmm=195,WorkZeroZOffsetMm=18};
        var code=new UccncPostProcessor().Generate(new ToolpathProgram{Operations=[operation]},machine);Assert.Contains("G0 X0.000 Y0.000 Z23.000",code);Assert.Contains("G1 X10.000 Y10.000 Z0.000",code);
    }
    [Theory]
    [InlineData(true, "G2")]
    [InlineData(false, "G3")]
    public void ArcOffsetsUsePreviousEndpointAcrossConsecutiveArcs(bool clockwise, string command)
    {
        var operation = new ToolpathOperation { Moves = [
            new RapidMove(new(110, 200), 5), new LinearMove(new(110, 200), -1, 200),
            new ArcMove(new(100, 210), new(100, 200), -1, 800, clockwise),
            new ArcMove(new(90, 200), new(100, 200), -1, 800, clockwise)] };
        var code = new UccncPostProcessor().Generate(new ToolpathProgram { Operations = [operation] },
            new MachineProfile { WorkZeroDocumentXmm = 100, WorkZeroDocumentYmm = 200 });
        Assert.Contains("G17", code);
        Assert.Contains("G91.1", code);
        Assert.Contains($"{command} X0.000 Y10.000 Z-1.000 I-10.000 J0.000", code);
        Assert.Contains($"{command} X-10.000 Y0.000 Z-1.000 I0.000 J-10.000", code);
    }

    [Fact]
    public void SanitizesCommentsAndRejectsMalformedMotion()
    {
        var valid = new ToolpathOperation { Name = "Cut ) M30 (", Moves = [new RapidMove(new(0, 0), 5)] };
        var code = new UccncPostProcessor().Generate(new ToolpathProgram { Name = "Job ) M30 (", Operations = [valid] }, new());
        Assert.Contains("(Cut ] M30 [)", code);
        Assert.DoesNotContain("(Cut ) M30 ()", code);

        var unpositionedArc = new ToolpathOperation { Moves = [new ArcMove(new(1, 1), new(0, 1), -1, 100, true)] };
        Assert.Throws<InvalidDataException>(() => new UccncPostProcessor().Generate(new ToolpathProgram { Operations = [unpositionedArc] }, new()));
        var badFeed = new ToolpathOperation { Moves = [new LinearMove(new(1, 1), -1, 0)] };
        Assert.Throws<InvalidDataException>(() => new UccncPostProcessor().Generate(new ToolpathProgram { Operations = [badFeed] }, new()));
    }
    [Fact]
    public void PocketAndDrill_MatchGoldenUccncProgram()
    {
        var pocket=new ToolpathOperation{Name="Pocket",Moves=[new ToolChange(1,"6mm End Mill"),new SpindleStart(18000),new RapidMove(new(10,10),5),new LinearMove(new(10,10),-2,300),new LinearMove(new(40,10),-2,1200),new SpindleStop()]};var drill=new ToolpathOperation{Name="Drill",Moves=[new ToolChange(2,"5mm Drill"),new SpindleStart(12000),new RapidMove(new(25,25),1),new LinearMove(new(25,25),-6,200),new DwellMove(.25),new RapidMove(new(25,25),5),new SpindleStop()]};var actual=new UccncPostProcessor().Generate(new(){Name="Golden",Operations=[pocket,drill]},new(){SafeZMm=5});var expected=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","uccnc-pocket-drill.nc"));Assert.Equal(expected.Replace("\r\n","\n"),actual.Replace("\r\n","\n"));
    }
}
