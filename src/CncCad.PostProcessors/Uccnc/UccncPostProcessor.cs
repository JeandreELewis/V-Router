using System.Globalization;
using System.Text;
using CncCad.Cam.Safety;
using CncCad.Cam.Operations;
using CncCad.Cam.Toolpaths;
using CncCad.Core.Documents;
using CncCad.Core.Geometry;
using CncCad.PostProcessors.GCode;
using CncCad.PostProcessors.Models;

namespace CncCad.PostProcessors.Uccnc;

public sealed class UccncPostProcessor() : DeclarativeGCodePostProcessor(CertifiedPostDefinitions.Uccnc);

public static class CertifiedPostDefinitions
{
    private const PostCapability Standard=PostCapability.Arcs|PostCapability.Dwell|PostCapability.ManualToolChange|PostCapability.AutomaticToolChange;
    // Built-in definitions remain certification-pending until parser evidence and a documented air-cut are attached.
    public static PostDefinition Uccnc {get;}=Definition("uccnc","UCCNC",ControllerFamily.Uccnc,".tap",PostToolChangeBehavior.AutomaticM6,Standard,PostCommentStyle.Parentheses,["G90","G17","G91.1","G94","G40","G49","G80"]);
    public static PostDefinition Mach3 {get;}=Definition("mach3","Mach3",ControllerFamily.Mach3,".tap",PostToolChangeBehavior.AutomaticM6,Standard,PostCommentStyle.Parentheses,["G90","G17","G91.1","G94","G40","G49","G80"]);
    public static PostDefinition Mach4 {get;}=Definition("mach4","Mach4",ControllerFamily.Mach4,".tap",PostToolChangeBehavior.AutomaticM6,Standard,PostCommentStyle.Parentheses,["G90","G17","G91.1","G94","G40","G49","G80"]);
    public static PostDefinition Grbl {get;}=Definition("grbl","GRBL",ControllerFamily.Grbl,".gcode",PostToolChangeBehavior.Unsupported,PostCapability.Arcs|PostCapability.Dwell,PostCommentStyle.Semicolon,["G90","G17","G94","G40"]);
    public static PostDefinition LinuxCnc {get;}=Definition("linuxcnc","LinuxCNC",ControllerFamily.LinuxCnc,".ngc",PostToolChangeBehavior.ManualPause,PostCapability.Arcs|PostCapability.Dwell|PostCapability.ManualToolChange,PostCommentStyle.Parentheses,["G90","G17","G91.1","G94","G40","G49","G80","G64 P0.01"]);
    public static IReadOnlyList<PostDefinition> All {get;}=[Uccnc,Mach3,Mach4,Grbl,LinuxCnc];
    private static PostDefinition Definition(string id,string name,ControllerFamily family,string extension,PostToolChangeBehavior toolChange,PostCapability capabilities,PostCommentStyle comments,IReadOnlyList<string> header)=>new(){Id=id,DisplayName=name,ControllerFamily=family,Extension=extension,ToolChangeBehavior=toolChange,Capabilities=capabilities,CommentStyle=comments,IsBuiltIn=true,CertificationStatus=PostCertificationStatus.CertificationPending,HeaderLines=header};
}

public class DeclarativeGCodePostProcessor(PostDefinition definition) : IPostProcessor
{
    public PostDefinition Definition { get; }=definition;

    public PostProcessResult Process(PostProcessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var diagnostics=Validate(request);
        if(diagnostics.Any(x=>x.Severity==PostDiagnosticSeverity.Error))return new([],diagnostics);
        var groups=request.ExportScope==PostExportScope.SeparateByTool?GroupConsecutiveByTool(request.Program):[(Tool:(int?)null,Program:request.Program)];
        var files=new List<GeneratedPostFile>();
        for(var index=0;index<groups.Count;index++)
        {
            var group=groups[index];
            var sequence=groups.Count>1?$"-{index+1:00}":"";
            var stem=SafeStem(request.Program.Name)+sequence+(group.Tool is{} tool?$"-T{tool}":"");
            try{files.Add(new(stem,Definition.Extension,Generate(group.Program,request.MachineSnapshot,request.OutputUnit,request.ExportScope),group.Tool));}
            catch(InvalidDataException exception){diagnostics.Add(new(PostDiagnosticSeverity.Error,"invalid-program",exception.Message));return new([],diagnostics);}
        }
        return new(files,diagnostics);
    }

