using CncCad.App.ViewModels;
using CncCad.Core.Commands;
using CncCad.Core.Documents;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;
using CncCad.Core.Selection;

namespace CncCad.Core.Tests;

public sealed class PrecisionCadTests
{
    [Fact]
    public void Matrix_Composition_And_Inverse_RoundTrip()
    {
        var transform=Matrix2D.Then(Matrix2D.Rotation(35),Matrix2D.Translation(12,-4));var point=new Point2D(8,3);
        Assert.True(point.DistanceTo(transform.Inverse().Transform(transform.Transform(point)))<1e-9);
    }

    [Fact]
    public void Transform_Command_Is_Undoable_And_HitTesting_Uses_World_Geometry()
    {
        var document=new CadDocument();var line=new LineEntity{Start=new(0,0),End=new(10,0)};document.Entities.Add(line);var history=new CommandHistory();
        history.Execute(new TransformEntitiesCommand(document,[line.Id],Matrix2D.Around(new(0,0),Matrix2D.Rotation(90)),"Rotate"));
        Assert.NotNull(EntityHitTester.HitTest(document.Entities,new(0,5),.1));Assert.Null(EntityHitTester.HitTest(document.Entities,new(5,0),.1));
        history.Undo();Assert.NotNull(EntityHitTester.HitTest(document.Entities,new(5,0),.1));
    }

    [Fact]
    public void Node_Commands_Preserve_Entity_Id_And_Undo_Topology()
    {
        var polyline=new PolylineEntity{Points=[new(0,0),new(10,0),new(10,10)],IsClosed=true};var id=polyline.Id;var history=new CommandHistory();
        history.Execute(new SetNodeCommand(polyline,1,new(12,0)));Assert.Equal(new Point2D(12,0),polyline.Points[1]);history.Undo();Assert.Equal(new Point2D(10,0),polyline.Points[1]);
        history.Execute(new PolylineNodesCommand(polyline,[new(0,0),new(5,0),new(10,0),new(10,10)],true,"Insert"));Assert.Equal(4,polyline.Points.Count);history.Undo();Assert.Equal(3,polyline.Points.Count);Assert.Equal(id,polyline.Id);
    }

    [Fact]
    public void Affine_Circle_Bounds_Are_Analytically_Exact()
    {
        var circle=new CircleEntity{Center=new(2,3),Radius=5,Transform=Matrix2D.Then(Matrix2D.Scale(2,.5),Matrix2D.Rotation(30))};var bounds=circle.GetBounds();var center=circle.Transform.Transform(circle.Center);
        var expectedX=Math.Sqrt(Math.Pow(5*circle.Transform.M11,2)+Math.Pow(5*circle.Transform.M21,2));var expectedY=Math.Sqrt(Math.Pow(5*circle.Transform.M12,2)+Math.Pow(5*circle.Transform.M22,2));
        Assert.Equal(center.X-expectedX,bounds.MinX,9);Assert.Equal(center.Y+expectedY,bounds.MaxY,9);
    }

    [Theory]
    [InlineData("part.nc",".txt","part.txt")]
    [InlineData("part",".tap","part.tap")]
    public void Export_Format_Normalizes_Extension(string input,string extension,string expected)
    {
        var format=new GCodeExportFormat("Test",extension);Assert.Equal(expected,format.NormalizePath(input));
    }
}
