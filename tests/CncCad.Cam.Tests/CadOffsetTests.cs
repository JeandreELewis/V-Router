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
    public void Open_Offset_Uses_Signed_Left_Right_Distance()
    {
        var source=new LineEntity{Start=new(0,0),End=new(10,0)};var service=new CadOffsetService();var left=Assert.IsType<PolylineEntity>(Assert.Single(service.Offset([source],2,CadOffsetJoin.Round).Entities));var right=Assert.IsType<PolylineEntity>(Assert.Single(service.Offset([source],-2,CadOffsetJoin.Round).Entities));
        Assert.Equal(2,left.Points[0].Y);Assert.Equal(-2,right.Points[0].Y);
    }
}
