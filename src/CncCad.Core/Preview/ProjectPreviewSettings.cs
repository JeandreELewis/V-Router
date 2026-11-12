namespace CncCad.Core.Preview;

public enum PreviewRenderStyle
{
    Technical,
    Material
}

public enum MaterialAppearancePreset
{
    Auto,
    Hardwood,
    Softwood,
    Plywood,
    Mdf
}

public enum GrainDirection
{
    AlongStockX,
    AlongStockY
}

public sealed record ProjectPreviewSettings
{
    public PreviewRenderStyle Style { get; init; } = PreviewRenderStyle.Technical;
    public MaterialAppearancePreset MaterialPreset { get; init; } = MaterialAppearancePreset.Auto;
    public GrainDirection GrainDirection { get; init; } = GrainDirection.AlongStockY;
    public string? TintHex { get; init; }

    public static ProjectPreviewSettings LegacyDefault { get; } = new();
    public static ProjectPreviewSettings NewWoodJobDefault { get; } = new()
    {
        Style = PreviewRenderStyle.Material,
        MaterialPreset = MaterialAppearancePreset.Hardwood,
        GrainDirection = GrainDirection.AlongStockY
    };

    public ProjectPreviewSettings Normalize()
    {
        var tint = NormalizeTint(TintHex);
        return this with { TintHex = tint };
    }

    public static string? NormalizeTint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var candidate = value.Trim();
        if (candidate.Length == 6) candidate = "#" + candidate;
        if (candidate.Length != 7 || candidate[0] != '#') return null;
        for (var index = 1; index < candidate.Length; index++)
            if (!Uri.IsHexDigit(candidate[index])) return null;
        return candidate.ToUpperInvariant();
    }

    public MaterialAppearancePreset ResolveMaterial(string? materialName, string? materialCategory)
    {
        if (MaterialPreset != MaterialAppearancePreset.Auto) return MaterialPreset;
        var value = $"{materialCategory} {materialName}".ToLowerInvariant();
        if (value.Contains("mdf")) return MaterialAppearancePreset.Mdf;
        if (value.Contains("ply")) return MaterialAppearancePreset.Plywood;
        if (value.Contains("soft") || value.Contains("pine") || value.Contains("cedar")) return MaterialAppearancePreset.Softwood;
        return MaterialAppearancePreset.Hardwood;
    }
}
