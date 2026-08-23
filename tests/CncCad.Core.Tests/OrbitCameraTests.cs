using CncCad.Core.Transforms;

namespace CncCad.Core.Tests;

public sealed class OrbitCameraTests
{
    [Fact]
    public void Presets_Project_Along_Expected_Axes()
    {
        var camera = new OrbitCamera();
        camera.Fit(100, 80, 18, 5, 800, 600);

        camera.SetPreset(CameraPreset.Top);
        var origin = camera.Project(new(0, 0, 0), 800, 600);
        var alongX = camera.Project(new(10, 0, 0), 800, 600);
        var alongY = camera.Project(new(0, 10, 0), 800, 600);
        Assert.True(alongX.X > origin.X);
        Assert.True(alongY.Y < origin.Y);

        camera.SetPreset(CameraPreset.Front);
        origin = camera.Project(new(0, 0, 0), 800, 600);
        var below = camera.Project(new(0, 0, -18), 800, 600);
        Assert.True(below.Y > origin.Y);

        camera.SetPreset(CameraPreset.Right);
        var rightOrigin = camera.Project(new(0, 0, 0), 800, 600);
        var alongRightY = camera.Project(new(0, 10, 0), 800, 600);
        Assert.True(alongRightY.X < rightOrigin.X);
    }

    [Fact]
    public void Orbit_Clamps_Pitch_Wraps_Yaw_And_Marks_Custom()
    {
        var camera = new OrbitCamera();
        camera.Orbit(Math.PI * 20, Math.PI * 20);
        Assert.InRange(camera.Yaw, -Math.PI, Math.PI);
        Assert.InRange(camera.Pitch, .05, Math.PI / 2 - .01);
        Assert.Equal(CameraPreset.Custom, camera.Preset);
        camera.Orbit(0, -Math.PI * 20);
        Assert.Equal(.05, camera.Pitch, 8);
    }

    [Fact]
    public void Fit_Frames_Slab_And_Safe_Height()
    {
        var camera = new OrbitCamera();
        camera.Fit(600, 400, 18, 5, 1200, 800);
        var corners = new[]
        {
            new Point3D(0,0,-18), new Point3D(600,400,-18),
            new Point3D(0,0,5), new Point3D(600,400,5)
        }.Select(point => camera.Project(point, 1200, 800));
        Assert.All(corners, point => { Assert.InRange(point.X, 0, 1200); Assert.InRange(point.Y, 0, 800); });
    }

    [Fact]
    public void Pan_And_Zoom_Are_Bounded()
    {
        var camera = new OrbitCamera();
        var before = camera.Project(new(0, 0, 0), 800, 600);
        camera.Pan(12, -7);
        var after = camera.Project(new(0, 0, 0), 800, 600);
        Assert.Equal(12, after.X - before.X, 8);
        Assert.Equal(-7, after.Y - before.Y, 8);
        camera.ZoomBy(1e20); Assert.Equal(500, camera.Zoom);
        camera.ZoomBy(1e-20); Assert.Equal(.02, camera.Zoom);
    }
}
