namespace CncCad.Core.Geometry;

/// <summary>
/// Defines the world-millimetre tolerances shared by document geometry, imports and CAM.
/// Dimensionless numerical guards (for example matrix determinants and curve parameters)
/// are deliberately kept separate from physical distances.
/// </summary>
public sealed record GeometryTolerancePolicy
{
    public const double DefaultEndpointEqualityMm = 1e-9;
    public const double DefaultMinimumLengthMm = 1e-9;
    public const double DefaultMinimumAreaMm2 = 1e-8;
    public const double DefaultTopologyMergeMm = 1e-8;
    public const double DefaultInteractiveMinimumLengthMm = 1e-6;
    public const double DefaultCurveFlatnessMm = 0.02;
    public const double DefaultImportApproximationMm = 0.05;
    public const double DefaultImportClosureMm = 0.001;
    public const double DefaultJoinToleranceMm = 0.05;
    public const double DefaultNumericEpsilon = 1e-12;
    public const double DefaultMatrixDeterminantEpsilon = 1e-12;
    public const int DefaultBooleanDecimalPrecision = 6;

    public double EndpointEqualityMm { get; init; } = DefaultEndpointEqualityMm;
    public double MinimumLengthMm { get; init; } = DefaultMinimumLengthMm;
    public double MinimumAreaMm2 { get; init; } = DefaultMinimumAreaMm2;
    public double TopologyMergeMm { get; init; } = DefaultTopologyMergeMm;
    public double InteractiveMinimumLengthMm { get; init; } = DefaultInteractiveMinimumLengthMm;
    public double CurveFlatnessMm { get; init; } = DefaultCurveFlatnessMm;
    public double ImportApproximationMm { get; init; } = DefaultImportApproximationMm;
    public double ImportClosureMm { get; init; } = DefaultImportClosureMm;
    public double JoinToleranceMm { get; init; } = DefaultJoinToleranceMm;
    public double NumericEpsilon { get; init; } = DefaultNumericEpsilon;
    public double MatrixDeterminantEpsilon { get; init; } = DefaultMatrixDeterminantEpsilon;
    public int BooleanDecimalPrecision { get; init; } = DefaultBooleanDecimalPrecision;

    public static GeometryTolerancePolicy Production { get; } = new();

    public IReadOnlyList<string> Validate()
    {
        var errors=new List<string>();
        Positive(EndpointEqualityMm,nameof(EndpointEqualityMm));Positive(MinimumLengthMm,nameof(MinimumLengthMm));Positive(MinimumAreaMm2,nameof(MinimumAreaMm2));Positive(TopologyMergeMm,nameof(TopologyMergeMm));Positive(InteractiveMinimumLengthMm,nameof(InteractiveMinimumLengthMm));Positive(CurveFlatnessMm,nameof(CurveFlatnessMm));Positive(ImportApproximationMm,nameof(ImportApproximationMm));Positive(ImportClosureMm,nameof(ImportClosureMm));Positive(JoinToleranceMm,nameof(JoinToleranceMm));Positive(NumericEpsilon,nameof(NumericEpsilon));Positive(MatrixDeterminantEpsilon,nameof(MatrixDeterminantEpsilon));
        if(BooleanDecimalPrecision is<0 or>8)errors.Add($"{nameof(BooleanDecimalPrecision)} must be between 0 and 8.");
        if(EndpointEqualityMm>InteractiveMinimumLengthMm)errors.Add($"{nameof(EndpointEqualityMm)} cannot exceed {nameof(InteractiveMinimumLengthMm)}.");
        if(MinimumLengthMm>InteractiveMinimumLengthMm)errors.Add($"{nameof(MinimumLengthMm)} cannot exceed {nameof(InteractiveMinimumLengthMm)}.");
        if(TopologyMergeMm<EndpointEqualityMm||TopologyMergeMm>InteractiveMinimumLengthMm)errors.Add($"{nameof(TopologyMergeMm)} must be between endpoint equality and the interactive minimum length.");
        if(ImportClosureMm<TopologyMergeMm)errors.Add($"{nameof(ImportClosureMm)} cannot be smaller than {nameof(TopologyMergeMm)}.");
        return errors;
        void Positive(double value,string name){if(!double.IsFinite(value)||value<=0)errors.Add($"{name} must be positive and finite.");}
    }

    public bool EndpointsEqual(Point2D first,Point2D second)=>GeometryValidation.IsFinite(first)&&GeometryValidation.IsFinite(second)&&first.DistanceTo(second)<=EndpointEqualityMm;
    public bool IsUsableLength(double lengthMm)=>double.IsFinite(lengthMm)&&lengthMm>MinimumLengthMm;
    public bool IsUsableArea(double areaMm2)=>double.IsFinite(areaMm2)&&Math.Abs(areaMm2)>MinimumAreaMm2;
}
