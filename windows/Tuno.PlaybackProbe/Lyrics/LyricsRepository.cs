using System.IO;
using System.Text.RegularExpressions;

namespace Tuno.PlaybackProbe.Lyrics;

public sealed record LyricLine(long? TimeMs, string Text);

public static partial class LrcParser
{
    [GeneratedRegex(@"\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?\]",RegexOptions.Compiled)]
    private static partial Regex Timestamp();
    [GeneratedRegex(@"\[offset:([+-]?\d+)\]",RegexOptions.IgnoreCase|RegexOptions.Compiled)]
    private static partial Regex Offset();
    [GeneratedRegex(@"^\[[a-z][a-z0-9_-]*:.*\]$",RegexOptions.IgnoreCase|RegexOptions.Compiled)]
    private static partial Regex Metadata();

    public static IReadOnlyList<LyricLine> Parse(string content)
    {
        if(string.IsNullOrWhiteSpace(content))return [];
        var offset=long.TryParse(Offset().Match(content).Groups[1].Value,out var n)?n:0;
        var timed=new List<LyricLine>();
        foreach(var raw in content.Replace("\r\n","\n").Split('\n'))
        {
            var matches=Timestamp().Matches(raw);
            if(matches.Count==0)continue;
            var words=Timestamp().Replace(raw,"").Trim();
            if(words.Length==0)continue;
            foreach(Match match in matches)
            {
                if(!long.TryParse(match.Groups[1].Value,out var minutes) || !long.TryParse(match.Groups[2].Value,out var seconds) || seconds>=60)continue;
                var fraction=match.Groups[3].Value;
                var millis=fraction.Length switch{1=>int.Parse(fraction)*100,2=>int.Parse(fraction)*10,3=>int.Parse(fraction),_=>0};
                timed.Add(new(Math.Max(0,minutes*60000+seconds*1000+millis+offset),words));
            }
        }
        if(timed.Count>0)return timed.OrderBy(x=>x.TimeMs).ToArray();
        return content.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries)
            .Select(s=>s.Trim()).Where(s=>s.Length>0 && !Metadata().IsMatch(s))
            .Select(s=>new LyricLine(null,s)).ToArray();
    }
    public static int ActiveIndex(IReadOnlyList<LyricLine> lines,long playbackTimeMs)
    {
        if(lines.Count==0 || lines[0].TimeMs is null)return -1;
        int lo=0,hi=lines.Count-1,result=-1;
        while(lo<=hi)
        {
            var mid=lo+(hi-lo)/2;
            if(lines[mid].TimeMs<=playbackTimeMs){result=mid;lo=mid+1;}else hi=mid-1;
        }
        return result;
    }
}

public sealed class LyricsRepository(string dataDirectory)
{
    private readonly string importedDirectory=Path.Combine(dataDirectory,"lyrics");
    public string ImportedFile(long trackId)=>Path.Combine(importedDirectory,$"{trackId}.lrc");
    public void Import(long trackId,string sourcePath)
    {
        var info=new FileInfo(sourcePath);
        if(!info.Exists || info.Length>2_000_000)throw new InvalidDataException("歌词文件不存在或超过 2 MB。");
        Directory.CreateDirectory(importedDirectory);
        File.WriteAllText(ImportedFile(trackId),File.ReadAllText(sourcePath));
    }
    public (IReadOnlyList<LyricLine> Lines,string Source) Load(string audioPath,string title,string artist,long trackId)
    {
        var stem=Path.Combine(Path.GetDirectoryName(audioPath)??"",Path.GetFileNameWithoutExtension(audioPath));
        var candidates=new[]{ImportedFile(trackId),stem+".lrc",
            Path.Combine(Path.GetDirectoryName(audioPath)??"",$"{title} - {artist}.lrc"),
            Path.Combine(Path.GetDirectoryName(audioPath)??"",$"{artist} - {title}.lrc")};
        foreach(var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var info=new FileInfo(path);
                if(!info.Exists || info.Length>2_000_000)continue;
                var lines=LrcParser.Parse(File.ReadAllText(path));
                if(lines.Count>0)return (lines,path==candidates[0]?"已导入歌词":"同名歌词文件");
            }
            catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}
        }
        try
        {
            using var audio=TagLib.File.Create(audioPath,TagLib.ReadStyle.None);
            var lines=LrcParser.Parse(audio.Tag.Lyrics??"");
            if(lines.Count>0)return (lines,"内嵌歌词");
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or TagLib.CorruptFileException or TagLib.UnsupportedFormatException or ArgumentException){}
        return ([],"未找到歌词");
    }
}
