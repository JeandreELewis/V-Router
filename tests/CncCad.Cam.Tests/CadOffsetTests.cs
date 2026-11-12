using CncCad.Cam.Profile;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;

namespace CncCad.Cam.Tests;

public sealed class CadOffsetTests
{
    [Fact]
    public void Closed_Offset_Creates_New_Contour_And_Preserves_Source()
    {
        var source=new RectangleEntity{Width=100,Height=100};var service=new CadOffsetService();var result=service.Offset([source],3,CadOffsetJoin.Miter);
        Assert.True(result.Success);var output=Assert.IsType<PolylineEntity>(Assert.Single(result.Entities));Assert.True(output.IsClosed);Assert.Equal(new BoundingBox(-3,-3,103,103),output.GetBounds());Assert.Equal(new BoundingBox(0,0,100,100),source.GetBounds());
    }

    [Fact]
    public void OpenLineOffsetUsesTheSignToChooseLeftOrRight()
    {
        var source=new LineEntity{Start=new(0,0),End=new(10,0)};var service=new CadOffsetService();var left=service.Offset([source],2,CadOffsetJoin.Round);var right=service.Offset([source],-2,CadOffsetJoin.Round);
        Assert.True(left.Success);Assert.Equal([new Point2D(0,2),new Point2D(10,2)],Assert.IsType<PolylineEntity>(Assert.Single(left.Entities)).Points);Assert.Equal([new Point2D(0,-2),new Point2D(10,-2)],Assert.IsType<PolylineEntity>(Assert.Single(right.Entities)).Points);
    }

    [Fact]
    public void OpenOffsetAppliesMiterBevelAndRoundJoinsDeterministically()
    {
        var inner=new PolylineEntity{Points=[new(0,0),new(10,0),new(10,10)]};var service=new CadOffsetService();var miter=Assert.IsType<PolylineEntity>(Assert.Single(service.Offset([inner],2,CadOffsetJoin.Miter).Entities));Assert.Equal([new Point2D(0,2),new Point2D(8,2),new Point2D(8,10)],miter.Points);
        var outer=new PolylineEntity{Points=[new(0,0),new(10,0),new(10,-10)]};var bevel=Assert.IsType<PolylineEntity>(Assert.Single(service.Offset([outer],2,CadOffsetJoin.Bevel).Entities));Assert.Equal([new Point2D(0,2),new Point2D(10,2),new Point2D(12,0),new Point2D(12,-10)],bevel.Points);
        var round=Assert.IsType<PolylineEntity>(Assert.Single(service.Offset([outer],2,CadOffsetJoin.Round).Entities));Assert.True(round.Points.Count>4);Assert.All(round.Points.Skip(1).SkipLast(1),point=>Assert.Equal(2,point.DistanceTo(new(10,0)),6));
    }

    [Fact]
    public void OpenOffsetPreservesMetadataAndRejectsDegenerateReversalsAndNonFiniteDistance()
    {
        var layer=Guid.NewGuid();var sheet=Guid.NewGuid();var group=Guid.NewGuid();var source=new PolylineEntity{LayerId=layer,MaterialSheetId=sheet,GroupId=group,StrokeColor="#123456",Transform=Matrix2D.Translation(20,30),Points=[new(0,0),new(10,0)]};var service=new CadOffsetService();var result=service.Offset([source],3,CadOffsetJoin.Miter);var offset=Assert.IsType<PolylineEntity>(Assert.Single(result.Entities));Assert.Equal(layer,offset.LayerId);Assert.Equal(sheet,offset.MaterialSheetId);Assert.Equal(group,offset.GroupId);Assert.Equal("#123456",offset.StrokeColor);Assert.Equal([new Point2D(20,33),new Point2D(30,33)],offset.Points);
        Assert.False(service.Offset([new PolylineEntity{Points=[new(0,0),new(10,0),new(0,0)]}],2,CadOffsetJoin.Miter).Success);Assert.False(service.Offset([source],double.NaN,CadOffsetJoin.Miter).Success);
    }

    [Fact]
    public void OpenOffsetRemovesSelfIntersectionLoopsDeterministically()
    {
        var source=new PolylineEntity{Points=[new(0,0),new(20,20),new(0,20),new(20,0)]};var service=new CadOffsetService();var first=Assert.IsType<PolylineEntity>(Assert.Single(service.Offset([source],1.5,CadOffsetJoin.Round).Entities));var second=Assert.IsType<PolylineEntity>(Assert.Single(service.Offset([source],1.5,CadOffsetJoin.Round).Entities));Assert.Equal(first.Points,second.Points);Assert.False(HasStrictIntersection(first.Points));Assert.All(first.Points,point=>{Assert.True(double.IsFinite(point.X));Assert.True(double.IsFinite(point.Y));});
    }

    private static bool HasStrictIntersection(IReadOnlyList<Point2D> points)
    {
        static double Cross(Vector2D a,Vector2D b)=>a.X*b.Y-a.Y*b.X;for(var first=0;first<points.Count-3;first++)for(var second=first+2;second<points.Count-1;second++){var a=points[first];var b=points[first+1];var c=points[second];var d=points[second+1];var r=b-a;var s=d-c;var denominator=Cross(r,s);if(Math.Abs(denominator)<1e-9)continue;var t=Cross(c-a,s)/denominator;var u=Cross(c-a,r)/denominator;if(t>1e-8&&t<1-1e-8&&u>1e-8&&u<1-1e-8)return true;}return false;
    }
}
