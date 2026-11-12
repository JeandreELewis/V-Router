using System.Text.Json;

namespace CncCad.IO.Projects;

public sealed record RecoverySnapshotInfo(string SnapshotId,string JobKey,string DisplayName,string? ProjectPath,DateTimeOffset SavedAtUtc,string SnapshotPath);

public interface IRecoveryStore
{
    Task SaveAsync(string jobKey,string displayName,string? projectPath,CncProject project,CancellationToken cancellationToken=default);
    Task<IReadOnlyList<RecoverySnapshotInfo>> GetRecoverableAsync(CancellationToken cancellationToken=default);
    Task<CncProject> LoadAsync(RecoverySnapshotInfo snapshot,CancellationToken cancellationToken=default);
    Task DeleteJobAsync(string jobKey,CancellationToken cancellationToken=default);
}

public sealed class JsonRecoveryStore(string directory,IProjectSerializer? serializer=null):IRecoveryStore
{
    private readonly IProjectSerializer _serializer=serializer??new JsonProjectSerializer();
    private static readonly JsonSerializerOptions MetadataOptions=new(JsonSerializerDefaults.General){WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase};

    public async Task SaveAsync(string jobKey,string displayName,string? projectPath,CncProject project,CancellationToken cancellationToken=default)
    {
        if(string.IsNullOrWhiteSpace(jobKey))throw new ArgumentException("Recovery job key is required.",nameof(jobKey));
        Directory.CreateDirectory(directory);
        var timestamp=DateTimeOffset.UtcNow;var id=$"{Safe(jobKey)}-{timestamp:yyyyMMddHHmmssfff}";var projectFile=Path.Combine(directory,id+".cnccad");var temp=projectFile+".tmp";
        await using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None,81920,FileOptions.Asynchronous|FileOptions.WriteThrough)){await _serializer.SaveAsync(project,stream,cancellationToken);await stream.FlushAsync(cancellationToken);stream.Flush(true);}
        File.Move(temp,projectFile,true);
        var info=new RecoverySnapshotInfo(id,jobKey,string.IsNullOrWhiteSpace(displayName)?"Unsaved job":displayName,projectPath,timestamp,projectFile);
        var metaTemp=projectFile+".json.tmp";await File.WriteAllTextAsync(metaTemp,JsonSerializer.Serialize(info,MetadataOptions),cancellationToken);File.Move(metaTemp,projectFile+".json",true);
        foreach(var old in (await ReadAllAsync(cancellationToken)).Where(x=>x.JobKey==jobKey).OrderByDescending(x=>x.SavedAtUtc).Skip(3))Delete(old);
    }

    public async Task<IReadOnlyList<RecoverySnapshotInfo>> GetRecoverableAsync(CancellationToken cancellationToken=default)
    {
        var all=await ReadAllAsync(cancellationToken);var result=new List<RecoverySnapshotInfo>();
        foreach(var snapshot in all.OrderByDescending(x=>x.SavedAtUtc))
        {
            if(!File.Exists(snapshot.SnapshotPath))continue;
            if(snapshot.ProjectPath is null||!File.Exists(snapshot.ProjectPath)||File.GetLastWriteTimeUtc(snapshot.ProjectPath)<snapshot.SavedAtUtc.UtcDateTime)result.Add(snapshot);
        }
        return result;
    }

    public async Task<CncProject> LoadAsync(RecoverySnapshotInfo snapshot,CancellationToken cancellationToken=default)
    { await using var stream=File.OpenRead(snapshot.SnapshotPath);return await _serializer.LoadAsync(stream,cancellationToken); }

    public async Task DeleteJobAsync(string jobKey,CancellationToken cancellationToken=default)
    { foreach(var snapshot in (await ReadAllAsync(cancellationToken)).Where(x=>x.JobKey==jobKey))Delete(snapshot); }

    private async Task<List<RecoverySnapshotInfo>> ReadAllAsync(CancellationToken cancellationToken)
    {
        if(!Directory.Exists(directory))return [];
        var result=new List<RecoverySnapshotInfo>();
        foreach(var file in Directory.EnumerateFiles(directory,"*.cnccad.json"))try{var value=JsonSerializer.Deserialize<RecoverySnapshotInfo>(await File.ReadAllTextAsync(file,cancellationToken),MetadataOptions);if(value is not null)result.Add(value);}catch(JsonException){}catch(IOException){}
        return result;
    }
    private static void Delete(RecoverySnapshotInfo snapshot){try{File.Delete(snapshot.SnapshotPath);File.Delete(snapshot.SnapshotPath+".json");}catch(IOException){}}
    private static string Safe(string value)=>string.Concat(value.Select(ch=>char.IsLetterOrDigit(ch)||ch is '-' or '_'?ch:'_'));
}
