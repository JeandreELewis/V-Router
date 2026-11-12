using Avalonia.Input;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;

namespace CncCad.Rendering.Canvas;

public sealed class DrawingToolSession
{
    private readonly List<Point2D> _points = [];
    private Point2D _cursor;
    private bool _dragging;
    public CanvasTool Tool { get; private set; }
    public DrawingMeasurement Measurement { get; private set; } = DrawingMeasurement.Idle;
    public bool IsActive => _points.Count > 0 || _dragging;

    public void SetTool(CanvasTool tool) { if (Tool != tool) Cancel(); Tool = tool; }
    public CadEntity? PointerPressed(Point2D point, Guid layerId, int clickCount)
    {
        if(!Finite(point))return null;
        _cursor = point;
        switch (Tool)
        {
            case CanvasTool.DrillPoint:return new DrillPointEntity{LayerId=layerId,Position=point};
            case CanvasTool.Rectangle: _points.Clear(); _points.Add(point); _dragging = true; break;
            case CanvasTool.Line:
                if (_points.Count == 0) _points.Add(point);
                else { if(_points[0].DistanceTo(point)<=GeometryTolerancePolicy.DefaultInteractiveMinimumLengthMm)return null;var entity = new LineEntity { LayerId = layerId, Start = _points[0], End = point }; Cancel(); return entity; }
                break;
            case CanvasTool.Circle:
                if (_points.Count == 0) _points.Add(point);
                else { var radius = _points[0].DistanceTo(point); if (radius <= GeometryTolerancePolicy.DefaultInteractiveMinimumLengthMm) return null; var entity = new CircleEntity { LayerId = layerId, Center = _points[0], Radius = radius }; Cancel(); return entity; }
                break;
            case CanvasTool.Polyline:
                if (_points.Count == 0 || _points[^1].DistanceTo(point) > GeometryTolerancePolicy.DefaultInteractiveMinimumLengthMm) _points.Add(point);
                if (clickCount >= 2 && _points.Count >= 2) return FinishPolyline(layerId, false);
                break;
            case CanvasTool.Arc:
                _points.Add(point);
                if (_points.Count == 3)
                {
                    try { var entity = GeometryAlgorithms.CreateThreePointArc(_points[0], _points[1], _points[2], layerId); Cancel(); return entity; }
                    catch (ArgumentException) { _points.RemoveAt(2); }
                }
                break;
        }
        UpdateMeasurement(); return null;
    }

    public CadEntity? PointerReleased(Point2D point, Guid layerId)
    {
        if (Tool != CanvasTool.Rectangle || !_dragging || _points.Count == 0||!Finite(point)) return null;
        _dragging = false;
        var start = _points[0]; Cancel();
        if (Math.Abs(start.X-point.X)<=GeometryTolerancePolicy.DefaultInteractiveMinimumLengthMm||Math.Abs(start.Y-point.Y)<=GeometryTolerancePolicy.DefaultInteractiveMinimumLengthMm) return null;
        return new RectangleEntity { LayerId = layerId, Origin = new(Math.Min(start.X, point.X), Math.Min(start.Y, point.Y)), Width = Math.Abs(point.X - start.X), Height = Math.Abs(point.Y - start.Y) };
    }

    public CadEntity? HandleKey(Key key, Guid layerId)
    {
        if (key == Key.Escape) { Cancel(); return null; }
        if (Tool == CanvasTool.Polyline && key == Key.Enter) return FinishPolyline(layerId, false);
        if (Tool == CanvasTool.Polyline && key == Key.C) return FinishPolyline(layerId, true);
        return null;
    }

    public void PointerMoved(Point2D point) { if(!Finite(point))return;_cursor = point; UpdateMeasurement(); }
    public void Cancel() { _points.Clear(); _dragging = false; Measurement = DrawingMeasurement.Idle; }

