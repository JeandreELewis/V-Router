using CncCad.Core.Documents;
using CncCad.Core.Geometry;
using CncCad.Core.Transforms;

namespace CncCad.Core.Tests;

public class GeometryTests
{
    [Fact] public void Distance_UsesEuclideanGeometry() => Assert.Equal(5, new Point2D(0, 0).DistanceTo(new(3, 4)));
    [Fact] public void Bounds_ContainAllPoints() => Assert.Equal(new BoundingBox(-2, 1, 8, 9), BoundingBox.FromPoints([new(-2, 9), new(8, 1)]));
    [Fact] public void ProductionTolerancePolicyIsDimensionedAndInternallyConsistent()
    {
        var policy=GeometryTolerancePolicy.Production;Assert.Empty(policy.Validate());Assert.Equal(GeometryValidation.CoordinateToleranceMm,policy.EndpointEqualityMm);Assert.True(policy.CurveFlatnessMm>policy.InteractiveMinimumLengthMm);Assert.True(policy.ImportApproximationMm>=policy.CurveFlatnessMm);Assert.True(policy.EndpointsEqual(new(0,0),new(policy.EndpointEqualityMm,0)));Assert.False(policy.EndpointsEqual(new(0,0),new(policy.EndpointEqualityMm*2,0)));
    }
    [Fact] public void InvalidTolerancePolicyIsRejectedBeforeUse()
    {
        var policy=GeometryTolerancePolicy.Production with{EndpointEqualityMm=double.NaN,MinimumLengthMm=-1,BooleanDecimalPrecision=9};var errors=policy.Validate();Assert.Contains(errors,error=>error.Contains(nameof(policy.EndpointEqualityMm)));Assert.Contains(errors,error=>error.Contains(nameof(policy.MinimumLengthMm)));Assert.Contains(errors,error=>error.Contains(nameof(policy.BooleanDecimalPrecision)));
    }
    [Fact] public void Rotation_RotatesNinetyDegrees() { var p = Matrix2D.Rotation(90).Transform(new(10, 0)); Assert.Equal(0, p.X, 8); Assert.Equal(10, p.Y, 8); }
    [Fact] public void Viewport_RoundTrip_PreservesDocumentPoint() { var transform = new ViewportTransform(); var point = new Point2D(20, 30); var result = transform.ScreenToDocument(transform.DocumentToScreen(point, 800), 800); Assert.Equal(point.X, result.X, 8); Assert.Equal(point.Y, result.Y, 8); }
    [Fact] public void CentreOrigin_MapsDocumentCentreToMachineZero() => Assert.Equal(new Point2D(0, 0), MachineCoordinateTransform.DocumentToMachine(new(300, 200), new JobSettings { WidthMm = 600, HeightMm = 400, Origin = XyOrigin.Centre }));
    [Theory]
    [InlineData(XyOrigin.BottomCentre,300,0)]
    [InlineData(XyOrigin.CentreLeft,0,200)]
    [InlineData(XyOrigin.CentreRight,600,200)]
    [InlineData(XyOrigin.TopCentre,300,400)]
    public void NinePointOrigin_MapsSelectedDatumToMachineZero(XyOrigin origin,double x,double y)
    {
        var job=new JobSettings{WidthMm=600,HeightMm=400,Origin=origin};Assert.Equal(new Point2D(0,0),MachineCoordinateTransform.DocumentToMachine(new(x,y),job));
    }
    [Fact] public void DatumOffsets_AndBedZ_AreApplied()
    {
        var job=new JobSettings{WidthMm=600,HeightMm=400,ThicknessMm=18,Origin=XyOrigin.Centre,OriginOffsetXmm=10,OriginOffsetYmm=-5,ZZero=ZZeroPosition.MachineBed};
        Assert.Equal(new Point2D(0,0),MachineCoordinateTransform.DocumentToMachine(new(310,195),job));Assert.Equal(0,MachineCoordinateTransform.DocumentToMachineZ(-18,job));Assert.Equal(23,MachineCoordinateTransform.DocumentToMachineZ(5,job));
    }
}
