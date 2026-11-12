using CncCad.Cam.Toolpaths;
using CncCad.Core.Geometry;

namespace CncCad.Cam.Operations;

public static class OperationNumerics
{
    public static IReadOnlyList<OperationValidationError> ValidateDefinition(CamOperationDefinition operation)
    {
        var errors = new List<OperationValidationError>();
        var values = new List<(string Name, double Value)>
        {
            ("tool diameter", operation.Tool.DiameterMm),
            ("tool feed", operation.Tool.FeedRateMmPerMin),
            ("tool plunge", operation.Tool.PlungeRateMmPerMin),
            ("tool step-down", operation.Tool.StepDownMm)
        };
        AddOptional(values, "tool cutting length", operation.Tool.CuttingLengthMm);
        AddOptional(values, "tool overall length", operation.Tool.OverallLengthMm);
        AddOptional(values, "tool shank diameter", operation.Tool.ShankDiameterMm);
        AddOptional(values, "tool stick-out", operation.Tool.StickOutMm);
        AddOptional(values, "V-bit angle", operation.Tool.VBitAngleDegrees);
        AddOptional(values, "V-bit tip diameter", operation.Tool.TipDiameterMm);

        switch (operation)
        {
            case ProfileOperationDefinition value:
                values.AddRange([
                    ("start depth", value.StartDepthMm), ("cut depth", value.CutDepthMm), ("pass depth", value.PassDepthMm),
                    ("allowance", value.AllowanceMm), ("start point", value.StartPointFraction), ("lead-in", value.LeadInLengthMm),
                    ("lead-out", value.LeadOutLengthMm), ("ramp length", value.RampLengthMm), ("onion skin", value.OnionSkinMm),
                    ("finish allowance", value.FinishAllowanceMm), ("safe Z", value.SafeZMm), ("feed", value.FeedRateMmPerMin),
                    ("plunge", value.PlungeRateMmPerMin), ("tab width", value.Tabs.WidthMm),
                    ("tab remaining height", value.Tabs.RemainingHeightMm), ("tab ramp length", value.Tabs.RampLengthMm),
                    ("tab exclusion", value.Tabs.ExclusionDistanceMm)
                ]);
                AddOptional(values, "last pass depth", value.LastPassDepthMm);
                foreach (var placement in value.Tabs.ManualPlacements) values.Add(("manual tab position", placement.NormalizedDistance));
                break;
            case PocketOperationDefinition value:
                values.AddRange([
                    ("start depth", value.StartDepthMm), ("cut depth", value.CutDepthMm), ("pass depth", value.PassDepthMm),
                    ("step-over", value.StepOverMm), ("allowance", value.AllowanceMm), ("rough allowance", value.RoughAllowanceMm),
                    ("ramp length", value.RampLengthMm), ("safe Z", value.SafeZMm), ("feed", value.FeedRateMmPerMin),
                    ("plunge", value.PlungeRateMmPerMin)
                ]);
                break;
            case EngraveOperationDefinition value:
                values.AddRange([
                    ("start depth", value.StartDepthMm), ("cut depth", value.CutDepthMm), ("pass depth", value.PassDepthMm),
                    ("safe Z", value.SafeZMm), ("feed", value.FeedRateMmPerMin), ("plunge", value.PlungeRateMmPerMin)
                ]);
                break;
            case VCarveOperationDefinition value:
                values.AddRange([
                    ("maximum depth", value.MaximumDepthMm), ("offset step-over", value.OffsetStepOverMm),
                    ("safe Z", value.SafeZMm), ("feed", value.FeedRateMmPerMin), ("plunge", value.PlungeRateMmPerMin)
                ]);
                break;
            case DrillOperationDefinition value:
                values.AddRange([
                    ("cut depth", value.CutDepthMm), ("peck depth", value.PeckDepthMm), ("safe Z", value.SafeZMm),
                    ("retract Z", value.RetractZMm), ("dwell", value.DwellSeconds), ("plunge", value.PlungeRateMmPerMin)
                ]);
                break;
            case SurfaceOperationDefinition value:
                values.AddRange([
                    ("origin X", value.OriginXmm), ("origin Y", value.OriginYmm), ("width", value.WidthMm),
                    ("height", value.HeightMm), ("start depth", value.StartDepthMm), ("cut depth", value.CutDepthMm),
                    ("pass depth", value.PassDepthMm), ("step-over", value.StepOverMm), ("overtravel", value.OvertravelMm), ("safe Z", value.SafeZMm),
                    ("feed", value.FeedRateMmPerMin), ("plunge", value.PlungeRateMmPerMin)
                ]);
                break;
        }

        foreach (var (name, value) in values.Where(item => !double.IsFinite(item.Value)))
            errors.Add(new("non-finite", $"{operation.Name}: {name} must be a finite number."));
        return errors;
    }

    public static IReadOnlyList<OperationValidationError> ValidateMoves(ToolpathOperation operation)
    {
        var errors = new List<OperationValidationError>();
        for (var index = 0; index < operation.Moves.Count; index++)
        {
            var valid = operation.Moves[index] switch
            {
                RapidMove move => Finite(move.End) && double.IsFinite(move.Z),
                LinearMove move => Finite(move.End) && double.IsFinite(move.Z) && double.IsFinite(move.FeedRate) && move.FeedRate > 0,
                ArcMove move => Finite(move.End) && Finite(move.Center) && double.IsFinite(move.Z) && double.IsFinite(move.FeedRate) && move.FeedRate > 0,
                DwellMove move => double.IsFinite(move.Seconds) && move.Seconds > 0,
                SpindleStart move => move.Rpm > 0,
                ToolChange move => !string.IsNullOrWhiteSpace(move.ToolName),
                SpindleStop => true,
                _ => false
            };
            if (!valid) errors.Add(new("invalid-move", $"{operation.Name}: move {index + 1} contains invalid or non-finite machine data."));
        }
        return errors;
    }

    private static bool Finite(Point2D point) => double.IsFinite(point.X) && double.IsFinite(point.Y);
    private static void AddOptional(List<(string Name, double Value)> values, string name, double? value)
    {
        if (value.HasValue) values.Add((name, value.Value));
    }
}
