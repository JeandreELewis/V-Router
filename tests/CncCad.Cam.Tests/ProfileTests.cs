using CncCad.Cam.Profile;
using CncCad.Cam.Tools;
using CncCad.Cam.Toolpaths;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;

namespace CncCad.Cam.Tests;

public class ProfileTests
{
    private readonly CncTool _tool = new() { Name = "6mm End Mill", DiameterMm = 6, StepDownMm = 3 };
    [Fact] public void OutsideProfile_OffsetsByCutterRadius()
    {
        var op = new ProfileToolpathGenerator().Generate(new RectangleEntity { Origin = new(10, 10), Width = 100, Height = 100 }, _tool, new(0, 3, 3, 5, 1800, 600, 18000));
        var rapid = Assert.IsType<RapidMove>(op.Moves[2]); Assert.Equal(new Point2D(7, 7), rapid.End);
        var cutting = op.Moves.OfType<LinearMove>().Select(m => m.End).ToArray(); Assert.Contains(new Point2D(113, 113), cutting);
    }
    [Fact] public void EighteenMillimetres_AtThreePerPass_CreatesSixPasses()
    {
        var op = new ProfileToolpathGenerator().Generate(new RectangleEntity { Width = 200, Height = 100 }, _tool, new(0, 18, 3, 5, 1800, 600, 18000));
        Assert.Equal(6, op.Moves.OfType<LinearMove>().Count(m => m.End == new Point2D(-3, -3) && m.FeedRate == 600));
        Assert.Equal(-18, op.Moves.OfType<LinearMove>().Min(m => m.Z));
    }
}
