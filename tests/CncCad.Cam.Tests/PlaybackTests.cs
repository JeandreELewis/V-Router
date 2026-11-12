using CncCad.Cam.Toolpaths;
using CncCad.Core.Geometry;
using CncCad.Simulation.Playback;
using CncCad.Simulation.MachineTime;

namespace CncCad.Cam.Tests;

public class PlaybackTests
{
    [Fact] public void LinearAndRapidDurations_UseTheirRespectiveRates()
    {
        var operation = Operation(new RapidMove(new(50,0),0), new LinearMove(new(110,0),0,60)); var session = Load(operation);
        Assert.Equal(0.6, session.Frame.Segments[0].Duration.TotalSeconds, 6); Assert.Equal(60, session.Frame.Segments[1].Duration.TotalSeconds, 6);
    }
    [Fact] public void ArcInterpolation_RespectsDirectionAndDuration()
    {
        var operation = Operation(new RapidMove(new(10,0),0), new ArcMove(new(0,10),new(0,0),0,60,false)); var session = Load(operation); var arc = session.Frame.Segments[1];
        var midpoint = arc.PointAt(.5); Assert.Equal(Math.Sqrt(50), midpoint.X, 5); Assert.Equal(Math.Sqrt(50), midpoint.Y, 5); Assert.Equal(Math.PI*10/2, arc.Duration.TotalSeconds, 5);
    }
    [Fact] public void Playback_SupportsPlayPauseSeekStopRestartAndCompletion()
    {
        var session = Load(Operation(new LinearMove(new(60,0),-6,60))); session.Play(); session.Advance(TimeSpan.FromSeconds(2)); Assert.Equal(10, session.Frame.Elapsed.TotalSeconds, 5);
        session.Pause(); session.Advance(TimeSpan.FromSeconds(2)); Assert.Equal(10, session.Frame.Elapsed.TotalSeconds, 5);
        session.Seek(.5); Assert.Equal(.5, session.Frame.Progress, 5); session.Restart(); Assert.Equal(PlaybackState.Playing, session.State); Assert.Equal(0, session.Frame.Progress);
        session.Advance(TimeSpan.FromMinutes(1)); Assert.Equal(PlaybackState.Completed, session.State); session.Stop(); Assert.Equal(PlaybackState.Paused, session.State); Assert.Equal(0, session.Frame.Progress);
    }
    [Fact] public void SpeedMultiplier_DoesNotModifyMoveOrProgramDuration()
    {
        var move = new LinearMove(new(60,0),0,60); var session = Load(Operation(move)); var total = session.TotalTime; session.SetSpeed(20); session.Play(); session.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(20, session.Frame.Elapsed.TotalSeconds, 5); Assert.Equal(total, session.TotalTime); Assert.Equal(60, move.FeedRate);
    }
    [Fact] public void MultipleOperations_PreserveInputOrderAndIdentity()
    {
        var first = Operation(new LinearMove(new(10,0),0,100)); first.Name="First"; var second = Operation(new LinearMove(new(20,0),0,100)); second.Name="Second"; var session = new ToolpathPlaybackSession(); session.Load([new(first,6),new(second,3)]);
        Assert.Equal([first.SourceOperationId, second.SourceOperationId], session.Frame.Segments.Select(s=>s.OperationId)); Assert.Equal(["First","Second"], session.Frame.Segments.Select(s=>s.OperationName));
    }
    [Fact] public void PlaybackAndEstimateShareArcDwellAndToolChangeTiming()
    {
        var operation=Operation(new ToolChange(1,"Tool"),new RapidMove(new(10,0),0),new ArcMove(new(0,10),new(0,0),0,60,false),new DwellMove(2));var program=new ToolpathProgram{Operations=[operation]};var session=new ToolpathPlaybackSession();session.Load([new(operation,6)],5000,3);
        Assert.Equal(MachineTimeEstimator.Estimate(program,5000,3),session.TotalTime);Assert.Contains(session.Frame.Segments,segment=>segment.Kind==PlaybackMoveKind.Dwell&&segment.Duration==TimeSpan.FromSeconds(2));
    }
    private static ToolpathPlaybackSession Load(ToolpathOperation operation) { var session = new ToolpathPlaybackSession(); session.Load([new(operation,6)]); return session; }
    private static ToolpathOperation Operation(params ToolpathMove[] moves) => new() { SourceOperationId = Guid.NewGuid(), Name = "Profile", Moves = [..moves] };
}
