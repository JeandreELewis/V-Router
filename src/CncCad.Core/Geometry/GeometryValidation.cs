namespace CncCad.Core.Geometry;

public static class GeometryValidation
{
    public const double CoordinateToleranceMm = GeometryTolerancePolicy.DefaultEndpointEqualityMm;

    public static bool IsFinite(Point2D point)=>double.IsFinite(point.X)&&double.IsFinite(point.Y);
    public static bool IsFinite(Vector2D vector)=>double.IsFinite(vector.X)&&double.IsFinite(vector.Y);
    public static bool IsFinite(Matrix2D matrix)=>double.IsFinite(matrix.M11)&&double.IsFinite(matrix.M12)&&double.IsFinite(matrix.M21)&&double.IsFinite(matrix.M22)&&double.IsFinite(matrix.OffsetX)&&double.IsFinite(matrix.OffsetY);
    public static bool IsInvertible(Matrix2D matrix)=>IsFinite(matrix)&&Math.Abs(matrix.M11*matrix.M22-matrix.M12*matrix.M21)>GeometryTolerancePolicy.DefaultMatrixDeterminantEpsilon;
    public static bool IsIdentity(Matrix2D matrix,double tolerance=CoordinateToleranceMm)=>Math.Abs(matrix.M11-1)<=tolerance&&Math.Abs(matrix.M12)<=tolerance&&Math.Abs(matrix.M21)<=tolerance&&Math.Abs(matrix.M22-1)<=tolerance&&Math.Abs(matrix.OffsetX)<=tolerance&&Math.Abs(matrix.OffsetY)<=tolerance;

    public static void RequireFinite(Vector2D vector,string parameter)
    {
        if(!IsFinite(vector))throw new ArgumentException($"{parameter} must contain finite coordinates.",parameter);
    }

    public static void RequireInvertible(Matrix2D matrix,string parameter)
    {
        if(!IsFinite(matrix))throw new ArgumentException($"{parameter} must contain finite values.",parameter);
        if(!IsInvertible(matrix))throw new ArgumentException($"{parameter} must be invertible.",parameter);
    }
}
