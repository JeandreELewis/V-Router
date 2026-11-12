namespace CncCad.PostProcessors.GCode;

public sealed record PostCertificationEvidence
{
    public required string PostId { get; init; }
    public required string ControllerOrParserVersion { get; init; }
    public required string MachineProfile { get; init; }
    public required string MetricParserReportSha256 { get; init; }
    public required string ImperialParserReportSha256 { get; init; }
    public bool ArcCoverage { get; init; }
    public bool DwellCoverage { get; init; }
    public bool ToolChangeCoverage { get; init; }
    public DateOnly AirCutDate { get; init; }
    public required string OperatorSignOff { get; init; }
    public required string PostDefinitionSha256 { get; init; }
    public IReadOnlyDictionary<string,string> ProofJobOutputSha256 { get; init; }=new Dictionary<string,string>();
}

public sealed record PostCertificationValidationResult(IReadOnlyList<string> Errors)
{
    public bool Success=>Errors.Count==0;
}

/// <summary>
/// The only supported route from CertificationPending to Certified. Evidence
/// remains reviewable data; post definitions never execute external scripts.
/// </summary>
public static class PostCertificationPolicy
{
    private static readonly string[] RequiredProofOutputs=
    [
        "cabinet-panel-mm","cabinet-panel-in",
        "sign-mm","sign-in",
        "nested-sheet-mm","nested-sheet-in"
    ];

    public static PostCertificationValidationResult Validate(PostDefinition definition,PostCertificationEvidence evidence,DateOnly? today=null)
    {
        ArgumentNullException.ThrowIfNull(definition);ArgumentNullException.ThrowIfNull(evidence);
        var errors=new List<string>();
        if(!definition.IsBuiltIn)errors.Add("Only immutable built-in post definitions can be certified.");
        if(!string.Equals(definition.Id,evidence.PostId,StringComparison.OrdinalIgnoreCase))errors.Add("Evidence post ID does not match the post definition.");
        Required(evidence.ControllerOrParserVersion,"Controller/parser version",errors);Required(evidence.MachineProfile,"Machine profile",errors);Required(evidence.OperatorSignOff,"Operator sign-off",errors);
        Hash(evidence.MetricParserReportSha256,"Metric parser report",errors);Hash(evidence.ImperialParserReportSha256,"Imperial parser report",errors);Hash(evidence.PostDefinitionSha256,"Post definition",errors);
        if(!evidence.ArcCoverage)errors.Add("Arc coverage evidence is required.");if(!evidence.DwellCoverage)errors.Add("Dwell coverage evidence is required.");if(!evidence.ToolChangeCoverage)errors.Add("Tool-change coverage evidence is required.");
        var current=today??DateOnly.FromDateTime(DateTime.UtcNow);if(evidence.AirCutDate==default||evidence.AirCutDate>current)errors.Add("A completed, non-future air-cut date is required.");
        foreach(var proof in RequiredProofOutputs)
        {
            if(evidence.ProofJobOutputSha256 is null||!evidence.ProofJobOutputSha256.TryGetValue(proof,out var hash))errors.Add($"Proof output '{proof}' is required.");
            else Hash(hash,$"Proof output '{proof}'",errors);
        }
        return new(errors);
    }

    public static PostDefinition Certify(PostDefinition definition,PostCertificationEvidence evidence,DateOnly? today=null)
    {
        var validation=Validate(definition,evidence,today);if(!validation.Success)throw new InvalidDataException("Post certification evidence is incomplete: "+string.Join(" ",validation.Errors));
        return definition with{CertificationStatus=PostCertificationStatus.Certified};
    }

    private static void Required(string? value,string label,List<string> errors){if(string.IsNullOrWhiteSpace(value))errors.Add($"{label} is required.");}
    private static void Hash(string? value,string label,List<string> errors){if(value is null||value.Length!=64||value.Any(character=>!char.IsAsciiHexDigit(character)))errors.Add($"{label} must have a SHA-256 hash.");}
}
