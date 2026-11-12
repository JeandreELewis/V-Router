using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CncCad.Cam.Toolpaths;
using CncCad.Cam.Safety;
using CncCad.Cam.Operations;
using CncCad.Cam.Profile;
using CncCad.Core.Documents;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;
using CncCad.Core.Preview;
using CncCad.Core.Selection;
using CncCad.Core.Snapping;
using CncCad.Core.Transforms;
using CncCad.Simulation.Playback;
using CncCad.Simulation.Stock;
using CncCad.Rendering.Preview;

namespace CncCad.Rendering.Canvas;

public enum CanvasTool { Select, Line, Polyline, Rectangle, Circle, Arc, DrillPoint, Trim }
internal enum SelectionDragMode { None, Scale, Rotate }

public sealed class CadCanvas : Control
{
    private readonly ViewportTransform _viewport = new();
    private readonly SnapEngine _snapEngine = new();
    private readonly DrawingToolSession _drawingSession = new();
    private readonly IInteractiveTrimService _trimService = new InteractiveTrimService();
    private readonly OrbitCamera _camera = new();
    private readonly StockPreviewRenderer _stockPreviewRenderer = new();
    private readonly DispatcherTimer _previewRefineTimer;
    private CancellationTokenSource? _previewRenderCancellation;
    private WriteableBitmap? _previewBitmap;
    private string? _previewBitmapKey;
    private string? _previewRequestedKey;
    private bool _interactivePreview;
    private ProjectPreviewSettings _previewSettings = ProjectPreviewSettings.LegacyDefault;
    private string? _previewMaterialName;
    private string? _previewMaterialCategory;
    private Point? _lastPan;
    private Point? _lastOrbit;
    private Point _cursorScreen;
    private Point2D? _cursorDocument;
    private Point2D? _selectionStart;
    private Point2D? _selectionEnd;
    private Point2D? _moveStart;
    private Point2D? _moveCurrent;
    private bool _selectionAdditive;
    private bool _didInitialFit;
    private bool _didInitial3DFit;
    private SnapResult _snapResult;
    private CadEntity? _nodeEntity;
    private int _nodeIndex=-1;
    private Point2D? _nodeStartPoint;
    private Point2D? _lastSelectionClick;private int _hitCycleIndex;
    private SelectionDragMode _selectionDragMode;private Point2D _transformPivot;private Point2D _transformStart;private Matrix2D _transformPreview=Matrix2D.Identity;private bool _uniformScale;
    private TrimCandidate? _trimCandidate;

    public CadDocument Document { get; set; } = new();
    public ToolpathProgram? Toolpaths { get; set; }
    public CanvasTool ActiveTool { get; private set; }
    public SelectionModel Selection { get; set; } = new();
    public SnapSettings SnapSettings { get; set; } = new();
    public bool ShowGrid { get; set; } = true;
    public bool SnappingEnabled { get; set; } = true;
    public bool ShowToolpaths { get; set; } = true;
    public bool ShowCutter { get; set; } = true;
    public bool ToolpathPreviewMode { get; set; }
    public bool NodeEditMode { get; set; }
    public Guid? ActiveMaterialSheetId { get; set; }
    public bool TabEditMode { get; set; }
    public IReadOnlyList<CamOperationDefinition> Operations { get; set; }=[];
    public PlaybackFrame PlaybackFrame { get; set; } = PlaybackFrame.Empty;
    public IReadOnlyList<FixtureDefinition> Fixtures { get; set; } = [];
    public IReadOnlyList<SafetyFinding> SafetyFindings { get; set; } = [];
    public StockHeightfield? StockSimulation { get; set; }
    public Action<CadEntity>? EntityCreated { get; set; }
    public Action<Vector2D>? SelectionMoved { get; set; }
    public Action<Matrix2D,string>? SelectionTransformed { get; set; }
    public Action<CadEntity,int,Point2D>? NodeMoved { get; set; }
    public Action<CadEntity,int,Point2D>? NodeSelected { get; set; }
    public Action? SelectionChanged { get; set; }
    public Action<Point2D, double>? PointerStatusChanged { get; set; }
    public Action<SnapResult, DrawingMeasurement>? InteractionStatusChanged { get; set; }
    public Action<Point2D>? TabPlacementRequested { get; set; }
    public Func<TrimCandidate,bool>? TrimRequested { get; set; }
    public Action<string>? TrimStatusChanged { get; set; }

    public CadCanvas()
    {
        ClipToBounds = true; Focusable = true;
        PointerPressed += OnPointerPressed; PointerMoved += OnPointerMoved; PointerReleased += OnPointerReleased; PointerWheelChanged += OnWheel;
        _previewRefineTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _previewRefineTimer.Tick += (_, _) =>
        {
            _previewRefineTimer.Stop();
            if (!_interactivePreview) return;
            _interactivePreview = false;
            InvalidatePreviewRender();
        };
    }

    public ProjectPreviewSettings PreviewSettings => _previewSettings;
    public void SetPreviewAppearance(ProjectPreviewSettings settings, string? materialName = null, string? materialCategory = null)
    {
        settings = settings.Normalize();
        if (_previewSettings == settings && _previewMaterialName == materialName && _previewMaterialCategory == materialCategory) return;
        _previewSettings = settings; _previewMaterialName = materialName; _previewMaterialCategory = materialCategory;
        InvalidatePreviewRender();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _previewRefineTimer.Stop();_previewRenderCancellation?.Cancel();_previewRenderCancellation?.Dispose();_previewRenderCancellation=null;_previewBitmap?.Dispose();_previewBitmap=null;
        base.OnDetachedFromVisualTree(e);
    }

