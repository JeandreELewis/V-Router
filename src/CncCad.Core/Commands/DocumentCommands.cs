using CncCad.Core.Documents;

namespace CncCad.Core.Commands;

public sealed class SetJobSettingsCommand:IUndoableCommand,IJobSettingsAffectingCommand
{
    private readonly CadDocument _document;
    private readonly JobSettings _before;
    private readonly JobSettings _after;

    public SetJobSettingsCommand(CadDocument document,JobSettings settings)
    {
        ArgumentNullException.ThrowIfNull(document);
        Validate(settings);
        _document=document;
        _before=Copy(document.Job);
        _after=Copy(settings);
    }

    public string Description=>"Edit job setup";
    public void Execute()=>_document.Job=Copy(_after);
    public void Undo()=>_document.Job=Copy(_before);

    public static bool Equivalent(JobSettings left,JobSettings right)=>
        left.Name==right.Name&&left.WidthMm==right.WidthMm&&left.HeightMm==right.HeightMm&&left.ThicknessMm==right.ThicknessMm&&
        left.DisplayUnit==right.DisplayUnit&&left.Origin==right.Origin&&left.OriginOffsetXmm==right.OriginOffsetXmm&&
        left.OriginOffsetYmm==right.OriginOffsetYmm&&left.ZZero==right.ZZero&&left.SafeZMm==right.SafeZMm;

    private static JobSettings Copy(JobSettings value)=>new()
    {
        Name=value.Name,WidthMm=value.WidthMm,HeightMm=value.HeightMm,ThicknessMm=value.ThicknessMm,
        DisplayUnit=value.DisplayUnit,Origin=value.Origin,OriginOffsetXmm=value.OriginOffsetXmm,
        OriginOffsetYmm=value.OriginOffsetYmm,ZZero=value.ZZero,SafeZMm=value.SafeZMm
    };

    private static void Validate(JobSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if(string.IsNullOrWhiteSpace(value.Name))throw new ArgumentException("Job name is required.",nameof(value));
        if(!double.IsFinite(value.WidthMm)||!double.IsFinite(value.HeightMm)||!double.IsFinite(value.ThicknessMm)||value.WidthMm<=0||value.HeightMm<=0||value.ThicknessMm<=0)throw new ArgumentException("Material dimensions must be positive finite values.",nameof(value));
        if(!double.IsFinite(value.SafeZMm)||value.SafeZMm<=0)throw new ArgumentException("Safe Z must be a positive finite value.",nameof(value));
        if(!double.IsFinite(value.OriginOffsetXmm)||!double.IsFinite(value.OriginOffsetYmm))throw new ArgumentException("Origin offsets must be finite values.",nameof(value));
        if(!Enum.IsDefined(value.DisplayUnit)||!Enum.IsDefined(value.Origin)||!Enum.IsDefined(value.ZZero))throw new ArgumentException("Job setup contains an unsupported option.",nameof(value));
    }
}