    private List<PostProcessDiagnostic> Validate(PostProcessRequest request)
    {
        var result=new List<PostProcessDiagnostic>();
        if(request.PostDefinition.Id!=Definition.Id)result.Add(new(PostDiagnosticSeverity.Error,"post-mismatch","The request post definition does not match the selected postprocessor."));
        if(request.Program.Operations.Count==0)result.Add(new(PostDiagnosticSeverity.Error,"empty","The G-code program contains no toolpath operations."));
        foreach(var operation in request.Program.Operations)
            foreach(var error in OperationNumerics.ValidateMoves(operation))
                result.Add(new(PostDiagnosticSeverity.Error,error.Code,error.Message));
        foreach(var operation in request.Program.Operations)
        {
            var positioned=false;
            for(var index=0;index<operation.Moves.Count;index++)
            {
                switch(operation.Moves[index])
                {
                    case RapidMove or LinearMove:positioned=true;break;
                    case ArcMove when !positioned:result.Add(new(PostDiagnosticSeverity.Error,"arc-start",$"{operation.Name}: arc move {index+1} has no preceding positioned move."));break;
                    case ArcMove:positioned=true;break;
                    case SpindleStart spindle when spindle.Rpm>request.MachineSnapshot.MaximumSpindleRpm:result.Add(new(PostDiagnosticSeverity.Error,"spindle-limit",$"{operation.Name}: spindle speed exceeds the machine limit."));break;
                }
            }
        }
        if(request.MachineSnapshot.MaximumSpindleRpm<=0)result.Add(new(PostDiagnosticSeverity.Error,"machine","The machine spindle limit is invalid."));
        if(!double.IsFinite(request.MachineSnapshot.SafeZMm)||!double.IsFinite(request.MachineSnapshot.WorkZeroDocumentXmm)||!double.IsFinite(request.MachineSnapshot.WorkZeroDocumentYmm)||!double.IsFinite(request.MachineSnapshot.WorkZeroZOffsetMm))result.Add(new(PostDiagnosticSeverity.Error,"machine-numeric","Machine datum and safe-Z values must be finite."));
        if(request.MachineSnapshot.ParkEnabled&&(!double.IsFinite(request.MachineSnapshot.ParkXmm)||!double.IsFinite(request.MachineSnapshot.ParkYmm)))result.Add(new(PostDiagnosticSeverity.Error,"park-invalid","Enabled machine-coordinate parking requires finite X and Y values."));
        if(request.Program.Operations.SelectMany(x=>x.Moves).Any(x=>x is ArcMove)&&(!Definition.Capabilities.HasFlag(PostCapability.Arcs)||Definition.ArcMode==PostArcMode.Unsupported))result.Add(new(PostDiagnosticSeverity.Error,"unsupported-arcs",$"{Definition.DisplayName} does not support arc output."));
        if(request.Program.Operations.SelectMany(x=>x.Moves).Any(x=>x is DwellMove)&&!Definition.Capabilities.HasFlag(PostCapability.Dwell))result.Add(new(PostDiagnosticSeverity.Error,"unsupported-dwell",$"{Definition.DisplayName} does not support dwell commands."));
        var toolChanges=request.Program.Operations.SelectMany(x=>x.Moves).OfType<ToolChange>().ToArray();
        var tools=toolChanges.Select(x=>x.ToolNumber).Distinct().ToArray();
        if(tools.Any(x=>x is null or <=0))result.Add(new(PostDiagnosticSeverity.Error,"invalid-tool","Every exported tool requires a positive tool number."));
        var operationTools=request.Program.Operations.Select(operation=>(operation.ToolId,Change:operation.Moves.OfType<ToolChange>().FirstOrDefault())).Where(x=>x.Change?.ToolNumber is>0).ToArray();
        var reusedNumber=operationTools.GroupBy(x=>x.Change!.ToolNumber).FirstOrDefault(group=>group.Select(x=>x.Change!.ToolName.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count()>1||group.Where(x=>x.ToolId!=Guid.Empty).Select(x=>x.ToolId).Distinct().Count()>1);
        if(reusedNumber is not null)result.Add(new(PostDiagnosticSeverity.Error,"duplicate-tool-number",$"Tool number T{reusedNumber.Key} is assigned to more than one cutter. Assign a unique number to every different cutter."));
        if(request.ExportScope==PostExportScope.Combined&&tools.Length>1)
            result.Add(new(PostDiagnosticSeverity.Error,"multi-tool-file-unsafe","A runnable machine file may contain only one cutter. Export separate files per tool."));
        if(!request.MachineSnapshot.SupportsArcs&&request.Program.Operations.SelectMany(x=>x.Moves).Any(x=>x is ArcMove))result.Add(new(PostDiagnosticSeverity.Error,"machine-arcs","The selected machine profile does not allow arcs."));
        if(request.MachineSnapshot.ControllerFamily!=ControllerFamily.Community&&request.MachineSnapshot.ControllerFamily!=Definition.ControllerFamily)result.Add(new(PostDiagnosticSeverity.Error,"controller-family",$"The {request.MachineSnapshot.ControllerFamily} machine profile is not compatible with the {Definition.DisplayName} post."));
        if(Definition.CertificationStatus!=PostCertificationStatus.Certified)result.Add(new(PostDiagnosticSeverity.Warning,"uncertified",$"{Definition.DisplayName} is {Definition.CertificationStatus.ToString().ToLowerInvariant()}; verify with an air cut before machining."));
        return result;
    }

    private string Generate(ToolpathProgram program,MachineProfile machine,MeasurementUnit unit,PostExportScope scope)
    {
        var scale=unit==MeasurementUnit.Inches?1/25.4:1d;
        string N(double value)
        {
            if(!double.IsFinite(value))throw new InvalidDataException("G-code contains a non-finite numeric value.");
            return (value*scale).ToString($"F{Definition.Precision}",CultureInfo.InvariantCulture);
        }
        string Comment(string? value){var safe=(string.IsNullOrWhiteSpace(value)?"Unnamed":value).Replace('(','[').Replace(')',']').Replace('\r',' ').Replace('\n',' ').Trim();return Definition.CommentStyle==PostCommentStyle.Parentheses?$"({safe})":$"; {safe}";}
        double X(double value)=>value-machine.WorkZeroDocumentXmm;
        double Y(double value)=>value-machine.WorkZeroDocumentYmm;
        double Z(double value)=>value+machine.WorkZeroZOffsetMm;
        var output=new StringBuilder();output.AppendLine(Comment($"Generated by V-Router - {program.Name}"));
        if(Definition.EmitUnitCode)output.AppendLine(unit==MeasurementUnit.Inches?"G20":"G21");
        foreach(var line in Definition.HeaderLines)output.AppendLine(line);
        Point2D? current=null;var spindle=false;int? activeTool=null;output.AppendLine($"G0 Z{N(Z(machine.SafeZMm))}");
        foreach(var operation in program.Operations)
        {
            output.AppendLine(Comment(operation.Name));
            foreach(var move in operation.Moves)
            {
                switch(move)
                {
                    case ToolChange tool:
                        if(activeTool==tool.ToolNumber)break;
                        output.AppendLine($"G0 Z{N(Z(machine.SafeZMm))}");if(spindle){output.AppendLine("M5");spindle=false;}
                        if(scope==PostExportScope.SeparateByTool)output.AppendLine(Comment($"Load T{tool.ToolNumber} {tool.ToolName}"));
                        else if(machine.ToolChangeMode==MachineToolChangeMode.Automatic)output.AppendLine($"T{tool.ToolNumber} M6 {Comment(tool.ToolName)}");
                        else if(machine.ToolChangeMode==MachineToolChangeMode.ManualPause){output.AppendLine(Comment($"Load T{tool.ToolNumber} {tool.ToolName}"));output.AppendLine("M0");}
                        activeTool=tool.ToolNumber;
                        break;
                    case SpindleStart start when start.Rpm<=0||start.Rpm>machine.MaximumSpindleRpm:throw new InvalidDataException("Spindle RPM is outside the machine limits.");
                    case SpindleStart start:output.AppendLine($"M3 S{start.Rpm}");spindle=true;break;
                    case SpindleStop:output.AppendLine("M5");spindle=false;break;
                    case DwellMove dwell when !double.IsFinite(dwell.Seconds)||dwell.Seconds<=0:throw new InvalidDataException("Dwell time must be positive and finite.");
                    case DwellMove dwell:output.AppendLine($"G4 P{dwell.Seconds.ToString($"F{Definition.Precision}",CultureInfo.InvariantCulture)}");break;
                    case RapidMove rapid:current=rapid.End;output.AppendLine($"G0 X{N(X(rapid.End.X))} Y{N(Y(rapid.End.Y))} Z{N(Z(rapid.Z))}");break;
                    case LinearMove linear when linear.FeedRate<=0:throw new InvalidDataException("Linear move feed rate must be positive.");
                    case LinearMove linear:current=linear.End;output.AppendLine($"G1 X{N(X(linear.End.X))} Y{N(Y(linear.End.Y))} Z{N(Z(linear.Z))} F{N(linear.FeedRate)}");break;
                    case ArcMove when current is null:throw new InvalidDataException("An arc move must follow a positioned move.");
                    case ArcMove arc when arc.FeedRate<=0:throw new InvalidDataException("Arc feed rate must be positive.");
                    case ArcMove arc:
                        var i=Definition.ArcMode==PostArcMode.IncrementalIj?arc.Center.X-current.Value.X:X(arc.Center.X);
                        var j=Definition.ArcMode==PostArcMode.IncrementalIj?arc.Center.Y-current.Value.Y:Y(arc.Center.Y);
                        output.AppendLine($"G{(arc.Clockwise?2:3)} X{N(X(arc.End.X))} Y{N(Y(arc.End.Y))} Z{N(Z(arc.Z))} I{N(i)} J{N(j)} F{N(arc.FeedRate)}");current=arc.End;break;
                    case ToolpathMove unsupported:throw new InvalidDataException($"Unsupported toolpath move '{unsupported.GetType().Name}'.");
                }
            }
        }
        output.AppendLine($"G0 Z{N(Z(machine.SafeZMm))}");output.AppendLine("M5");if(machine.ParkEnabled)output.AppendLine($"G53 G0 X{N(machine.ParkXmm)} Y{N(machine.ParkYmm)}");foreach(var line in Definition.FooterLines)output.AppendLine(line);return output.ToString();
    }

    private static IReadOnlyList<(int? Tool,ToolpathProgram Program)> GroupConsecutiveByTool(ToolpathProgram program)
    {
        var groups=new List<(int? Tool,ToolpathProgram Program)>();
        foreach(var operation in program.Operations)
        {
            var tool=operation.Moves.OfType<ToolChange>().FirstOrDefault()?.ToolNumber;
            if(groups.Count==0||groups[^1].Tool!=tool)groups.Add((tool,new ToolpathProgram{Name=program.Name,Operations=[operation]}));
            else groups[^1].Program.Operations.Add(operation);
        }
        return groups;
    }
    private static string SafeStem(string value){var invalid=Path.GetInvalidFileNameChars().ToHashSet();var safe=new string(value.Where(c=>!invalid.Contains(c)).ToArray()).Trim();return string.IsNullOrWhiteSpace(safe)?"v-router-job":safe;}
}
