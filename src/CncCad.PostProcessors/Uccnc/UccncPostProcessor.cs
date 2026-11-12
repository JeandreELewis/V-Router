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

public sealed class UccncPostProcessor() : DeclarativeGCodePostProcessor(BuiltInPostDefinitions.Uccnc);

public static class BuiltInPostDefinitions
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
        var groups=request.ExportScope==PostExportScope.SeparateByTool?GroupConsecutiveByCutter(request.Program):[(Tool:(int?)null,Program:request.Program)];
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
        var motion=MotionEvaluator.Evaluate(request.Program,new(request.MachineSnapshot.RapidRateMmPerMin));foreach(var error in motion.Diagnostics)result.Add(new(PostDiagnosticSeverity.Error,error.Code,error.Message));foreach(var trace in motion.Events.Where(trace=>trace.Kind==MotionEventKind.SpindleStart&&trace.Source is SpindleStart spindle&&spindle.Rpm>request.MachineSnapshot.MaximumSpindleRpm))result.Add(new(PostDiagnosticSeverity.Error,"spindle-limit",$"{trace.OperationName}: spindle speed exceeds the machine limit."));
        if(request.MachineSnapshot.MaximumSpindleRpm<=0)result.Add(new(PostDiagnosticSeverity.Error,"machine","The machine spindle limit is invalid."));
        if(!double.IsFinite(request.MachineSnapshot.SafeZMm)||!double.IsFinite(request.MachineSnapshot.WorkZeroDocumentXmm)||!double.IsFinite(request.MachineSnapshot.WorkZeroDocumentYmm)||!double.IsFinite(request.MachineSnapshot.WorkZeroZOffsetMm))result.Add(new(PostDiagnosticSeverity.Error,"machine-numeric","Machine datum and safe-Z values must be finite."));
        if(request.MachineSnapshot.ParkEnabled&&(!double.IsFinite(request.MachineSnapshot.ParkXmm)||!double.IsFinite(request.MachineSnapshot.ParkYmm)))result.Add(new(PostDiagnosticSeverity.Error,"park-invalid","Enabled machine-coordinate parking requires finite X and Y values."));
        if(motion.Events.Any(trace=>trace.Kind==MotionEventKind.Arc)&&(!Definition.Capabilities.HasFlag(PostCapability.Arcs)||Definition.ArcMode==PostArcMode.Unsupported))result.Add(new(PostDiagnosticSeverity.Error,"unsupported-arcs",$"{Definition.DisplayName} does not support arc output."));
        if(motion.Events.Any(trace=>trace.Kind==MotionEventKind.Dwell)&&!Definition.Capabilities.HasFlag(PostCapability.Dwell))result.Add(new(PostDiagnosticSeverity.Error,"unsupported-dwell",$"{Definition.DisplayName} does not support dwell commands."));
        var toolChanges=motion.Events.Where(trace=>trace.Kind==MotionEventKind.ToolChange).Select(trace=>(ToolChange)trace.Source).ToArray();
        var tools=toolChanges.Select(x=>x.ToolNumber).Distinct().ToArray();
        if(tools.Any(x=>x is null or <=0))result.Add(new(PostDiagnosticSeverity.Error,"invalid-tool","Every exported tool requires a positive tool number."));
        foreach(var operation in request.Program.Operations)
        {
            var changes=operation.Moves.OfType<ToolChange>().ToArray();
            if(operation.ToolId!=Guid.Empty&&changes.Length==0)result.Add(new(PostDiagnosticSeverity.Error,"missing-tool-selection",$"{operation.Name}: the cutter snapshot is not accompanied by a tool selection command."));
            if(changes.Select(CutterChangeKey).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())result.Add(new(PostDiagnosticSeverity.Error,"operation-multiple-tools",$"{operation.Name}: one operation cannot contain more than one cutter."));
        }
        var operationTools=request.Program.Operations.Select(operation=>(operation.ToolId,Change:operation.Moves.OfType<ToolChange>().FirstOrDefault())).Where(x=>x.Change?.ToolNumber is>0).ToArray();
        var reusedNumber=operationTools.GroupBy(x=>x.Change!.ToolNumber).FirstOrDefault(group=>group.Select(x=>x.Change!.ToolName.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count()>1||group.Where(x=>x.ToolId!=Guid.Empty).Select(x=>x.ToolId).Distinct().Count()>1);
        if(reusedNumber is not null)result.Add(new(PostDiagnosticSeverity.Error,"duplicate-tool-number",$"Tool number T{reusedNumber.Key} is assigned to more than one cutter. Assign a unique number to every different cutter."));
        var inconsistentIdentity=operationTools.Where(x=>x.ToolId!=Guid.Empty).GroupBy(x=>x.ToolId).FirstOrDefault(group=>group.Select(x=>CutterChangeKey(x.Change!)).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any());
        if(inconsistentIdentity is not null)result.Add(new(PostDiagnosticSeverity.Error,"tool-identity-conflict","One cutter identity has conflicting tool numbers or names. Refresh the affected operation snapshots."));
        var cutterCount=request.Program.Operations.Select(CutterIdentityKey).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if(request.ExportScope==PostExportScope.Combined&&cutterCount>1)
            result.Add(new(PostDiagnosticSeverity.Error,"multi-tool-file-unsafe","A runnable machine file may contain only one cutter. Export separate files per tool."));
        if(!request.MachineSnapshot.SupportsArcs&&motion.Events.Any(trace=>trace.Kind==MotionEventKind.Arc))result.Add(new(PostDiagnosticSeverity.Error,"machine-arcs","The selected machine profile does not allow arcs."));
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
        var motion=MotionEvaluator.Evaluate(program,new(machine.RapidRateMmPerMin));if(!motion.Success)throw new InvalidDataException(string.Join(" ",motion.Diagnostics.Select(diagnostic=>diagnostic.Message)));var spindle=false;int? activeTool=null;output.AppendLine($"G0 Z{N(Z(machine.SafeZMm))}");
        foreach(var trace in motion.Events)
        {
            if(trace.MoveIndex==0)output.AppendLine(Comment(trace.OperationName));
            switch(trace.Kind)
            {
                    case MotionEventKind.ToolChange when trace.Source is ToolChange tool:
                        if(activeTool==tool.ToolNumber)break;
                        output.AppendLine($"G0 Z{N(Z(machine.SafeZMm))}");if(spindle){output.AppendLine("M5");spindle=false;}
                        if(scope==PostExportScope.SeparateByTool)output.AppendLine(Comment($"Load T{tool.ToolNumber} {tool.ToolName}"));
                        else if(machine.ToolChangeMode==MachineToolChangeMode.Automatic)output.AppendLine($"T{tool.ToolNumber} M6 {Comment(tool.ToolName)}");
                        else if(machine.ToolChangeMode==MachineToolChangeMode.ManualPause){output.AppendLine(Comment($"Load T{tool.ToolNumber} {tool.ToolName}"));output.AppendLine("M0");}
                        activeTool=tool.ToolNumber;
                        break;
                    case MotionEventKind.SpindleStart when trace.Source is SpindleStart start:output.AppendLine($"M3 S{start.Rpm}");spindle=true;break;
                    case MotionEventKind.SpindleStop:output.AppendLine("M5");spindle=false;break;
                    case MotionEventKind.Dwell when trace.Source is DwellMove dwell:output.AppendLine($"G4 P{dwell.Seconds.ToString($"F{Definition.Precision}",CultureInfo.InvariantCulture)}");break;
                    case MotionEventKind.Rapid:output.AppendLine($"G0 X{N(X(trace.End.X))} Y{N(Y(trace.End.Y))} Z{N(Z(trace.EndZ))}");break;
                    case MotionEventKind.Linear when trace.Source is LinearMove linear:output.AppendLine($"G1 X{N(X(trace.End.X))} Y{N(Y(trace.End.Y))} Z{N(Z(trace.EndZ))} F{N(linear.FeedRate)}");break;
                    case MotionEventKind.Arc when trace.Source is ArcMove arc&&trace.ArcCenter is{} center:
                        var i=Definition.ArcMode==PostArcMode.IncrementalIj?center.X-trace.Start.X:X(center.X);
                        var j=Definition.ArcMode==PostArcMode.IncrementalIj?center.Y-trace.Start.Y:Y(center.Y);
                        output.AppendLine($"G{(trace.Clockwise?2:3)} X{N(X(trace.End.X))} Y{N(Y(trace.End.Y))} Z{N(Z(trace.EndZ))} I{N(i)} J{N(j)} F{N(arc.FeedRate)}");break;
                    default:throw new InvalidDataException($"Unsupported canonical motion event '{trace.Kind}'.");
            }
        }
        output.AppendLine($"G0 Z{N(Z(machine.SafeZMm))}");output.AppendLine("M5");if(machine.ParkEnabled)output.AppendLine($"G53 G0 X{N(machine.ParkXmm)} Y{N(machine.ParkYmm)}");foreach(var line in Definition.FooterLines)output.AppendLine(line);return output.ToString();
    }

    private static IReadOnlyList<(int? Tool,ToolpathProgram Program)> GroupConsecutiveByCutter(ToolpathProgram program)
    {
        var groups=new List<(string Cutter,int? Tool,ToolpathProgram Program)>();
        foreach(var operation in program.Operations)
        {
            var change=operation.Moves.OfType<ToolChange>().FirstOrDefault();var cutter=CutterIdentityKey(operation);var tool=change?.ToolNumber;
            if(groups.Count==0||!string.Equals(groups[^1].Cutter,cutter,StringComparison.OrdinalIgnoreCase))groups.Add((cutter,tool,new ToolpathProgram{Name=program.Name,Operations=[operation]}));
            else groups[^1].Program.Operations.Add(operation);
        }
        return groups.Select(group=>(group.Tool,group.Program)).ToArray();
    }
    private static string CutterIdentityKey(ToolpathOperation operation)=>operation.ToolId!=Guid.Empty?$"id:{operation.ToolId:N}":operation.Moves.OfType<ToolChange>().FirstOrDefault() is{} change?$"change:{CutterChangeKey(change)}":"unspecified";
    private static string CutterChangeKey(ToolChange change)=>$"{change.ToolNumber?.ToString(CultureInfo.InvariantCulture)??"none"}:{change.ToolName.Trim()}";
    private static string SafeStem(string value){var invalid=Path.GetInvalidFileNameChars().ToHashSet();var safe=new string(value.Where(c=>!invalid.Contains(c)).ToArray()).Trim();return string.IsNullOrWhiteSpace(safe)?"v-router-job":safe;}
}
