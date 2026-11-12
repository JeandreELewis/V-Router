using CncCad.Core.Preview;
using CncCad.Core.Transforms;

namespace CncCad.Rendering.Preview;

public readonly record struct PreviewCameraState(double Yaw, double Pitch, double Zoom, double PanX, double PanY, Point3D Target)
{
    public static PreviewCameraState From(OrbitCamera camera) => new(camera.Yaw, camera.Pitch, camera.Zoom, camera.PanX, camera.PanY, camera.Target);

    public ProjectedPoint Project(Point3D point, double viewportWidth, double viewportHeight)
    {
        var x = point.X - Target.X; var y = point.Y - Target.Y; var z = point.Z - Target.Z;
        var cosine = Math.Cos(Yaw); var sine = Math.Sin(Yaw);
        var horizontal = cosine * x - sine * y;
        var away = sine * x + cosine * y;
        var vertical = away * Math.Sin(Pitch) + z * Math.Cos(Pitch);
        var depth = away * Math.Cos(Pitch) - z * Math.Sin(Pitch);
        return new(viewportWidth / 2 + PanX + horizontal * Zoom, viewportHeight / 2 + PanY - vertical * Zoom, depth);
    }
}

public sealed class PreviewRaster
{
    public PreviewRaster(int width, int height, byte[] bgraPixels) { Width = width; Height = height; BgraPixels = bgraPixels; }
    public int Width { get; }
    public int Height { get; }
    public byte[] BgraPixels { get; }
    public (byte R, byte G, byte B, byte A) Pixel(int x, int y)
    {
        var index = (y * Width + x) * 4;
        return (BgraPixels[index + 2], BgraPixels[index + 1], BgraPixels[index], BgraPixels[index + 3]);
    }
}

public sealed class StockPreviewRenderer
{
    private readonly record struct ScreenVertex(double X, double Y, double Depth, Point3D World, SurfaceNormal Normal);
    private readonly record struct Rgb(double R, double G, double B)
    {
        public Rgb Multiply(double value) => new(R * value, G * value, B * value);
        public Rgb Blend(Rgb other, double amount) => new(R + (other.R - R) * amount, G + (other.G - G) * amount, B + (other.B - B) * amount);
    }

