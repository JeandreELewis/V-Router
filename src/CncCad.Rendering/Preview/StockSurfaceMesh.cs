using CncCad.Core.Transforms;
using CncCad.Simulation.Stock;

namespace CncCad.Rendering.Preview;

public enum StockSurfaceKind { Top, Side, Bottom }

public readonly record struct SurfaceNormal(double X, double Y, double Z)
{
    public SurfaceNormal Normalize()
    {
        var length = Math.Sqrt(X * X + Y * Y + Z * Z);
        return length <= 1e-12 ? new(0, 0, 1) : new(X / length, Y / length, Z / length);
    }
}

public readonly record struct StockSurfaceVertex(Point3D Position, SurfaceNormal Normal);
public readonly record struct StockSurfaceTriangle(StockSurfaceVertex A, StockSurfaceVertex B, StockSurfaceVertex C, StockSurfaceKind Kind);

public sealed class StockSurfaceMesh
{
    public required IReadOnlyList<StockSurfaceTriangle> Triangles { get; init; }
    public required double WidthMm { get; init; }
    public required double HeightMm { get; init; }
    public required double ThicknessMm { get; init; }
    public required int Columns { get; init; }
    public required int Rows { get; init; }
}

public static class StockSurfaceMeshBuilder
{
    public static StockSurfaceMesh Build(StockHeightfield? field, double widthMm, double heightMm, double thicknessMm, int maximumSegmentsPerAxis)
    {
        if (widthMm <= 0 || heightMm <= 0 || thicknessMm <= 0) throw new ArgumentOutOfRangeException(nameof(widthMm));
        maximumSegmentsPerAxis = Math.Clamp(maximumSegmentsPerAxis, 8, 768);
        var sourceColumns = field?.Columns ?? 1;
        var sourceRows = field?.Rows ?? 1;
        var columns = Math.Max(1, Math.Min(sourceColumns, maximumSegmentsPerAxis));
        var rows = Math.Max(1, Math.Min(sourceRows, maximumSegmentsPerAxis));
        var vertices = new StockSurfaceVertex[(columns + 1) * (rows + 1)];
        var dx = widthMm / columns;
        var dy = heightMm / rows;

        double Height(double x, double y)
        {
            if (field is null) return 0;
            var column = Math.Clamp((int)Math.Floor(x / field.CellSizeMm), 0, field.Columns - 1);
            var row = Math.Clamp((int)Math.Floor(y / field.CellSizeMm), 0, field.Rows - 1);
            return field[column, row];
        }

        SurfaceNormal Normal(double x, double y)
        {
            var sampleX = Math.Max(dx, field?.CellSizeMm ?? dx);
            var sampleY = Math.Max(dy, field?.CellSizeMm ?? dy);
            var left = Height(Math.Max(0, x - sampleX), y);
            var right = Height(Math.Min(widthMm, x + sampleX), y);
            var down = Height(x, Math.Max(0, y - sampleY));
            var up = Height(x, Math.Min(heightMm, y + sampleY));
            var spanX = Math.Max(sampleX, Math.Min(widthMm, x + sampleX) - Math.Max(0, x - sampleX));
            var spanY = Math.Max(sampleY, Math.Min(heightMm, y + sampleY) - Math.Max(0, y - sampleY));
            return new SurfaceNormal(-(right - left) / spanX, -(up - down) / spanY, 1).Normalize();
        }

        for (var row = 0; row <= rows; row++)
        for (var column = 0; column <= columns; column++)
        {
            var x = column == columns ? widthMm : column * dx;
            var y = row == rows ? heightMm : row * dy;
            vertices[row * (columns + 1) + column] = new(new(x, y, Height(x, y)), Normal(x, y));
        }

        var triangles = new List<StockSurfaceTriangle>(columns * rows * 2 + (columns + rows) * 4 + 2);
        StockSurfaceVertex V(int column, int row) => vertices[row * (columns + 1) + column];
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            var a = V(column, row); var b = V(column + 1, row); var c = V(column + 1, row + 1); var d = V(column, row + 1);
            triangles.Add(new(a, b, c, StockSurfaceKind.Top));
            triangles.Add(new(a, c, d, StockSurfaceKind.Top));
        }

        AddSide(Enumerable.Range(0, columns + 1).Select(column => V(column, 0)), new(0, -1, 0));
        AddSide(Enumerable.Range(0, columns + 1).Reverse().Select(column => V(column, rows)), new(0, 1, 0));
        AddSide(Enumerable.Range(0, rows + 1).Reverse().Select(row => V(0, row)), new(-1, 0, 0));
        AddSide(Enumerable.Range(0, rows + 1).Select(row => V(columns, row)), new(1, 0, 0));

        var bottomNormal = new SurfaceNormal(0, 0, -1);
        var ba = new StockSurfaceVertex(new(0, 0, -thicknessMm), bottomNormal);
        var bb = new StockSurfaceVertex(new(widthMm, 0, -thicknessMm), bottomNormal);
        var bc = new StockSurfaceVertex(new(widthMm, heightMm, -thicknessMm), bottomNormal);
        var bd = new StockSurfaceVertex(new(0, heightMm, -thicknessMm), bottomNormal);
        triangles.Add(new(ba, bc, bb, StockSurfaceKind.Bottom));
        triangles.Add(new(ba, bd, bc, StockSurfaceKind.Bottom));

        return new() { Triangles = triangles, WidthMm = widthMm, HeightMm = heightMm, ThicknessMm = thicknessMm, Columns = columns, Rows = rows };

        void AddSide(IEnumerable<StockSurfaceVertex> edge, SurfaceNormal normal)
        {
            var points = edge.ToArray();
            for (var index = 1; index < points.Length; index++)
            {
                var topA = points[index - 1] with { Normal = normal };
                var topB = points[index] with { Normal = normal };
                var lowA = new StockSurfaceVertex(topA.Position with { Z = -thicknessMm }, normal);
                var lowB = new StockSurfaceVertex(topB.Position with { Z = -thicknessMm }, normal);
                triangles.Add(new(topA, lowA, lowB, StockSurfaceKind.Side));
                triangles.Add(new(topA, lowB, topB, StockSurfaceKind.Side));
            }
        }
    }
}