    public IReadOnlyList<CadEntity> Preview(Guid layerId)
    {
        if (_points.Count == 0) return [];
        return Tool switch
        {
            CanvasTool.Line => [new LineEntity { LayerId = layerId, Start = _points[0], End = _cursor }],
            CanvasTool.Rectangle => [new RectangleEntity { LayerId = layerId, Origin = new(Math.Min(_points[0].X, _cursor.X), Math.Min(_points[0].Y, _cursor.Y)), Width = Math.Abs(_cursor.X - _points[0].X), Height = Math.Abs(_cursor.Y - _points[0].Y) }],
            CanvasTool.Circle => [new CircleEntity { LayerId = layerId, Center = _points[0], Radius = _points[0].DistanceTo(_cursor) }],
            CanvasTool.Polyline => [new PolylineEntity { LayerId = layerId, Points = [.. _points, _cursor] }],
            CanvasTool.Arc when _points.Count == 1 => [new LineEntity { LayerId = layerId, Start = _points[0], End = _cursor }],
            CanvasTool.Arc when _points.Count == 2 => ArcPreview(layerId),
            _ => []
        };
    }

    private IReadOnlyList<CadEntity> ArcPreview(Guid layerId)
    {
        try { return [GeometryAlgorithms.CreateThreePointArc(_points[0], _points[1], _cursor, layerId)]; }
        catch (ArgumentException) { return [new PolylineEntity { LayerId = layerId, Points = [_points[0], _points[1], _cursor] }]; }
    }
    private CadEntity? FinishPolyline(Guid layerId, bool closed)
    {
        if (_points.Count < (closed?3:2)) return null;
        var entity = new PolylineEntity { LayerId = layerId, Points = [.. _points], IsClosed = closed }; Cancel(); return entity;
    }
    private void UpdateMeasurement()
    {
        if (_points.Count == 0) { Measurement = DrawingMeasurement.Idle; return; }
        var delta = _cursor - _points[^1]; var angle = Math.Atan2(delta.Y, delta.X) * 180 / Math.PI;
        Measurement = Tool switch
        {
            CanvasTool.Line => new("Line", DrawingPhase.DefiningGeometry, [new("L", delta.Length), new("∠", angle, "°"), new("ΔX", delta.X), new("ΔY", delta.Y)], "Click to finish"),
            CanvasTool.Polyline => new("Polyline", DrawingPhase.DefiningGeometry, [new("Segment", delta.Length), new("∠", angle, "°"), new("Total", PolylineLength() + delta.Length)], "Click next point · Enter finish · C close"),
            CanvasTool.Rectangle => new("Rectangle", DrawingPhase.DefiningGeometry, [new("W", Math.Abs(_cursor.X - _points[0].X)), new("H", Math.Abs(_cursor.Y - _points[0].Y))], "Release to finish"),
            CanvasTool.Circle => new("Circle", DrawingPhase.DefiningGeometry, [new("R", _points[0].DistanceTo(_cursor)), new("Ø", _points[0].DistanceTo(_cursor) * 2)], "Click to finish"),
            CanvasTool.Arc => ArcMeasurement(delta.Length),
            _ => DrawingMeasurement.Idle
        };
    }
    private double PolylineLength() { var total = 0d; for (var i = 1; i < _points.Count; i++) total += _points[i - 1].DistanceTo(_points[i]); return total; }
    private DrawingMeasurement ArcMeasurement(double chord)
    {
        if (_points.Count < 2) return new("3-Point Arc", DrawingPhase.DefiningStart, [new("Chord", chord)], "Click a point on the curve");
        try
        {
            var arc = GeometryAlgorithms.CreateThreePointArc(_points[0], _points[1], _cursor);
            return new("3-Point Arc", DrawingPhase.DefiningGeometry, [new("R", arc.Radius), new("Sweep", Math.Abs(arc.SweepAngleDegrees), "°")], "Click the end point");
        }
        catch (ArgumentException) { return new("3-Point Arc", DrawingPhase.DefiningGeometry, [new("Chord", _points[0].DistanceTo(_cursor))], "Move off the construction line"); }
    }
    private static bool Finite(Point2D point)=>double.IsFinite(point.X)&&double.IsFinite(point.Y);
}