    public void SetTool(CanvasTool tool) { ActiveTool = tool; _trimCandidate=null;_drawingSession.SetTool(tool); Cursor=new Cursor(tool==CanvasTool.Trim?StandardCursorType.Cross:StandardCursorType.Arrow);ReportInteraction(); InvalidateVisual(); }
    public bool HandleDrawingKey(Key key)
    {
        if(key==Key.Escape&&_nodeEntity is not null){_nodeEntity=null;_nodeIndex=-1;_nodeStartPoint=null;InvalidateVisual();return true;}
        var wasActive = _drawingSession.IsActive; var entity = _drawingSession.HandleKey(key, Document.ActiveLayerId);
        if (entity is not null) EntityCreated?.Invoke(entity); ReportInteraction(); InvalidateVisual(); return wasActive;
    }
    public void ZoomToFit() { if (Bounds.Width > 0) _viewport.Fit(Document.Job.WidthMm, Document.Job.HeightMm, Bounds.Width, Bounds.Height); InvalidateVisual(); }
    public CameraPreset CameraPreset => _camera.Preset;
    public void SetCameraPreset(CameraPreset preset) { _camera.SetPreset(preset); Fit3D(); }
    public void Fit3D() { if (Bounds.Width>0) { _camera.Fit(Document.Job.WidthMm,Document.Job.HeightMm,Document.Job.ThicknessMm,Document.Job.SafeZMm,Bounds.Width,Bounds.Height); _didInitial3DFit=true; } InvalidatePreviewRender(); }
    public void Enter3DPreview() { if (!_didInitial3DFit) Fit3D(); }
    public void Reset3DView() { _camera.SetPreset(CameraPreset.Isometric); _didInitial3DFit=false; if (ToolpathPreviewMode) Fit3D(); }
    public void ZoomToSelection()
    {
        var entities = Document.Entities.Where(entity => Selection.Contains(entity.Id)).ToArray(); if (entities.Length == 0 || Bounds.Width <= 0) return;
        var bounds = BoundingBox.FromPoints(entities.SelectMany(entity => { var b = entity.GetBounds(); return new[] { new Point2D(b.MinX, b.MinY), new Point2D(b.MaxX, b.MaxY) }; }));
        _viewport.Fit(bounds, Bounds.Width, Bounds.Height); InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context); context.FillRectangle(Brushes.Transparent, Bounds);
        if (!_didInitialFit && Bounds.Width > 0) { _viewport.Fit(Document.Job.WidthMm, Document.Job.HeightMm, Bounds.Width, Bounds.Height); _didInitialFit = true; }
        if (ToolpathPreviewMode) { Draw3DPreview(context); return; }
        if (ShowGrid) DrawGrid(context);
        var material = DocRect(new(0, 0, Document.Job.WidthMm, Document.Job.HeightMm));
        context.FillRectangle(new SolidColorBrush(Color.Parse("#242B31")), material); context.DrawRectangle(new Pen(new SolidColorBrush(Color.Parse("#84929E")), 1.5), material);
        DrawOrigin(context);
        foreach (var entity in Document.Entities.Where(IsEntityVisible)) DrawEntity(context, entity, false, ToolpathPreviewMode ? 0.45 : 1);
        DrawTabMarkers(context);
        var delta = _moveStart is { } start && _moveCurrent is { } end ? end - start : new Vector2D(0, 0);
        foreach (var entity in Document.Entities.Where(e => Selection.Contains(e.Id))) DrawSelection(context, entity, delta);
        if(Selection.Count>0&&!NodeEditMode&&_moveStart is null)DrawSelectionAdorner(context);
        if(NodeEditMode)DrawNodes(context);
        foreach (var preview in _drawingSession.Preview(Document.ActiveLayerId)) DrawEntity(context, preview, true);
        DrawTrimPreview(context);
        if (_moveStart is null && _selectionStart is { } selectionStart && _selectionEnd is { } selectionEnd) DrawSelectionArea(context, selectionStart, selectionEnd);
        DrawSnap(context);
        DrawMeasurementHud(context, CurrentMeasurement());
        DrawRulers(context);
    }
    private void DrawRulers(DrawingContext context)
    {
        const double size=22;var background=new SolidColorBrush(Color.FromArgb(235,24,31,36));var pen=new Pen(new SolidColorBrush(Color.Parse("#60717C")),1);var textBrush=new SolidColorBrush(Color.Parse("#AAB8C1"));context.FillRectangle(background,new Rect(0,0,Bounds.Width,size));context.FillRectangle(background,new Rect(0,0,size,Bounds.Height));
        var step=10d;while(step*_viewport.Zoom<55)step*=5;var min=Doc(new Point(size,Bounds.Height-size));var max=Doc(new Point(Bounds.Width, size));var typeface=new Typeface("Inter");
        for(var x=Math.Floor(min.X/step)*step;x<=max.X;x+=step){var screen=Screen(new Point2D(x,0)).X;if(screen<size)continue;context.DrawLine(pen,new(screen,size-7),new(screen,size));context.DrawText(new FormattedText(x.ToString("0.##",CultureInfo.InvariantCulture),CultureInfo.InvariantCulture,FlowDirection.LeftToRight,typeface,9,textBrush),new Point(screen+2,3));}
        for(var y=Math.Floor(min.Y/step)*step;y<=max.Y;y+=step){var screen=Screen(new Point2D(0,y)).Y;if(screen<size)continue;context.DrawLine(pen,new(size-7,screen),new(size,screen));context.DrawText(new FormattedText(y.ToString("0.##",CultureInfo.InvariantCulture),CultureInfo.InvariantCulture,FlowDirection.LeftToRight,typeface,9,textBrush),new Point(2,screen-11));}
    }
    private void DrawTrimPreview(DrawingContext context)
    {
        if(ActiveTool!=CanvasTool.Trim)return;if(_trimCandidate is{Success:true} candidate){var pen=new Pen(new SolidColorBrush(Color.Parse("#FF4D5E")),4);for(var i=1;i<candidate.RemovePath.Count;i++)context.DrawLine(pen,Screen(candidate.RemovePath[i-1]),Screen(candidate.RemovePath[i]));foreach(var boundary in candidate.Boundaries)context.DrawEllipse(new SolidColorBrush(Color.Parse("#FFD166")),new Pen(Brushes.Black,1),Screen(boundary),5,5);}
        var p=_cursorScreen;var iconPen=new Pen(new SolidColorBrush(Color.Parse("#F1F6F8")),1.5);context.DrawEllipse(null,iconPen,new(p.X+15,p.Y+12),3,3);context.DrawEllipse(null,iconPen,new(p.X+21,p.Y+12),3,3);context.DrawLine(iconPen,new(p.X+17,p.Y+14),new(p.X+25,p.Y+22));context.DrawLine(iconPen,new(p.X+19,p.Y+14),new(p.X+11,p.Y+22));
    }
    private void Draw3DPreview(DrawingContext context)
    {
        if(!DrawRenderedStock(context)){DrawMaterialSlab(context);DrawStockRemoval(context);} DrawDatum3D(context);var geometryPen=new Pen(new SolidColorBrush(Color.FromArgb(120,101,201,255)),1.2);
        DrawFixtures(context);
        foreach(var entity in Document.Entities.Where(IsEntityVisible)) DrawEntity3D(context,entity,geometryPen);
        if(ShowToolpaths && PlaybackFrame.State!=PlaybackState.Empty) DrawPlayback(context,PlaybackFrame); else if(ShowToolpaths && Toolpaths is not null) DrawToolpaths(context,Toolpaths);
        foreach(var finding in SafetyFindings.Where(x=>x.Position is not null)){var p=P(finding.Position!.Value,finding.Z??0);IBrush color=finding.Severity==SafetySeverity.Error?Brushes.Red:new SolidColorBrush(Color.Parse("#E8B85C"));context.DrawEllipse(null,new Pen(color,2.5),p,7,7);context.DrawLine(new Pen(color,2),new(p.X-10,p.Y),new(p.X+10,p.Y));}
    }
    private bool DrawRenderedStock(DrawingContext context)
    {
        RequestPreviewRender();
        if (_previewBitmap is null) return false;
        context.DrawImage(_previewBitmap, new Rect(0, 0, Bounds.Width, Bounds.Height));
        return true;
    }
    private void RequestPreviewRender()
    {
        if (Bounds.Width < 2 || Bounds.Height < 2 || Document.Job.WidthMm <= 0 || Document.Job.HeightMm <= 0 || Document.Job.ThicknessMm <= 0) return;
        var camera = PreviewCameraState.From(_camera);
        var field = StockSimulation;
        var quality = _interactivePreview ? "interactive" : "refined";
        var sourceIdentity=field is null?RuntimeHelpers.GetHashCode(Document):RuntimeHelpers.GetHashCode(field);
        var key = string.Join("|", sourceIdentity, Document.Job.WidthMm.ToString("R",CultureInfo.InvariantCulture), Document.Job.HeightMm.ToString("R",CultureInfo.InvariantCulture), Document.Job.ThicknessMm.ToString("R",CultureInfo.InvariantCulture), Bounds.Width.ToString("0.0",CultureInfo.InvariantCulture), Bounds.Height.ToString("0.0",CultureInfo.InvariantCulture), camera.Yaw.ToString("R",CultureInfo.InvariantCulture), camera.Pitch.ToString("R",CultureInfo.InvariantCulture), camera.Zoom.ToString("R",CultureInfo.InvariantCulture), camera.PanX.ToString("R",CultureInfo.InvariantCulture), camera.PanY.ToString("R",CultureInfo.InvariantCulture), _previewSettings.Style, _previewSettings.MaterialPreset, _previewSettings.GrainDirection, _previewSettings.TintHex, _previewMaterialName, _previewMaterialCategory, quality);
        if (_previewBitmapKey == key || _previewRequestedKey == key) return;
        _previewRenderCancellation?.Cancel(); _previewRenderCancellation?.Dispose();
        var cancellation = _previewRenderCancellation = new CancellationTokenSource();
        _previewRequestedKey = key;
        var viewportWidth = Bounds.Width; var viewportHeight = Bounds.Height;
        var scale = _interactivePreview ? .55 : 1;
        var targetWidth = Math.Max(32, (int)Math.Round(Math.Min(viewportWidth * scale, _interactivePreview ? 900 : 1600)));
        var targetHeight = Math.Max(32, (int)Math.Round(Math.Min(viewportHeight * scale, _interactivePreview ? 700 : 1200)));
        var segmentLimit = _interactivePreview ? 160 : 420;
        var settings = _previewSettings; var materialName = _previewMaterialName; var materialCategory = _previewMaterialCategory;
        var widthMm = Document.Job.WidthMm; var heightMm = Document.Job.HeightMm; var thicknessMm = Document.Job.ThicknessMm;
        _ = Task.Run(() =>
        {
            var mesh = StockSurfaceMeshBuilder.Build(field, widthMm, heightMm, thicknessMm, segmentLimit);
            return _stockPreviewRenderer.Render(mesh, camera, settings, materialName, materialCategory, targetWidth, targetHeight, viewportWidth, viewportHeight, cancellation.Token);
        }, cancellation.Token).ContinueWith(task =>
        {
            if (task.IsCanceled || task.IsFaulted || cancellation.IsCancellationRequested)
            {
                Dispatcher.UIThread.Post(()=>{if(_previewRequestedKey==key)_previewRequestedKey=null;},DispatcherPriority.Background);return;
            }
            Dispatcher.UIThread.Post(() =>
            {
                if (cancellation.IsCancellationRequested || _previewRequestedKey != key) return;
                var bitmap = CreateBitmap(task.Result);
                var previous = _previewBitmap; _previewBitmap = bitmap; _previewBitmapKey = key; _previewRequestedKey = null;
                previous?.Dispose(); InvalidateVisual();
            }, DispatcherPriority.Render);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    private static WriteableBitmap CreateBitmap(PreviewRaster raster)
    {
        var bitmap = new WriteableBitmap(new PixelSize(raster.Width,raster.Height),new Vector(96,96),PixelFormat.Bgra8888,AlphaFormat.Premul);
        using var frame = bitmap.Lock();
        var sourceStride = raster.Width * 4;
        for (var row = 0; row < raster.Height; row++) Marshal.Copy(raster.BgraPixels,row*sourceStride,IntPtr.Add(frame.Address,row*frame.RowBytes),sourceStride);
        return bitmap;
    }
    private void InvalidatePreviewRender()
    {
        _previewRequestedKey=null;_previewRenderCancellation?.Cancel();InvalidateVisual();
    }
    private void BeginInteractivePreview()
    {
        if(!_interactivePreview){_interactivePreview=true;InvalidatePreviewRender();}
        _previewRefineTimer.Stop();_previewRefineTimer.Start();
    }
    private void DrawStockRemoval(DrawingContext context)
    {
        if(StockSimulation is not{} field)return;var stride=Math.Max(1,Math.Max(field.Columns,field.Rows)/180);var pen=new Pen(new SolidColorBrush(Color.FromArgb(155,202,169,113)),.8);
        for(var row=0;row<field.Rows;row+=stride)for(var column=stride;column<field.Columns;column+=stride){var aColumn=column-stride;var a=new Point2D((aColumn+.5)*field.CellSizeMm,(row+.5)*field.CellSizeMm);var b=new Point2D((column+.5)*field.CellSizeMm,(row+.5)*field.CellSizeMm);context.DrawLine(pen,P(a,field[aColumn,row]),P(b,field[column,row]));}
        for(var column=0;column<field.Columns;column+=stride)for(var row=stride;row<field.Rows;row+=stride){var aRow=row-stride;var a=new Point2D((column+.5)*field.CellSizeMm,(aRow+.5)*field.CellSizeMm);var b=new Point2D((column+.5)*field.CellSizeMm,(row+.5)*field.CellSizeMm);context.DrawLine(pen,P(a,field[column,aRow]),P(b,field[column,row]));}
    }
    private void DrawFixtures(DrawingContext context)
    {
        foreach(var fixture in Fixtures){var z=fixture.TopZmm;var bottom=-Document.Job.ThicknessMm;var top=new[]{P(fixture.MinXmm,fixture.MinYmm,z),P(fixture.MaxXmm,fixture.MinYmm,z),P(fixture.MaxXmm,fixture.MaxYmm,z),P(fixture.MinXmm,fixture.MaxYmm,z)};var low=new[]{P(fixture.MinXmm,fixture.MinYmm,bottom),P(fixture.MaxXmm,fixture.MinYmm,bottom),P(fixture.MaxXmm,fixture.MaxYmm,bottom),P(fixture.MinXmm,fixture.MaxYmm,bottom)};FillFace(context,top,"#7A493E");var pen=new Pen(new SolidColorBrush(Color.Parse("#FF9A7A")),1.4);for(var i=0;i<4;i++){context.DrawLine(pen,top[i],top[(i+1)%4]);context.DrawLine(pen,top[i],low[i]);}}
    }
    private void DrawMaterialSlab(DrawingContext context)
    {
        var w=Document.Job.WidthMm; var h=Document.Job.HeightMm; var z=-Document.Job.ThicknessMm; var top=new[]{P(0,0,0),P(w,0,0),P(w,h,0),P(0,h,0)}; var bottom=new[]{P(0,0,z),P(w,0,z),P(w,h,z),P(0,h,z)};
        FillFace(context,new[]{top[0],top[1],bottom[1],bottom[0]},"#303A40"); FillFace(context,new[]{top[1],top[2],bottom[2],bottom[1]},"#283239"); FillFace(context,top,"#465159");
        var edge=new Pen(new SolidColorBrush(Color.Parse("#91A1AD")),1.2); for(var i=0;i<4;i++){context.DrawLine(edge,top[i],top[(i+1)%4]);context.DrawLine(edge,bottom[i],bottom[(i+1)%4]);context.DrawLine(edge,top[i],bottom[i]);}
    }
    private void DrawDatum3D(DrawingContext context){var w=Document.Job.WidthMm;var h=Document.Job.HeightMm;var datum=MachineCoordinateTransform.OriginInDocument(Document.Job);var o=P(datum,0); context.DrawLine(new Pen(Brushes.Red,2),o,P(datum.X+Math.Min(w,40),datum.Y,0));context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#65D18A")),2),o,P(datum.X,datum.Y+Math.Min(h,40),0));context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#65C9FF")),2),o,P(datum,Math.Min(40,Document.Job.SafeZMm+20)));}
    private void FillFace(DrawingContext context,Point[] points,string color){var g=new StreamGeometry();using(var s=g.Open()){s.BeginFigure(points[0],true);foreach(var p in points.Skip(1))s.LineTo(p);s.EndFigure(true);}context.DrawGeometry(new SolidColorBrush(Color.Parse(color)),null,g);}
    private void DrawEntity3D(DrawingContext context,CadEntity entity,Pen pen)
    {
        var points=EntityGeometry.WorldPoints(entity).ToArray();
        for(var i=1;i<points.Length;i++)context.DrawLine(pen,P(points[i-1],0),P(points[i],0)); if(entity is RectangleEntity or PolylineEntity{IsClosed:true} && points.Length>2)context.DrawLine(pen,P(points[^1],0),P(points[0],0));
    }

    private void DrawGrid(DrawingContext context)
    {
        var spacing = _viewport.Zoom * SnapSettings.GridSpacingMm; while (spacing < 14) spacing *= 5;
        var pen = new Pen(new SolidColorBrush(Color.Parse("#273039")), 1);
        for (var x = _viewport.Pan.X % spacing; x < Bounds.Width; x += spacing) context.DrawLine(pen, new(x, 0), new(x, Bounds.Height));
        for (var y = (Bounds.Height - _viewport.Pan.Y) % spacing; y < Bounds.Height; y += spacing) context.DrawLine(pen, new(0, y), new(Bounds.Width, y));
    }
    private void DrawOrigin(DrawingContext context)
    {
        var p = Screen(MachineCoordinateTransform.OriginInDocument(Document.Job)); context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#E96363")), 2), p, new(p.X + 24, p.Y)); context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#65D18A")), 2), p, new(p.X, p.Y - 24));
    }
    private void DrawEntity(DrawingContext context, CadEntity entity, bool preview, double opacity = 1,Pen? overridePen=null)
    {
        var layerColor=entity.StrokeColor??Document.Layers.FirstOrDefault(x=>x.Id==entity.LayerId)?.Color;var color = preview ? Colors.White : Color.TryParse(layerColor,out var parsed)?parsed:Color.Parse("#65C9FF"); color = Color.FromArgb((byte)(255 * opacity), color.R, color.G, color.B);
        var pen = overridePen??new Pen(new SolidColorBrush(color), preview ? 1 : 1.5, dashStyle: preview ? DashStyle.Dash : null);
        if(entity is DrillPointEntity drill){var p=Screen(drill.Transform.Transform(drill.Position));context.DrawEllipse(null,pen,p,5,5);context.DrawLine(pen,new(p.X-7,p.Y),new(p.X+7,p.Y));context.DrawLine(pen,new(p.X,p.Y-7),new(p.X,p.Y+7));return;}
        if(entity is VectorPathEntity path){foreach(var figure in path.Figures){var figurePoints=VectorPathGeometry.Flatten([figure],.02).Select(path.Transform.Transform).ToArray();for(var i=1;i<figurePoints.Length;i++)context.DrawLine(pen,Screen(figurePoints[i-1]),Screen(figurePoints[i]));}return;}
        if(entity is TextEntity text){foreach(var outline in text.Outlines){var outlinePoints=VectorPathGeometry.Flatten([outline],.02).Select(text.Transform.Transform).ToArray();for(var i=1;i<outlinePoints.Length;i++)context.DrawLine(pen,Screen(outlinePoints[i-1]),Screen(outlinePoints[i]));}return;}
        var points=EntityGeometry.WorldPoints(entity);for(var i=1;i<points.Count;i++)context.DrawLine(pen,Screen(points[i-1]),Screen(points[i]));
        if(entity is RectangleEntity or CircleEntity or EllipseEntity or PolylineEntity{IsClosed:true}&&points.Count>2)context.DrawLine(pen,Screen(points[^1]),Screen(points[0]));
    }
    private void DrawArc(DrawingContext context, ArcEntity arc, Pen pen)
    {
        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(Screen(GeometryAlgorithms.PointOnArc(arc, arc.StartAngleDegrees)), false);
            stream.ArcTo(Screen(GeometryAlgorithms.PointOnArc(arc, arc.StartAngleDegrees + arc.SweepAngleDegrees)), new Size(arc.Radius * _viewport.Zoom, arc.Radius * _viewport.Zoom), 0, Math.Abs(arc.SweepAngleDegrees) > 180, arc.SweepAngleDegrees > 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
            stream.EndFigure(false);
        }
        context.DrawGeometry(null, pen, geometry);
    }
    private void DrawSelection(DrawingContext context, CadEntity entity, Vector2D offset)
    {
        if(_selectionDragMode!=SelectionDragMode.None){var ghost=EntityGeometry.Clone(entity);ghost.Transform=Matrix2D.Then(ghost.Transform,_transformPreview);DrawEntity(context,ghost,true);}else if (offset.Length > 1e-9) { var ghost = EntityGeometry.Clone(entity); EntityGeometry.Translate(ghost, offset); DrawEntity(context, ghost, true); }
        var primary=Selection.PrimaryId==entity.Id;DrawEntity(context,entity,false,1,new Pen(new SolidColorBrush(Color.FromArgb(220,10,14,17)),primary?7:5));DrawEntity(context,entity,false,1,new Pen(new SolidColorBrush(primary?Color.Parse("#FFE36E"):Color.Parse("#FFF2B0")),primary?3.2:2.2));
    }
    private BoundingBox? SelectedBounds(){var entities=Document.Entities.Where(x=>Selection.Contains(x.Id)).ToArray();return entities.Length==0?null:BoundingBox.FromPoints(entities.SelectMany(x=>{var b=x.GetBounds();return new[]{new Point2D(b.MinX,b.MinY),new Point2D(b.MaxX,b.MaxY)};}));}
    private void DrawSelectionAdorner(DrawingContext context){if(SelectedBounds() is not{} bounds)return;var corners=new[]{new Point2D(bounds.MinX,bounds.MinY),new Point2D(bounds.MaxX,bounds.MinY),new Point2D(bounds.MaxX,bounds.MaxY),new Point2D(bounds.MinX,bounds.MaxY)}.Select(_transformPreview.Transform).Select(Screen).ToArray();var pen=new Pen(new SolidColorBrush(Color.FromArgb(150,255,209,102)),1,dashStyle:DashStyle.Dash);for(var i=0;i<4;i++)context.DrawLine(pen,corners[i],corners[(i+1)%4]);var brush=new SolidColorBrush(Color.Parse("#FFD166"));foreach(var point in corners)context.FillRectangle(brush,new(point.X-5,point.Y-5,10,10));var topMid=new Point((corners[2].X+corners[3].X)/2,(corners[2].Y+corners[3].Y)/2);var center=new Point(corners.Average(x=>x.X),corners.Average(x=>x.Y));var direction=new Vector(topMid.X-center.X,topMid.Y-center.Y);var length=Math.Max(1,direction.Length);var rotate=new Point(topMid.X+direction.X/length*28,topMid.Y+direction.Y/length*28);context.DrawLine(pen,topMid,rotate);context.DrawEllipse(new SolidColorBrush(Color.Parse("#65C9FF")),new Pen(Brushes.White,1),rotate,6,6);}
    private void DrawSelectionArea(DrawingContext context, Point2D a, Point2D b)
    {
        var rect = DocRect(new(Math.Min(a.X,b.X), Math.Min(a.Y,b.Y), Math.Max(a.X,b.X), Math.Max(a.Y,b.Y)));
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(35, 101, 201, 255)), rect); context.DrawRectangle(new Pen(new SolidColorBrush(Color.Parse("#65C9FF")), 1, dashStyle: DashStyle.Dash), rect);
    }
    private void DrawNodes(DrawingContext context){var anchorBrush=new SolidColorBrush(Color.Parse("#FFD166"));var handleBrush=new SolidColorBrush(Color.Parse("#65C9FF"));var handlePen=new Pen(new SolidColorBrush(Color.FromArgb(150,101,201,255)),1);foreach(var entity in Document.Entities.Where(e=>Selection.Contains(e.Id)&&e is LineEntity or PolylineEntity or VectorPathEntity&&!ContainsExactArc(e))){if(entity is not VectorPathEntity path){foreach(var p in EditableNodes(entity)){var s=Screen(p);context.DrawEllipse(anchorBrush,null,s,5,5);}continue;}foreach(var figure in path.Figures){var current=path.Transform.Transform(figure.Start);var startScreen=Screen(current);context.DrawEllipse(anchorBrush,null,startScreen,5,5);foreach(var segment in figure.Segments){switch(segment){case QuadraticPathSegment q:DrawHandle(path.Transform.Transform(q.Control),current,q.End);break;case CubicPathSegment c:DrawHandle(path.Transform.Transform(c.Control1),current,c.End);DrawHandle(path.Transform.Transform(c.Control2),path.Transform.Transform(c.End),c.End);break;}current=path.Transform.Transform(segment.End);context.DrawEllipse(anchorBrush,null,Screen(current),5,5);}}}void DrawHandle(Point2D control,Point2D anchor,Point2D end){var a=Screen(anchor);var h=Screen(control);context.DrawLine(handlePen,a,h);context.FillRectangle(handleBrush,new(h.X-4,h.Y-4,8,8));}}
    private static bool ContainsExactArc(CadEntity entity)=>entity is VectorPathEntity path&&path.Figures.SelectMany(figure=>figure.Segments).Any(segment=>segment is ArcPathSegment);
    private static IEnumerable<Point2D> EditableNodes(CadEntity entity)=>entity switch{LineEntity l=>new[]{l.Transform.Transform(l.Start),l.Transform.Transform(l.End)},PolylineEntity p=>p.Points.Select(p.Transform.Transform),VectorPathEntity path when !ContainsExactArc(path)=>VectorPathNodes.Points(path).Select(path.Transform.Transform),_=>[]};
    private void DrawSnap(DrawingContext context)
    {
        if (_snapResult.Type == SnapType.None) return; var p = Screen(_snapResult.Point); var pen = new Pen(new SolidColorBrush(Color.Parse("#71E6A8")), 2);
        context.DrawEllipse(null, pen, p, 5, 5); context.DrawLine(pen, new(p.X - 7, p.Y), new(p.X + 7, p.Y)); context.DrawLine(pen, new(p.X, p.Y - 7), new(p.X, p.Y + 7));
    }
    private void DrawMeasurementHud(DrawingContext context, DrawingMeasurement measurement)
    {
        if (!measurement.IsVisible) return;
        var titleText = _snapResult.Type == SnapType.None ? measurement.ToolName : $"{measurement.ToolName}  ·  {_snapResult.Type} snap";
        var title = Text(titleText, 12, new SolidColorBrush(Color.Parse("#8EDCFF")), FontWeight.SemiBold);
        var values = Text(measurement.Summary, 13, Brushes.White, FontWeight.SemiBold);
        var guidance = Text(measurement.Guidance, 10, new SolidColorBrush(Color.Parse("#A9B7C0")), FontWeight.Normal);
        var width = Math.Max(title.Width, Math.Max(values.Width, guidance.Width)) + 24; var height = title.Height + values.Height + guidance.Height + 22;
        var rect = HudLayout.Place(Bounds.Size, _cursorScreen, new(width, height));
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(238, 25, 33, 39)), new Pen(new SolidColorBrush(Color.Parse("#4C90AD")), 1), new RoundedRect(rect, 7), new BoxShadows(new BoxShadow { Blur = 10, Color = Color.FromArgb(120, 0, 0, 0), OffsetY = 3 }));
        var x = rect.X + 12; var y = rect.Y + 9; context.DrawText(title, new(x, y)); y += title.Height + 2; context.DrawText(values, new(x, y)); y += values.Height + 2; context.DrawText(guidance, new(x, y));
    }
    private void DrawTabMarkers(DrawingContext context){var pen=new Pen(new SolidColorBrush(Color.Parse("#F2C14E")),2);foreach(var operation in Operations.OfType<ProfileOperationDefinition>().Where(x=>x.Tabs.Enabled&&(ActiveMaterialSheetId is null?x.MaterialSheetId is null:x.MaterialSheetId==ActiveMaterialSheetId)))foreach(var id in operation.GeometryIds){var entity=Document.Entities.FirstOrDefault(x=>x.Id==id);if(entity is null)continue;var path=EntityGeometry.WorldPoints(entity);if(path.Count<3)continue;foreach(var point in ProfileTabEngine.Positions(path,operation.Tabs,id)){var p=Screen(point);context.DrawLine(pen,new(p.X-6,p.Y-6),new(p.X+6,p.Y+6));context.DrawLine(pen,new(p.X-6,p.Y+6),new(p.X+6,p.Y-6));}}}
    private static FormattedText Text(string value, double size, IBrush brush, FontWeight weight) => new(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter", FontStyle.Normal, weight), size, brush);
    private void DrawToolpaths(DrawingContext context, ToolpathProgram program)
    {
        var current = new Point2D(0, 0);
        var z = 0d;
        foreach (var move in program.Operations.SelectMany(o => o.Moves))
        {
            if (move is RapidMove rapid) { context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#E8B85C")), 1, dashStyle: DashStyle.Dash), P(current,z), P(rapid.End,rapid.Z)); current = rapid.End; z=rapid.Z; }
            else if (move is LinearMove linear) { context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#EF5DA8")), 2), P(current,z), P(linear.End,linear.Z)); current = linear.End; z=linear.Z; }
        }
    }
    private void DrawPlayback(DrawingContext context, PlaybackFrame frame)
    {
        var future = new Pen(new SolidColorBrush(Color.Parse("#46545D")), 1);
        var cut = new Pen(new SolidColorBrush(Color.Parse("#EF5DA8")), 2.4);
        var rapid = new Pen(new SolidColorBrush(Color.Parse("#E8B85C")), 1.4, dashStyle: DashStyle.Dash);
        var plunge = new Pen(new SolidColorBrush(Color.Parse("#9B8CFF")), 2.4);
        for (var i = 0; i < frame.Segments.Count; i++)
        {
            var completion = i < frame.CompletedSegmentCount ? 1 : i == frame.ActiveSegmentIndex ? frame.ActiveSegmentProgress : 0;
            var segment=frame.Segments[i]; var isPlunge=segment.Start.DistanceTo(segment.End)<1e-9&&Math.Abs(segment.EndZ-segment.StartZ)>1e-9;
            DrawPlaybackSegment(context, segment, completion, completion > 0 ? (segment.Kind == PlaybackMoveKind.Rapid ? rapid : isPlunge ? plunge : cut) : future);
        }
        if(!ShowCutter)return;var marker = P(frame.Position,frame.Z); var radius = Math.Max(5, frame.ToolDiameterMm * _camera.Zoom / 2); var markerPen = new Pen(new SolidColorBrush(Color.Parse("#72E6A8")), 2);
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(55, 114, 230, 168)), markerPen, marker, radius, radius); context.DrawLine(markerPen, new(marker.X-radius-3,marker.Y), new(marker.X+radius+3,marker.Y)); context.DrawLine(markerPen, new(marker.X,marker.Y-radius-3),new(marker.X,marker.Y+radius+3));
        context.DrawLine(markerPen,marker,P(frame.Position,Math.Max(frame.Z,Document.Job.SafeZMm)));
        if(frame.ActiveSegmentIndex>=0&&frame.ActiveSegmentIndex<frame.Segments.Count){var active=frame.Segments[frame.ActiveSegmentIndex];var aheadFraction=Math.Min(1,frame.ActiveSegmentProgress+.04);var ahead=P(active.PointAt(aheadFraction),active.ZAt(aheadFraction));context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#F7F1A1")),2.5),marker,ahead);}
    }
    private void DrawPlaybackSegment(DrawingContext context, PlaybackSegment segment, double completion, Pen pen)
    {
        var steps = segment.ArcCenter is null ? 1 : 32; var previous = segment.Start;
        if (completion <= 0) { for (var step = 1; step <= steps; step++) { var f=step/(double)steps; var next=segment.PointAt(f); context.DrawLine(pen,P(previous,segment.ZAt((step-1)/(double)steps)),P(next,segment.ZAt(f))); previous=next;} return; }
        var completedSteps = Math.Max(1, (int)Math.Ceiling(steps * completion));
        var prior=0d; for (var step = 1; step <= completedSteps; step++) { var fraction=Math.Min(completion,step/(double)steps);var next=segment.PointAt(fraction);var actualPen=segment.ZAt(fraction)<-Document.Job.ThicknessMm?new Pen(Brushes.Red,2.4):pen;context.DrawLine(actualPen,P(previous,segment.ZAt(prior)),P(next,segment.ZAt(fraction)));previous=next;prior=fraction;if(fraction>=completion)break;}
        if (completion < 1) { previous = segment.PointAt(completion); var previousFraction=completion; for (var step = completedSteps + 1; step <= steps; step++) { var fraction=step/(double)steps; var next = segment.PointAt(fraction); context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#46545D")), 1), P(previous,segment.ZAt(previousFraction)), P(next,segment.ZAt(fraction))); previous = next; previousFraction=fraction; } }
    }

    private Point Screen(Point2D p) { var value = _viewport.DocumentToScreen(p, Bounds.Height); return new(value.X, value.Y); }
    private Point P(Point2D p,double z)=>P(p.X,p.Y,z);
    private Point P(double x,double y,double z){var p=_camera.Project(new(x,y,z),Bounds.Width,Bounds.Height);return new(p.X,p.Y);}
    private Rect DocRect(BoundingBox b) { var topLeft = Screen(new(b.MinX, b.MaxY)); return new(topLeft.X, topLeft.Y, b.Width * _viewport.Zoom, b.Height * _viewport.Zoom); }
    private Point2D Doc(Point p) => _viewport.ScreenToDocument(new(p.X, p.Y), Bounds.Height);
    private Point2D Snapped(Point2D point) { if (!SnappingEnabled) { _snapResult = SnapResult.None(point); return point; } var entities = (_moveStart is null ? Document.Entities : Document.Entities.Where(e => !Selection.Contains(e.Id))).Where(IsEntityVisible); _snapResult = _snapEngine.Snap(point, entities, SnapSettings, 10 / _viewport.Zoom); return _snapResult.Point; }
    private bool IsEntityVisible(CadEntity entity)=>entity.IsVisible&&(ActiveMaterialSheetId is null?entity.MaterialSheetId is null:entity.MaterialSheetId==ActiveMaterialSheetId)&&Document.Layers.FirstOrDefault(x=>x.Id==entity.LayerId)?.IsVisible!=false;
    private bool IsEditable(CadEntity entity)=>IsEntityVisible(entity)&&Document.Layers.FirstOrDefault(x=>x.Id==entity.LayerId)?.IsLocked!=true;
    private DrawingMeasurement CurrentMeasurement() => _moveStart is { } start && _moveCurrent is { } end ? DrawingMeasurement.Move(end.X - start.X, end.Y - start.Y) : _drawingSession.Measurement;
    private void ReportInteraction() => InteractionStatusChanged?.Invoke(_snapResult, CurrentMeasurement());

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Focus(); var screen = e.GetPosition(this); _cursorScreen = screen;
        if(ToolpathPreviewMode){BeginInteractivePreview();if(e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)_lastOrbit=screen;else if(e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed)_lastPan=screen;e.Pointer.Capture(this);return;}
        if(ActiveTool==CanvasTool.Trim&&IsLeftPress(e))
        {
            var click=Doc(screen);UpdateTrimCandidate(click);
            if(_trimCandidate is not {Success:true} candidate){TrimStatusChanged?.Invoke(_trimCandidate?.Error??"Hover a vector portion bounded by crossings.");InvalidateVisual();return;}
            try
            {
                if(TrimRequested?.Invoke(candidate)==true){_trimCandidate=null;TrimStatusChanged?.Invoke("Vector trimmed · scissors remains active");}
                else TrimStatusChanged?.Invoke("Trim was not applied. The red preview is still selected.");
            }
            catch(Exception ex){TrimStatusChanged?.Invoke($"Trim failed: {ex.Message}");}
            InvalidateVisual();return;
        }
        var point = Snapped(Doc(screen));
        if(TabEditMode&&e.GetCurrentPoint(this).Properties.IsLeftButtonPressed){TabPlacementRequested?.Invoke(point);InvalidateVisual();return;}
        if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed) { _lastPan = screen; e.Pointer.Capture(this); return; }
        if(NodeEditMode){var candidates=Document.Entities.Where(x=>Selection.Contains(x.Id)&&x is LineEntity or PolylineEntity or VectorPathEntity).SelectMany(x=>EditableNodes(x).Select((p,i)=>(Entity:x,Index:i,Point:p))).OrderBy(x=>x.Point.DistanceTo(point)).ToArray();if(candidates.FirstOrDefault() is var node&&node.Entity is not null&&node.Point.DistanceTo(point)<=8/_viewport.Zoom){_nodeEntity=node.Entity;_nodeIndex=node.Index;_nodeStartPoint=node.Point;_cursorDocument=node.Point;NodeSelected?.Invoke(node.Entity,node.Index,node.Point);e.Pointer.Capture(this);return;}}
        if (ActiveTool == CanvasTool.Select)
        {
            if(TryStartSelectionTransform(screen,e.KeyModifiers)){e.Pointer.Capture(this);InvalidateVisual();return;}
            var hit = EntityHitTester.HitTest(Document.Entities.Where(IsEditable), point, 6 / _viewport.Zoom);
            if (hit is not null && Selection.Contains(hit.Id)) { _moveStart = point; _moveCurrent = point;_selectionStart=point;_selectionEnd=point; }
            else { _selectionStart = point; _selectionEnd = point; }
            _selectionAdditive = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Shift); e.Pointer.Capture(this);
        }
        else { var entity = _drawingSession.PointerPressed(point, Document.ActiveLayerId, e.ClickCount); if (entity is not null) EntityCreated?.Invoke(entity); e.Pointer.Capture(this); }
        ReportInteraction(); InvalidateVisual();
    }
    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var screen = e.GetPosition(this); _cursorScreen = screen;
        if(ToolpathPreviewMode){if(_lastOrbit is{} orbit){BeginInteractivePreview();_camera.Orbit((screen.X-orbit.X)*.008,(orbit.Y-screen.Y)*.008);_lastOrbit=screen;}if(_lastPan is{} pan){BeginInteractivePreview();_camera.Pan(screen.X-pan.X,screen.Y-pan.Y);_lastPan=screen;}InvalidateVisual();return;}
        if(ActiveTool==CanvasTool.Trim){_cursorDocument=Doc(screen);UpdateTrimCandidate(_cursorDocument.Value);TrimStatusChanged?.Invoke(_trimCandidate is{Success:true}?"Click the red segment to trim it":_trimCandidate?.Error??"Hover the portion to remove");PointerStatusChanged?.Invoke(_cursorDocument.Value,_viewport.Zoom);InvalidateVisual();return;}
        if(_selectionDragMode!=SelectionDragMode.None){var current=Doc(screen);if(_selectionDragMode==SelectionDragMode.Rotate){var a=Math.Atan2(_transformStart.Y-_transformPivot.Y,_transformStart.X-_transformPivot.X);var b=Math.Atan2(current.Y-_transformPivot.Y,current.X-_transformPivot.X);_transformPreview=Matrix2D.Around(_transformPivot,Matrix2D.Rotation((b-a)*180/Math.PI));}else{var start=_transformStart-_transformPivot;var now=current-_transformPivot;var sx=Math.Abs(start.X)<1e-9?1:now.X/start.X;var sy=Math.Abs(start.Y)<1e-9?1:now.Y/start.Y;if(_uniformScale){var factor=Math.Abs(sx)>=Math.Abs(sy)?sx:sy;sx=sy=factor;}if(Math.Abs(sx)>1e-4&&Math.Abs(sy)>1e-4)_transformPreview=Matrix2D.Around(_transformPivot,Matrix2D.Scale(sx,sy));}PointerStatusChanged?.Invoke(current,_viewport.Zoom);InvalidateVisual();return;}
        _cursorDocument = Snapped(Doc(screen)); _drawingSession.PointerMoved(_cursorDocument.Value); PointerStatusChanged?.Invoke(_cursorDocument.Value, _viewport.Zoom);
        if (_lastPan is { } last) { _viewport.PanBy(new(screen.X - last.X, screen.Y - last.Y)); _lastPan = screen; }
        if (_selectionStart is not null) _selectionEnd = _cursorDocument; if (_moveStart is not null) _moveCurrent = _cursorDocument;
        ReportInteraction(); InvalidateVisual();
    }
    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if(ToolpathPreviewMode){_lastOrbit=null;_lastPan=null;_previewRefineTimer.Stop();_previewRefineTimer.Start();e.Pointer.Capture(null);return;}
        if(ActiveTool==CanvasTool.Trim){e.Pointer.Capture(null);return;}
        if(_selectionDragMode!=SelectionDragMode.None){var description=_selectionDragMode==SelectionDragMode.Rotate?"Selection rotated":"Selection resized";SelectionTransformed?.Invoke(_transformPreview,description);_selectionDragMode=SelectionDragMode.None;_transformPreview=Matrix2D.Identity;e.Pointer.Capture(null);ReportInteraction();InvalidateVisual();return;}
        if(NodeEditMode&&_nodeEntity is not null&&_cursorDocument is{} nodePoint){if(_nodeStartPoint is{} nodeStart&&nodeStart.DistanceTo(nodePoint)>1e-7)NodeMoved?.Invoke(_nodeEntity,_nodeIndex,nodePoint);_nodeEntity=null;_nodeIndex=-1;_nodeStartPoint=null;e.Pointer.Capture(null);InvalidateVisual();return;}
        if (ActiveTool != CanvasTool.Select && _cursorDocument is { } drawingEnd) { var entity = _drawingSession.PointerReleased(drawingEnd, Document.ActiveLayerId); if (entity is not null) EntityCreated?.Invoke(entity); }
        if(_moveStart is { } moveStart&&_moveCurrent is { } moveEnd&&moveStart.DistanceTo(moveEnd)>1e-6){SelectionMoved?.Invoke(moveEnd-moveStart);_selectionStart=null;_selectionEnd=null;}
        if (_selectionStart is { } start && _selectionEnd is { } end)
        {
            if (start.DistanceTo(end) < 2 / _viewport.Zoom)
            {
                var hits=EntityHitTester.HitTestAll(Document.Entities.Where(IsEditable),end,6/_viewport.Zoom);if(_lastSelectionClick is{} previous&&previous.DistanceTo(end)<3/_viewport.Zoom&&hits.Count>1)_hitCycleIndex=(_hitCycleIndex+1)%hits.Count;else _hitCycleIndex=0;_lastSelectionClick=end;var hit=hits.Count==0?null:hits[_hitCycleIndex%hits.Count];
                if (hit is null) { if (!_selectionAdditive) Selection.Clear(); } else {var ids=hit.GroupId is{} group?Document.Entities.Where(x=>x.GroupId==group&&IsEditable(x)).Select(x=>x.Id).ToArray():[hit.Id];if(_selectionAdditive)foreach(var id in ids)Selection.Toggle(id);else Selection.Set(ids);}
            }
            else
            {
                var area = new BoundingBox(Math.Min(start.X,end.X), Math.Min(start.Y,end.Y), Math.Max(start.X,end.X), Math.Max(start.Y,end.Y)); var ids = EntityHitTester.Inside(Document.Entities.Where(IsEditable), area).ToArray();
                if (!_selectionAdditive) Selection.Set(ids); else foreach (var id in ids) Selection.Add(id);
            }
            SelectionChanged?.Invoke();
        }
        _selectionStart = null; _selectionEnd = null; _moveStart = null; _moveCurrent = null; _lastPan = null; e.Pointer.Capture(null); ReportInteraction(); InvalidateVisual();
    }
    private bool TryStartSelectionTransform(Point screen,KeyModifiers modifiers)
    {
        if(Selection.Count==0||SelectedBounds() is not{} bounds)return false;var corners=new[]{new Point2D(bounds.MinX,bounds.MinY),new Point2D(bounds.MaxX,bounds.MinY),new Point2D(bounds.MaxX,bounds.MaxY),new Point2D(bounds.MinX,bounds.MaxY)};var cornerScreens=corners.Select(Screen).ToArray();var corner=Enumerable.Range(0,4).OrderBy(i=>Math.Sqrt(Math.Pow(screen.X-cornerScreens[i].X,2)+Math.Pow(screen.Y-cornerScreens[i].Y,2))).First();if(Math.Sqrt(Math.Pow(screen.X-cornerScreens[corner].X,2)+Math.Pow(screen.Y-cornerScreens[corner].Y,2))<=11){_selectionDragMode=SelectionDragMode.Scale;_transformStart=corners[corner];_transformPivot=corners[(corner+2)%4];_uniformScale=!modifiers.HasFlag(KeyModifiers.Alt);_transformPreview=Matrix2D.Identity;return true;}var topMid=new Point((cornerScreens[2].X+cornerScreens[3].X)/2,(cornerScreens[2].Y+cornerScreens[3].Y)/2);var center=new Point(cornerScreens.Average(x=>x.X),cornerScreens.Average(x=>x.Y));var direction=new Vector(topMid.X-center.X,topMid.Y-center.Y);var length=direction.Length;if(length>1e-6){var rotate=new Point(topMid.X+direction.X/length*28,topMid.Y+direction.Y/length*28);if(Math.Sqrt(Math.Pow(screen.X-rotate.X,2)+Math.Pow(screen.Y-rotate.Y,2))<=12){_selectionDragMode=SelectionDragMode.Rotate;_transformPivot=new((bounds.MinX+bounds.MaxX)/2,(bounds.MinY+bounds.MaxY)/2);_transformStart=Doc(screen);_transformPreview=Matrix2D.Identity;return true;}}return false;
    }
    private static double DistanceToEntity(Point2D point,CadEntity entity){var paths=entity is VectorPathEntity vector?vector.Figures.Select(f=>(IReadOnlyList<Point2D>)VectorPathGeometry.Flatten(VectorPathGeometry.Transform([f],vector.Transform),.01)).ToArray():[(IReadOnlyList<Point2D>)EntityGeometry.WorldPoints(entity)];var best=double.MaxValue;foreach(var path in paths)for(var i=1;i<path.Count;i++){var d=path[i]-path[i-1];var length=d.X*d.X+d.Y*d.Y;var t=length<1e-12?0:Math.Clamp(((point.X-path[i-1].X)*d.X+(point.Y-path[i-1].Y)*d.Y)/length,0,1);best=Math.Min(best,point.DistanceTo(new(path[i-1].X+d.X*t,path[i-1].Y+d.Y*t)));}return best;}
    private void UpdateTrimCandidate(Point2D point){var hits=EntityHitTester.HitTestAll(Document.Entities.Where(IsEditable),point,8/_viewport.Zoom);var target=hits.Select((entity,index)=>(Entity:entity,Index:index,Distance:DistanceToEntity(point,entity))).OrderBy(x=>x.Distance).ThenBy(x=>x.Index).FirstOrDefault().Entity;_trimCandidate=target is null?null:_trimService.FindCandidate(target,Document.Entities.Where(IsEditable),point,8/_viewport.Zoom);}
    private bool IsLeftPress(PointerPressedEventArgs e){var properties=e.GetCurrentPoint(this).Properties;return properties.IsLeftButtonPressed||properties.PointerUpdateKind==PointerUpdateKind.LeftButtonPressed;}
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        var screen=e.GetPosition(this);if(ToolpathPreviewMode){BeginInteractivePreview();_camera.ZoomBy(e.Delta.Y>0?1.15:1/1.15);InvalidateVisual();return;}_cursorScreen=screen;_viewport.SetZoom(_viewport.Zoom*(e.Delta.Y>0?1.15:1/1.15),new(screen.X,screen.Y),Bounds.Height);_cursorDocument=Snapped(Doc(screen));PointerStatusChanged?.Invoke(_cursorDocument.Value,_viewport.Zoom);ReportInteraction();InvalidateVisual();
    }
}
