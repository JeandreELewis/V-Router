namespace CncCad.Core.Transforms;

public readonly record struct Point3D(double X, double Y, double Z);
public readonly record struct ProjectedPoint(double X, double Y, double Depth);
public enum CameraPreset { Custom, Isometric, Top, Bottom, Front, Back, Left, Right }

public sealed class OrbitCamera
{
    public double Yaw { get; private set; }
    public double Pitch { get; private set; }
    public double Zoom { get; private set; } = 1;
    public double PanX { get; private set; }
    public double PanY { get; private set; }
    public Point3D Target { get; private set; }
    public CameraPreset Preset { get; private set; } = CameraPreset.Isometric;

    public OrbitCamera() => SetPreset(CameraPreset.Isometric);
    public ProjectedPoint Project(Point3D point, double viewportWidth, double viewportHeight)
    {
        var x=point.X-Target.X; var y=point.Y-Target.Y; var z=point.Z-Target.Z; var c=Math.Cos(Yaw); var s=Math.Sin(Yaw);
        var horizontal=c*x-s*y; var away=s*x+c*y; var vertical=away*Math.Sin(Pitch)+z*Math.Cos(Pitch); var depth=away*Math.Cos(Pitch)-z*Math.Sin(Pitch);
        return new(viewportWidth/2+PanX+horizontal*Zoom, viewportHeight/2+PanY-vertical*Zoom, depth);
    }
    public void Orbit(double yawDelta, double pitchDelta) { Yaw=Normalize(Yaw+yawDelta); Pitch=Math.Clamp(Pitch+pitchDelta,.05,Math.PI/2-.01); Preset=CameraPreset.Custom; }
    public void Pan(double x, double y) { PanX+=x; PanY+=y; }
    public void ZoomBy(double factor) => Zoom=Math.Clamp(Zoom*factor,.02,500);
    public void SetPreset(CameraPreset preset)
    {
        Preset=preset; (Yaw,Pitch)=preset switch
        {
            CameraPreset.Top => (0,Math.PI/2-.01),
            CameraPreset.Bottom => (0,-Math.PI/2+.01),
            CameraPreset.Front => (0,.05),
            CameraPreset.Back => (Math.PI,.05),
            CameraPreset.Left => (-Math.PI/2,.05),
            CameraPreset.Right => (Math.PI/2,.05),
            _ => (-Math.PI/4,.62)
        };
    }
    public void Fit(double width, double height, double thickness, double safeZ, double viewportWidth, double viewportHeight, double padding=70)
    {
        Target=new(width/2,height/2,(safeZ-thickness)/2); PanX=PanY=0; Zoom=1;
        var points=new[]{new Point3D(0,0,-thickness),new(width,0,-thickness),new(0,height,-thickness),new(width,height,-thickness),new(0,0,safeZ),new(width,0,safeZ),new(0,height,safeZ),new(width,height,safeZ)}.Select(p=>Project(p,0,0)).ToArray();
        var spanX=Math.Max(1,points.Max(p=>p.X)-points.Min(p=>p.X)); var spanY=Math.Max(1,points.Max(p=>p.Y)-points.Min(p=>p.Y)); Zoom=Math.Max(.02,Math.Min((viewportWidth-padding*2)/spanX,(viewportHeight-padding*2)/spanY));
    }
    private static double Normalize(double value) { while(value>Math.PI)value-=Math.PI*2; while(value<=-Math.PI)value+=Math.PI*2; return value; }
}
