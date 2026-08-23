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
        Assert.All(candidate.Replacements,x=>{Assert.Equal(group,x.GroupId);Assert.Equal("#FF00AA",x.StrokeColor);Assert.Equal(target.LayerId,x.LayerId);});Assert.Equal(new Point2D(10,0),a.Start);
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
    public void Closed_Polyline_Trims_Between_Crossings()
    {
        var target=new PolylineEntity{Points=[new(0,0),new(20,0),new(20,20),new(0,20)],IsClosed=true};
        var left=new LineEntity{Start=new(5,-5),End=new(5,5)};var right=new LineEntity{Start=new(15,-5),End=new(15,5)};
        var result=_service.FindCandidate(target,[left,right],new(10,0),.01);Assert.True(result.Success,result.Error);Assert.Single(result.Replacements);Assert.False(Assert.IsType<PolylineEntity>(result.Replacements[0]).IsClosed);
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
