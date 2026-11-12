using CncCad.IO.Projects;
using CncCad.Cam.Drilling;
using CncCad.Cam.Operations;
using CncCad.Cam.Pocket;
using CncCad.Cam.Profile;
using CncCad.Cam.Toolpaths;
using CncCad.Core.Transforms;
using CncCad.PostProcessors.Models;
using CncCad.PostProcessors.Uccnc;

if(args.Length is <1 or >2)throw new ArgumentException("Pass the destination .cnccad path and optional UCCNC output directory.");
var destination=Path.GetFullPath(args[0]);
Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
var project=ModernDoorAcceptanceProject.Create();
await using var stream=new FileStream(destination,FileMode.Create,FileAccess.Write,FileShare.None);
await new JsonProjectSerializer().SaveAsync(project,stream);
Console.WriteLine(destination);

if(args.Length==2)
{
    var output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
    var calculator=new CompositeOperationCalculator(new ProfileOperationCalculator(),new PocketOperationCalculator(),new DrillOperationCalculator(),new EngraveOperationCalculator(),new VCarveOperationCalculator());
    var geometry=project.Document.Entities.ToDictionary(x=>x.Id);
    foreach(var operation in project.CamJob.Operations){var result=calculator.Calculate(operation,geometry);if(!result.Success)throw new InvalidDataException($"{operation.Name}: {string.Join("; ",result.Errors.Select(x=>x.Message))}");operation.CalculatedToolpath=result.Toolpath;operation.Status=OperationStatus.Current;}
    var origin=MachineCoordinateTransform.OriginInDocument(project.Document.Job);var snapshot=project.CamJob.Machine!;
    var machine=new MachineProfile{Name=snapshot.Name,PostProcessor=snapshot.PostProcessorId,SafeZMm=snapshot.DefaultClearanceMm,MaximumXmm=snapshot.MaximumXmm,MaximumYmm=snapshot.MaximumYmm,MaximumZmm=snapshot.MaximumZmm,MaximumSpindleRpm=snapshot.MaximumSpindleRpm,RapidRateMmPerMin=snapshot.RapidRateMmPerMin,ParkXmm=snapshot.ParkXmm,ParkYmm=snapshot.ParkYmm,WorkZeroDocumentXmm=origin.X,WorkZeroDocumentYmm=origin.Y};
    var post=new UccncPostProcessor();
    Write("Test Door - Selected Profile.nc",[project.CamJob.Operations[0].CalculatedToolpath!]);
    Write("Test Door - Combined.nc",project.CamJob.Operations.Select(x=>x.CalculatedToolpath!).ToList());
    foreach(var operation in project.CamJob.Operations)Write($"Test Door - {operation.Order+1:00} {SafeName(operation.Name)}.nc",[operation.CalculatedToolpath!]);
    void Write(string name,List<ToolpathOperation> operations){var text=post.Generate(new(){Name=Path.GetFileNameWithoutExtension(name),Operations=operations},machine);var path=Path.Combine(output,name);var temporary=path+".tmp";File.WriteAllText(temporary,text);File.Move(temporary,path,true);Console.WriteLine(path);}
    static string SafeName(string value)=>string.Concat(value.Select(character=>Path.GetInvalidFileNameChars().Contains(character)?'_':character));
}
