using CncCad.Cam.Toolpaths;
using CncCad.Core.Geometry;

namespace CncCad.Simulation.Playback;

public enum PlaybackState { Empty, Paused, Playing, Completed }
public enum PlaybackMoveKind { Rapid, Cutting, Dwell }
public sealed record PlaybackProgramItem(ToolpathOperation Operation, double ToolDiameterMm);
public sealed record PlaybackSegment(Guid OperationId, string OperationName, PlaybackMoveKind Kind, Point2D Start, double StartZ, Point2D End, double EndZ, TimeSpan StartTime, TimeSpan Duration, double ToolDiameterMm, Point2D? ArcCenter = null, bool Clockwise = false, int MoveIndex = -1)
{
    public TimeSpan EndTime => StartTime + Duration;
    public Point2D PointAt(double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        if (ArcCenter is not { } center) return new(Start.X + (End.X - Start.X) * progress, Start.Y + (End.Y - Start.Y) * progress);
        var radius = Start.DistanceTo(center); var startAngle = Math.Atan2(Start.Y - center.Y, Start.X - center.X); var endAngle = Math.Atan2(End.Y - center.Y, End.X - center.X);
        var sweep = endAngle - startAngle;
        if (Clockwise && sweep >= 0) sweep -= Math.PI * 2; else if (!Clockwise && sweep <= 0) sweep += Math.PI * 2;
        var angle = startAngle + sweep * progress; return new(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
    }
    public double ZAt(double progress) => StartZ + (EndZ - StartZ) * Math.Clamp(progress, 0, 1);
}
public sealed record PlaybackFrame(PlaybackState State, TimeSpan Elapsed, TimeSpan Total, double Progress, Point2D Position, double Z, Guid? ActiveOperationId, string? ActiveOperationName, double ToolDiameterMm, IReadOnlyList<PlaybackSegment> Segments, int CompletedSegmentCount, int ActiveSegmentIndex, double ActiveSegmentProgress)
{
    public static PlaybackFrame Empty { get; } = new(PlaybackState.Empty, TimeSpan.Zero, TimeSpan.Zero, 0, new(0,0), 0, null, null, 0, [], 0, -1, 0);
}

public sealed class ToolpathPlaybackSession
{
    public static IReadOnlyList<double> SupportedSpeeds { get; } = [0.25, 0.5, 1, 2, 5, 10, 20];
    private readonly List<PlaybackSegment> _segments = [];
    private TimeSpan _elapsed;
    public PlaybackState State { get; private set; } = PlaybackState.Empty;
    public double SpeedMultiplier { get; private set; } = 5;
    public TimeSpan TotalTime => _segments.Count == 0 ? TimeSpan.Zero : _segments[^1].EndTime;
    public PlaybackFrame Frame => CreateFrame();

    public void Load(IEnumerable<PlaybackProgramItem> items,double rapidRateMmPerMin=5000,double toolChangeSeconds=0)
    {
        _segments.Clear();_elapsed=TimeSpan.Zero;var source=items.ToArray();var program=new ToolpathProgram{Operations=source.Select(item=>item.Operation).ToList()};var evaluation=MotionEvaluator.Evaluate(program,new(rapidRateMmPerMin,toolChangeSeconds));if(!evaluation.Success)throw new InvalidDataException(string.Join(" ",evaluation.Diagnostics.Select(x=>x.Message)));var diameters=source.GroupBy(item=>item.Operation.SourceOperationId).ToDictionary(group=>group.Key,group=>group.First().ToolDiameterMm);
        foreach(var trace in evaluation.Events.Where(trace=>trace.Kind is MotionEventKind.Rapid or MotionEventKind.Linear or MotionEventKind.Arc or MotionEventKind.Dwell or MotionEventKind.ToolChange).Where(trace=>trace.Duration>TimeSpan.Zero))
        {
            var kind=trace.Kind==MotionEventKind.Rapid?PlaybackMoveKind.Rapid:trace.Kind is MotionEventKind.Dwell or MotionEventKind.ToolChange?PlaybackMoveKind.Dwell:PlaybackMoveKind.Cutting;_segments.Add(new(trace.OperationId,trace.OperationName,kind,trace.Start,trace.StartZ,trace.End,trace.EndZ,trace.StartTime,trace.Duration,diameters.GetValueOrDefault(trace.OperationId),trace.ArcCenter,trace.Clockwise,trace.MoveIndex));
        }
        State = _segments.Count == 0 ? PlaybackState.Empty : PlaybackState.Paused;
    }
    public void Play() { if (State == PlaybackState.Empty) return; if (State == PlaybackState.Completed) _elapsed = TimeSpan.Zero; State = PlaybackState.Playing; }
    public void Pause() { if (State == PlaybackState.Playing) State = PlaybackState.Paused; }
    public void Stop() { _elapsed = TimeSpan.Zero; if (State != PlaybackState.Empty) State = PlaybackState.Paused; }
    public void Restart() { _elapsed = TimeSpan.Zero; if (State != PlaybackState.Empty) State = PlaybackState.Playing; }
    public void Clear() { _segments.Clear(); _elapsed = TimeSpan.Zero; State = PlaybackState.Empty; }
    public void SetSpeed(double multiplier) { if (!SupportedSpeeds.Contains(multiplier)) throw new ArgumentOutOfRangeException(nameof(multiplier)); SpeedMultiplier = multiplier; }
    public void Seek(double normalizedProgress) { _elapsed = TimeSpan.FromTicks((long)(TotalTime.Ticks * Math.Clamp(normalizedProgress, 0, 1))); if (_segments.Count > 0 && _elapsed >= TotalTime) State = PlaybackState.Completed; else if (State == PlaybackState.Completed) State = PlaybackState.Paused; }
    public void Advance(TimeSpan wallClock)
    {
        if (State != PlaybackState.Playing || wallClock <= TimeSpan.Zero) return; _elapsed += TimeSpan.FromTicks((long)(wallClock.Ticks * SpeedMultiplier));
        if (_elapsed >= TotalTime) { _elapsed = TotalTime; State = PlaybackState.Completed; }
    }
    private PlaybackFrame CreateFrame()
    {
        if (_segments.Count == 0) return PlaybackFrame.Empty;
        var completed = _segments.TakeWhile(segment => segment.EndTime <= _elapsed).Count(); var index = Math.Min(completed, _segments.Count - 1); var active = _segments[index];
        var local = active.Duration <= TimeSpan.Zero ? 1 : Math.Clamp((_elapsed - active.StartTime).TotalSeconds / active.Duration.TotalSeconds, 0, 1);
        if (State == PlaybackState.Completed) { completed = _segments.Count; index = _segments.Count - 1; active = _segments[index]; local = 1; }
        return new(State, _elapsed, TotalTime, TotalTime <= TimeSpan.Zero ? 0 : _elapsed.TotalSeconds / TotalTime.TotalSeconds, active.PointAt(local), active.ZAt(local), active.OperationId, active.OperationName, active.ToolDiameterMm, _segments, completed, index, local);
    }
}
