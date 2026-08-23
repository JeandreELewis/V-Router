using CncCad.Cam.Toolpaths;
using CncCad.Core.Geometry;

namespace CncCad.Simulation.MachineTime;

public static class MachineTimeEstimator
{
    public static TimeSpan Estimate(ToolpathProgram program, double rapidRateMmPerMin)
    {
        var seconds = 0d;
        var current = new Point2D(0, 0);
        var z = 0d;
        foreach (var move in program.Operations.SelectMany(o => o.Moves))
        {
            if (move is RapidMove rapid) { seconds += Distance3D(current, z, rapid.End, rapid.Z) / rapidRateMmPerMin * 60; current = rapid.End; z = rapid.Z; }
            else if (move is LinearMove linear) { seconds += Distance3D(current, z, linear.End, linear.Z) / linear.FeedRate * 60; current = linear.End; z = linear.Z; }
        }
        return TimeSpan.FromSeconds(seconds);
    }
    private static double Distance3D(Point2D a, double az, Point2D b, double bz) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(az - bz, 2));
}
