using CncCad.Cam.Toolpaths;
using CncCad.Core.Geometry;

namespace CncCad.Simulation.Playback;

public enum PlaybackState { Empty, Paused, Playing, Completed }
public enum PlaybackMoveKind { Rapid, Cutting }
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

    public void Load(IEnumerable<PlaybackProgramItem> items, double rapidRateMmPerMin = 5000)
    {
        _segments.Clear(); _elapsed = TimeSpan.Zero; var point = new Point2D(0,0); var z = 0d; var cursor = TimeSpan.Zero;
        foreach (var item in items)
        for(var moveIndex=0;moveIndex<item.Operation.Moves.Count;moveIndex++)
        {
            var move=item.Operation.Moves[moveIndex];
            PlaybackSegment? segment = move switch
            {
                RapidMove rapid => Create(item, PlaybackMoveKind.Rapid, point, z, rapid.End, rapid.Z, rapidRateMmPerMin, cursor,moveIndex),
                LinearMove linear => Create(item, PlaybackMoveKind.Cutting, point, z, linear.End, linear.Z, linear.FeedRate, cursor,moveIndex),
                ArcMove arc => CreateArc(item, point, z, arc, cursor,moveIndex),
                _ => null
            };
            if (segment is null) continue; _segments.Add(segment); cursor = segment.EndTime; point = segment.End; z = segment.EndZ;
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
    private static PlaybackSegment Create(PlaybackProgramItem item, PlaybackMoveKind kind, Point2D start, double startZ, Point2D end, double endZ, double rate, TimeSpan cursor,int moveIndex)
    {
        var distance = Math.Sqrt(Math.Pow(end.X-start.X,2)+Math.Pow(end.Y-start.Y,2)+Math.Pow(endZ-startZ,2)); var duration = TimeSpan.FromMinutes(distance / Math.Max(rate, 1));
        return new(item.Operation.SourceOperationId, item.Operation.Name, kind, start, startZ, end, endZ, cursor, duration, item.ToolDiameterMm,MoveIndex:moveIndex);
    }
    private static PlaybackSegment CreateArc(PlaybackProgramItem item, Point2D start, double startZ, ArcMove arc, TimeSpan cursor,int moveIndex)
    {
        var radius = start.DistanceTo(arc.Center); var a = Math.Atan2(start.Y-arc.Center.Y,start.X-arc.Center.X); var b = Math.Atan2(arc.End.Y-arc.Center.Y,arc.End.X-arc.Center.X); var sweep = b-a; if (arc.Clockwise && sweep >= 0) sweep -= Math.PI*2; else if (!arc.Clockwise && sweep <= 0) sweep += Math.PI*2;
        var distance = Math.Sqrt(Math.Pow(Math.Abs(sweep)*radius,2)+Math.Pow(arc.Z-startZ,2));
        return new(item.Operation.SourceOperationId, item.Operation.Name, PlaybackMoveKind.Cutting, start, startZ, arc.End, arc.Z, cursor, TimeSpan.FromMinutes(distance/Math.Max(arc.FeedRate,1)), item.ToolDiameterMm, arc.Center, arc.Clockwise,moveIndex);
    }
}
