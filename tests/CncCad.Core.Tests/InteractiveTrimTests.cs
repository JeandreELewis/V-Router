using CncCad.Core.Entities;
using CncCad.Core.Geometry;

namespace CncCad.Core.Tests;

public sealed class InteractiveTrimTests
{
    private readonly InteractiveTrimService _service=new();

    [Fact]
    public void Clicked_Line_Portion_Is_Previewed_And_Cutters_Are_Untouched()
    {
        var group=Guid.NewGuid();var target=new LineEntity{Start=new(0,5),End=new(30,5),LayerId=Guid.NewGuid(),GroupId=group,StrokeColor="#FF00AA"};
        var a=new LineEntity{Start=new(10,0),End=new(10,10)};var b=new LineEntity{Start=new(20,0),End=new(20,10)};
        var candidate=_service.FindCandidate(target,[a,b],new(15,5),.001);
        Assert.True(candidate.Success,candidate.Error);Assert.Equal(new Point2D(10,5),candidate.RemovePath[0]);Assert.Equal(new Point2D(20,5),candidate.RemovePath[^1]);Assert.Equal(2,candidate.Replacements.Count);
        Assert.Contains(candidate.Replacements,x=>x.Id==target.Id);Assert.All(candidate.Replacements,x=>{Assert.Equal(group,x.GroupId);Assert.Equal("#FF00AA",x.StrokeColor);Assert.Equal(target.LayerId,x.LayerId);});Assert.Equal(new Point2D(10,0),a.Start);
    }

    [Fact]
    public void One_Crossing_Trims_An_Open_End()
    {
        var target=new LineEntity{Start=new(0,5),End=new(30,5)};var cutter=new LineEntity{Start=new(10,0),End=new(10,10)};
        var candidate=_service.FindCandidate(target,[cutter],new(3,5),.001);
        Assert.True(candidate.Success,candidate.Error);Assert.Single(candidate.Replacements);var remaining=Assert.IsType<LineEntity>(candidate.Replacements[0]);Assert.Equal(new Point2D(10,5),remaining.Start);Assert.Equal(new Point2D(30,5),remaining.End);
    }

