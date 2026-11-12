using CncCad.Cam.Nesting;
using CncCad.Cam.Operations;

namespace CncCad.IO.Projects;

public static class ProductionAcceptanceProjects
{
    public static IReadOnlyDictionary<string,Func<CncProject>> All { get; }=new Dictionary<string,Func<CncProject>>(StringComparer.Ordinal)
    {
        ["cabinet-panel"]=CabinetPanel,
        ["sign"]=Sign,
        ["nested-sheet"]=NestedSheet
    };

    public static CncProject CabinetPanel()
    {
        var project=ModernDoorAcceptanceProject.Create();project.Document.Job.Name="Cabinet Panel Proof";return project;
    }

    public static CncProject Sign()
    {
        var project=ModernDoorAcceptanceProject.Create();project.Document.Job.Name="Sign Proof";project.CamJob.Operations=project.CamJob.Operations.Where(x=>x is EngraveOperationDefinition or VCarveOperationDefinition).ToList();project.CamJob.NormalizeOrder();return project;
    }

    public static CncProject NestedSheet()
    {
        var project=ModernDoorAcceptanceProject.Create();project.Document.Job.Name="Nested Sheet Proof";var a=new MaterialSheetDefinition{Name="Sheet A",WidthMm=project.Document.Job.WidthMm,HeightMm=project.Document.Job.HeightMm};var b=new MaterialSheetDefinition{Name="Sheet B",WidthMm=project.Document.Job.WidthMm,HeightMm=project.Document.Job.HeightMm};
        for(var i=0;i<project.CamJob.Operations.Count;i++){var sheet=i%2==0?a.Id:b.Id;switch(project.CamJob.Operations[i]){case ProfileOperationDefinition value:value.MaterialSheetId=sheet;break;case PocketOperationDefinition value:value.MaterialSheetId=sheet;break;case DrillOperationDefinition value:value.MaterialSheetId=sheet;break;case EngraveOperationDefinition value:value.MaterialSheetId=sheet;break;case VCarveOperationDefinition value:value.MaterialSheetId=sheet;break;}}
        project.Nesting=new(){Status=NestingLayoutStatus.Current,AcceptedLayout=new(){Sheets=[a,b],Placements=[],Score=new(0,0,0)}};return project;
    }
}
