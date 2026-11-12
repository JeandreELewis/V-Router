using System.Text;

namespace CncCad.App.Services;

public sealed record AtomicExportFile(string Path,string Content);

public static class AtomicExportBatchWriter
{
    public static async Task WriteAsync(IEnumerable<AtomicExportFile> files,CancellationToken cancellationToken=default)
    {
        var batch=files.Select(file=>file with{Path=System.IO.Path.GetFullPath(file.Path)}).ToArray();
        if(batch.Length==0)throw new ArgumentException("The export batch is empty.",nameof(files));
        var duplicate=batch.GroupBy(file=>file.Path,StringComparer.OrdinalIgnoreCase).FirstOrDefault(group=>group.Count()>1);
        if(duplicate is not null)throw new InvalidOperationException($"Two generated programs resolve to the same filename: {System.IO.Path.GetFileName(duplicate.Key)}.");

        var staged=new List<(AtomicExportFile File,string Temporary,string? Backup,bool Committed)>();
        try
        {
            foreach(var file in batch)
            {
                var directory=System.IO.Path.GetDirectoryName(file.Path)??Directory.GetCurrentDirectory();
                Directory.CreateDirectory(directory);
                var temporary=System.IO.Path.Combine(directory,$".{System.IO.Path.GetFileName(file.Path)}.{Guid.NewGuid():N}.tmp");
                await File.WriteAllTextAsync(temporary,file.Content,new UTF8Encoding(false),cancellationToken);
                staged.Add((file,temporary,null,false));
            }

            for(var index=0;index<staged.Count;index++)
            {
                var item=staged[index];
                string? backup=null;
                if(File.Exists(item.File.Path))
                {
                    backup=item.File.Path+$".{Guid.NewGuid():N}.bak";
                    File.Move(item.File.Path,backup);
                }
                staged[index]=(item.File,item.Temporary,backup,false);
                File.Move(item.Temporary,item.File.Path);
                staged[index]=(item.File,item.Temporary,backup,true);
            }

            foreach(var item in staged.Where(item=>item.Backup is not null))File.Delete(item.Backup!);
        }
        catch
        {
            foreach(var item in staged.AsEnumerable().Reverse())
            {
                if(item.Committed&&File.Exists(item.File.Path))File.Delete(item.File.Path);
                if(item.Backup is not null&&File.Exists(item.Backup))File.Move(item.Backup,item.File.Path,true);
            }
            throw;
        }
        finally
        {
            foreach(var item in staged)
            {
                if(File.Exists(item.Temporary))File.Delete(item.Temporary);
                if(item.Backup is not null&&File.Exists(item.Backup))File.Delete(item.Backup);
            }
        }
    }
}