    [Fact]
    public void Closed_Vector_Requires_Two_Crossings_And_Overlap_Is_Rejected()
    {
        var circle=new CircleEntity{Center=new(0,0),Radius=10};var one=new LineEntity{Start=new(0,0),End=new(20,0)};
        Assert.False(_service.FindCandidate(circle,[one],new(8,0),.01).Success);
        var target=new LineEntity{Start=new(0,0),End=new(20,0)};var overlap=new LineEntity{Start=new(5,0),End=new(15,0)};var invalid=_service.FindCandidate(target,[overlap],new(10,0),.01);Assert.False(invalid.Success);Assert.Contains("overlapping",invalid.Error,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Closed_Polyline_TrimsOnlyClickedEdgeAndPreservesCorners()
    {
        var target=new PolylineEntity{Points=[new(0,0),new(20,0),new(20,20),new(0,20)],IsClosed=true};
        var left=new LineEntity{Start=new(5,-5),End=new(5,5)};var right=new LineEntity{Start=new(15,-5),End=new(15,5)};
        var result=_service.FindCandidate(target,[left,right],new(10,0),.01);Assert.True(result.Success,result.Error);Assert.Equal(5,result.Replacements.Count);Assert.All(result.Replacements,x=>Assert.IsType<PolylineEntity>(x));
        var edges=result.Replacements.Cast<PolylineEntity>().Select(x=>x.Points).ToArray();Assert.Contains(edges,x=>x.SequenceEqual([new Point2D(20,0),new Point2D(20,20)]));Assert.Contains(edges,x=>x.SequenceEqual([new Point2D(0,20),new Point2D(0,0)]));
        Assert.DoesNotContain(edges,x=>x.Count>2);
    }

    [Fact]
    public void RectangleTrimUsesFreeCadStyleAtomicEdgesInsteadOfWalkingAroundCorners()
    {
        var target=new RectangleEntity{Origin=new(0,0),Width=100,Height=50};var left=new LineEntity{Start=new(25,-10),End=new(25,10)};var right=new LineEntity{Start=new(75,-10),End=new(75,10)};
        var result=_service.FindCandidate(target,[left,right],new(50,0),.001);Assert.True(result.Success,result.Error);Assert.Equal([new Point2D(25,0),new Point2D(75,0)],result.RemovePath);Assert.Equal(5,result.Replacements.Count);Assert.All(result.Replacements,x=>Assert.IsType<PolylineEntity>(x));
        var edges=result.Replacements.Cast<PolylineEntity>().Select(x=>x.Points).ToArray();Assert.Contains(edges,x=>x.SequenceEqual([new Point2D(100,0),new Point2D(100,50)]));Assert.Contains(edges,x=>x.SequenceEqual([new Point2D(100,50),new Point2D(0,50)]));Assert.Contains(edges,x=>x.SequenceEqual([new Point2D(0,50),new Point2D(0,0)]));
    }

    [Fact]
    public void OneCrossingOnRectangleTrimsOnlyTheClickedEndOfThatEdge()
    {
        var target=new RectangleEntity{Origin=new(0,0),Width=100,Height=50};var cutter=new LineEntity{Start=new(40,-10),End=new(40,10)};
        var result=_service.FindCandidate(target,[cutter],new(10,0),.001);Assert.True(result.Success,result.Error);Assert.Equal([new Point2D(0,0),new Point2D(40,0)],result.RemovePath);Assert.Equal(4,result.Replacements.Count);
        Assert.Contains(result.Replacements.Cast<PolylineEntity>(),x=>x.Points.SequenceEqual([new Point2D(40,0),new Point2D(100,0)]));
    }

    [Fact]
    public void ExactCornerHoverIsRejectedAsAmbiguous()
    {
        var target=new RectangleEntity{Origin=new(0,0),Width=100,Height=50};var cutter=new LineEntity{Start=new(40,-10),End=new(40,10)};
        var result=_service.FindCandidate(target,[cutter],new(0,0),.001);Assert.False(result.Success);Assert.Contains("corner",result.Error,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClickAwayFromTargetIsDiagnosticNoOp()
    {
        var target=new LineEntity{Start=new(0,0),End=new(100,0)};var a=new LineEntity{Start=new(25,-10),End=new(25,10)};var b=new LineEntity{Start=new(75,-10),End=new(75,10)};
        var result=_service.FindCandidate(target,[a,b],new(50,5),.1);
        Assert.False(result.Success);Assert.Contains("directly on",result.Error,StringComparison.OrdinalIgnoreCase);Assert.Empty(result.Replacements);
    }

    [Fact]
    public void TransformedRectangleStillTrimsOnlyItsHoveredWorldSpaceEdge()
    {
        var target=new RectangleEntity{Origin=new(0,0),Width=100,Height=50,Transform=Matrix2D.Then(Matrix2D.Rotation(30),Matrix2D.Translation(40,25))};var corners=EntityGeometry.WorldPoints(target);var edgeStart=corners[0];var edgeEnd=corners[1];var direction=edgeEnd-edgeStart;var normal=new Vector2D(-direction.Y,direction.X);normal=new(normal.X/normal.Length*10,normal.Y/normal.Length*10);var reverseNormal=new Vector2D(-normal.X,-normal.Y);Point2D At(double t)=>new(edgeStart.X+direction.X*t,edgeStart.Y+direction.Y*t);var a=At(.25);var b=At(.75);var left=new LineEntity{Start=a+reverseNormal,End=a+normal};var right=new LineEntity{Start=b+reverseNormal,End=b+normal};
        var result=_service.FindCandidate(target,[left,right],At(.5),.001);Assert.True(result.Success,result.Error);Assert.Equal(5,result.Replacements.Count);Assert.Equal(a.X,result.RemovePath[0].X,6);Assert.Equal(a.Y,result.RemovePath[0].Y,6);Assert.Equal(b.X,result.RemovePath[^1].X,6);Assert.Equal(b.Y,result.RemovePath[^1].Y,6);
    }

    [Fact]
    public void PeriodicCircleUsesNearestIntersectionsAroundTheClickedArc()
    {
        var target=new CircleEntity{Center=new(0,0),Radius=10};var cutter=new LineEntity{Start=new(-20,0),End=new(20,0)};
        var result=_service.FindCandidate(target,[cutter],new(0,10),.01);Assert.True(result.Success,result.Error);Assert.Single(result.Replacements);Assert.True(result.RemovePath.Count>2);Assert.All(result.RemovePath,p=>Assert.True(p.Y>=-.02));
    }

    [Fact]
    public void MultiFigure_Path_Preserves_Disconnected_Figures_Without_Joining_Ends()
    {
        var target=new VectorPathEntity{Figures=[new(){Start=new(0,0),Segments=[new LinePathSegment(new(30,0))]},new(){Start=new(0,20),Segments=[new LinePathSegment(new(30,20))]}]};
        var a=new LineEntity{Start=new(10,15),End=new(10,25)};var b=new LineEntity{Start=new(20,15),End=new(20,25)};
        var result=_service.FindCandidate(target,[a,b],new(15,20),.001);Assert.True(result.Success,result.Error);
        Assert.Equal(3,result.Replacements.Count);var untouched=Assert.IsType<PolylineEntity>(result.Replacements[0]);Assert.Equal([new Point2D(0,0),new Point2D(30,0)],untouched.Points);
        Assert.DoesNotContain(result.Replacements.OfType<PolylineEntity>().SelectMany(x=>x.Points.Zip(x.Points.Skip(1))),pair=>pair.First.Y!=pair.Second.Y);
    }
}