    public PreviewRaster Render(StockSurfaceMesh mesh, PreviewCameraState camera, ProjectPreviewSettings settings, string? materialName, string? materialCategory, int width, int height, double projectionWidth, double projectionHeight, CancellationToken cancellationToken = default)
    {
        width = Math.Clamp(width, 32, 2400); height = Math.Clamp(height, 32, 1800);
        projectionWidth = Math.Max(1, projectionWidth); projectionHeight = Math.Max(1, projectionHeight);
        var pixels = new byte[width * height * 4];
        var depthBuffer = new double[width * height];
        Array.Fill(depthBuffer, double.PositiveInfinity);
        var scaleX = width / projectionWidth; var scaleY = height / projectionHeight;
        var resolved = settings.ResolveMaterial(materialName, materialCategory);
        var tint = ParseTint(settings.TintHex);
        var triangleIndex = 0;

        foreach (var triangle in mesh.Triangles)
        {
            if ((triangleIndex++ & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            var a = Project(triangle.A); var b = Project(triangle.B); var c = Project(triangle.C);
            Rasterize(a, b, c, triangle.Kind);
        }
        return new(width, height, pixels);

        ScreenVertex Project(StockSurfaceVertex vertex)
        {
            var projected = camera.Project(vertex.Position, projectionWidth, projectionHeight);
            return new(projected.X * scaleX, projected.Y * scaleY, projected.Depth, vertex.Position, vertex.Normal);
        }

        void Rasterize(ScreenVertex a, ScreenVertex b, ScreenVertex c, StockSurfaceKind kind)
        {
            var area = Edge(a.X, a.Y, b.X, b.Y, c.X, c.Y);
            if (Math.Abs(area) < 1e-8) return;
            var minX = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X))));
            var maxX = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
            var minY = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))));
            var maxY = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
            if (minX > maxX || minY > maxY) return;
            for (var y = minY; y <= maxY; y++)
            {
                if ((y & 31) == 0) cancellationToken.ThrowIfCancellationRequested();
                for (var x = minX; x <= maxX; x++)
                {
                    var px = x + .5; var py = y + .5;
                    var wa = Edge(b.X, b.Y, c.X, c.Y, px, py) / area;
                    var wb = Edge(c.X, c.Y, a.X, a.Y, px, py) / area;
                    var wc = 1 - wa - wb;
                    if (wa < -1e-8 || wb < -1e-8 || wc < -1e-8) continue;
                    var depth = a.Depth * wa + b.Depth * wb + c.Depth * wc;
                    var pixelIndex = y * width + x;
                    if (depth >= depthBuffer[pixelIndex]) continue;
                    depthBuffer[pixelIndex] = depth;
                    var world = new Point3D(a.World.X * wa + b.World.X * wb + c.World.X * wc, a.World.Y * wa + b.World.Y * wb + c.World.Y * wc, a.World.Z * wa + b.World.Z * wb + c.World.Z * wc);
                    var normal = new SurfaceNormal(a.Normal.X * wa + b.Normal.X * wb + c.Normal.X * wc, a.Normal.Y * wa + b.Normal.Y * wb + c.Normal.Y * wc, a.Normal.Z * wa + b.Normal.Z * wb + c.Normal.Z * wc).Normalize();
                    var color = Shade(world, normal, kind);
                    var offset = pixelIndex * 4;
                    pixels[offset] = ToByte(color.B); pixels[offset + 1] = ToByte(color.G); pixels[offset + 2] = ToByte(color.R); pixels[offset + 3] = 255;
                }
            }
        }

        Rgb Shade(Point3D world, SurfaceNormal normal, StockSurfaceKind kind)
        {
            var technical = settings.Style == PreviewRenderStyle.Technical;
            var baseColor = technical ? new Rgb(79, 94, 104) : MaterialColor(resolved, world, kind, settings.GrainDirection, mesh.ThicknessMm);
            if (!technical && tint is { } tintColor) baseColor = baseColor.Blend(tintColor, .42);
            var light = new SurfaceNormal(-.42, -.35, .84).Normalize();
            var diffuse = Math.Max(0, normal.X * light.X + normal.Y * light.Y + normal.Z * light.Z);
            var edgeLight = Math.Max(0, normal.Z);
            var lighting = .34 + diffuse * .58 + Math.Pow(Math.Max(0, diffuse), 18) * .13 + edgeLight * .04;
            if (kind == StockSurfaceKind.Side) lighting *= .82;
            if (kind == StockSurfaceKind.Bottom) lighting *= .58;
            var depthDarkening = 1 - Math.Min(.16, Math.Max(0, -world.Z) / Math.Max(1, mesh.ThicknessMm) * .16);
            return baseColor.Multiply(lighting * depthDarkening);
        }
    }

    private static Rgb MaterialColor(MaterialAppearancePreset preset, Point3D world, StockSurfaceKind kind, GrainDirection direction, double thickness)
    {
        var along = direction == GrainDirection.AlongStockY ? world.Y : world.X;
        var across = direction == GrainDirection.AlongStockY ? world.X : world.Y;
        var warp = Math.Sin(along * .008) * 1.6 + SmoothNoise(along * .014, across * .021) * 2.2;
        var fine = SmoothNoise(along * .12, across * .18) - .5;
        return preset switch
        {
            MaterialAppearancePreset.Softwood => Vary(new(222, 183, 117), .86 + .14 * Wave(across * .035 + warp) + fine * .045),
            MaterialAppearancePreset.Plywood when kind == StockSurfaceKind.Side => Vary(new(190, 143, 86), .82 + .16 * Wave((-world.Z / Math.Max(1, thickness)) * Math.PI * 18)),
            MaterialAppearancePreset.Plywood => Vary(new(204, 161, 103), .9 + .08 * Wave(across * .06 + warp * .7) + fine * .035),
            MaterialAppearancePreset.Mdf => Vary(new(177, 143, 101), .91 + (SmoothNoise(world.X * .42, world.Y * .42) - .5) * .09),
            _ => Vary(new(203, 154, 99), .86 + .12 * Wave(across * .075 + warp) + fine * .05)
        };
    }

    private static Rgb Vary(Rgb color, double factor) => color.Multiply(Math.Clamp(factor, .68, 1.12));
    private static double Wave(double value) => .5 + .5 * Math.Sin(value);
    private static double SmoothNoise(double x, double y)
    {
        var ix = (int)Math.Floor(x); var iy = (int)Math.Floor(y); var fx = x - ix; var fy = y - iy;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        var a = Hash(ix, iy); var b = Hash(ix + 1, iy); var c = Hash(ix, iy + 1); var d = Hash(ix + 1, iy + 1);
        return (a + (b - a) * fx) + ((c + (d - c) * fx) - (a + (b - a) * fx)) * fy;
    }
    private static double Hash(int x, int y)
    {
        unchecked
        {
            var value = (uint)(x * 374761393 + y * 668265263);
            value = (value ^ (value >> 13)) * 1274126177;
            return (value ^ (value >> 16)) / (double)uint.MaxValue;
        }
    }
    private static double Edge(double ax, double ay, double bx, double by, double px, double py) => (px - ax) * (by - ay) - (py - ay) * (bx - ax);
    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
    private static Rgb? ParseTint(string? value)
    {
        var normalized = ProjectPreviewSettings.NormalizeTint(value);
        if (normalized is null) return null;
        return new(Convert.ToByte(normalized.Substring(1, 2), 16), Convert.ToByte(normalized.Substring(3, 2), 16), Convert.ToByte(normalized.Substring(5, 2), 16));
    }
}
