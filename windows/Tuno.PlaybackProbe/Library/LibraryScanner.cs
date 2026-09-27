using System.IO;
namespace Tuno.PlaybackProbe.Library;

public sealed class LibraryScanner(LibraryStore store, IReadOnlyCollection<string>? excludedFolders = null)
{
    private readonly string[] excludedPathKeys = (excludedFolders ?? []).Select(LibraryStore.PathKey).ToArray();
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".wma", ".opus", ".aiff", ".aif" };
    public Task<ScanResult> ScanFolderAsync(string folder,IProgress<ScanProgress>? progress,CancellationToken token)
        => Task.Run(() => Scan(folder,null,progress,token),token);
    public Task<ScanResult> ImportFilesAsync(string[] files,IProgress<ScanProgress>? progress,CancellationToken token)
        => Task.Run(() => Scan(null,files,progress,token),token);
    private ScanResult Scan(string? folder,string[]? files,IProgress<ScanProgress>? progress,CancellationToken token)
    {
        if(folder is not null && !Directory.Exists(folder))throw new DirectoryNotFoundException("音乐文件夹不可用："+folder);
        var old=store.ReadTracks().ToDictionary(t=>LibraryStore.PathKey(t.Path));
        var updates=new List<TrackMetadata>();var seen=new HashSet<string>();int visited=0,warnings=0,updated=0;
        IEnumerable<string> Walk(string root)
        {
            var pending=new Stack<string>();pending.Push(root);
            while(pending.TryPop(out var dir))
            {
                token.ThrowIfCancellationRequested();
                if(IsExcluded(dir))continue;
                string[] children;
                try { children=Directory.GetFileSystemEntries(dir); }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){ warnings++;continue; }
                foreach(var child in children)
                {
                    token.ThrowIfCancellationRequested();FileAttributes attributes;
                    try{attributes=File.GetAttributes(child);}
                    catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){warnings++;continue;}
                    if(attributes.HasFlag(FileAttributes.ReparsePoint))continue;
                    if(attributes.HasFlag(FileAttributes.Directory))
                    {if(!IsExcluded(child))pending.Push(child);}
                    else if(!IsExcluded(child))yield return child;
                }
            }
        }
        foreach(var path in folder is not null?Walk(folder):files ?? [])
        {
            token.ThrowIfCancellationRequested();
            if(!Extensions.Contains(Path.GetExtension(path)) || !seen.Add(LibraryStore.PathKey(path)))continue;
            visited++;
            try
            {
                var info=new FileInfo(path);
                if(!info.Exists){warnings++;continue;}
                if(old.TryGetValue(LibraryStore.PathKey(path),out var previous) && previous.Size==info.Length &&
                    previous.ModifiedTicks==info.LastWriteTimeUtc.Ticks && !previous.Missing && previous.Warning is null)
                { if(visited%25==0)progress?.Report(new(visited,updated,warnings,info.Name));continue; }
                string title=info.Name[..^info.Extension.Length],artist="未知歌手",album="未知专辑";
                long duration=0;byte[]? cover=null;string? warning=null;
                try
                {
                    using var audio=TagLib.File.Create(path,TagLib.ReadStyle.Average);
                    if(!string.IsNullOrWhiteSpace(audio.Tag.Title))title=audio.Tag.Title.Trim();
                    if(audio.Tag.Performers.Length>0)artist=string.Join(" / ",audio.Tag.Performers);
                    if(!string.IsNullOrWhiteSpace(audio.Tag.Album))album=audio.Tag.Album.Trim();
                    duration=(long)audio.Properties.Duration.TotalMilliseconds;
                    var picture=audio.Tag.Pictures.FirstOrDefault();
                    if(picture?.Data.Count is >0 and <=4194304)cover=picture.Data.Data;
                }
                catch(Exception ex) when(ex is TagLib.CorruptFileException or TagLib.UnsupportedFormatException or IOException or UnauthorizedAccessException or ArgumentException)
                {warning=ex.Message;warnings++;}
                updates.Add(new(Path.GetFullPath(path),title,artist,album,duration,info.Length,info.LastWriteTimeUtc.Ticks,cover,warning));
                updated++;
                // Bound retained artwork memory for large libraries. Completed batches survive cancellation.
                if(updates.Count>=32){store.CommitScan(updates,null,token);updates.Clear();}
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){warnings++;}
            if(visited%10==0)progress?.Report(new(visited,updated,warnings,Path.GetFileName(path)));
        }
        token.ThrowIfCancellationRequested();
        store.CommitScan(updates,folder,token);
        store.RefreshAvailability(token);
        progress?.Report(new(visited,updated,warnings,"完成"));
        return new(visited,updated,warnings);
    }

    private bool IsExcluded(string path)
    {
        var key=LibraryStore.PathKey(path);
        return excludedPathKeys.Any(excluded=>key.Equals(excluded,StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith(excluded+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));
    }
}
