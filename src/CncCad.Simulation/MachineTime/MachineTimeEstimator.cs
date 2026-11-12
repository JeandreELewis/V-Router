using CncCad.Cam.Toolpaths;

namespace CncCad.Simulation.MachineTime;

public static class MachineTimeEstimator
{
    public static TimeSpan Estimate(ToolpathProgram program,double rapidRateMmPerMin,double toolChangeSeconds=0)=>MotionEvaluator.Evaluate(program,new(rapidRateMmPerMin,toolChangeSeconds)).TotalTime;
}
