using System.IO;
using Tuno.PlaybackProbe.Lyrics;
namespace Tuno.PlaybackProbe;
internal static class LyricsSmokeTest
{
    public static int Run(string directory,string sourceMp3)
    {
        Directory.CreateDirectory(directory);
        var run=Path.Combine(Path.GetFullPath(directory),"lyrics-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);var log=new List<string>();
        void Check(bool condition,string name){if(!condition)throw new Exception(name);log.Add("PASS "+name);}
        try
        {
            var content="[ar:测试]\n[offset:-250]\n[00:01.50][00:03.500]第一句\n[00:05.0]第二句\n";
            var lines=LrcParser.Parse(content);
            Check(lines.Count==3 && lines[0].TimeMs==1250 && lines[1].TimeMs==3250 && lines[2].TimeMs==4750,"multiple timestamps / fractions / offset / chronological order");
            Check(LrcParser.ActiveIndex(lines,1249)==-1 && LrcParser.ActiveIndex(lines,1250)==0 && LrcParser.ActiveIndex(lines,3300)==1,"current line boundaries");
            var plain=LrcParser.Parse("[ar:测试]\n没有时间轴\n第二行");
            Check(plain.Count==2 && plain.All(l=>l.TimeMs is null),"plain lyrics and metadata filtering");
            var file=Path.Combine(run,"中文 歌曲.mp3");File.Copy(sourceMp3,file);
            var repo=new LyricsRepository(Path.Combine(run,"data"));
            var sidecar=Path.ChangeExtension(file,".lrc");File.WriteAllText(sidecar,content);
            var result=repo.Load(file,"中文歌曲","歌手",1);
            Check(result.Source=="同名歌词文件" && result.Lines.Count==3,"sidecar lyric discovery with Unicode path");
            var imported=Path.Combine(run,"导入.lrc");File.WriteAllText(imported,"[00:02.00]导入优先");repo.Import(1,imported);
            result=repo.Load(file,"中文歌曲","歌手",1);
            Check(result.Source=="已导入歌词" && result.Lines.Count==1 && result.Lines[0].Text=="导入优先","imported lyric overrides sidecar");
            using(var tag=TagLib.File.Create(file)) {tag.Tag.Lyrics="[00:04.00]内嵌歌词";tag.Save();}
            File.Move(sidecar,sidecar+".bak");
            result=repo.Load(file,"中文歌曲","歌手",2);
            Check(result.Source=="内嵌歌词" && result.Lines.Count==1 && result.Lines[0].TimeMs==4000,"embedded lyric fallback");
            log.Add("INFO isolated lyric test directory: "+run);
            File.WriteAllLines(Path.Combine(directory,"lyrics-result.txt"),log);return 0;
        }
        catch(Exception ex){log.Add("FAIL "+ex);File.WriteAllLines(Path.Combine(directory,"lyrics-result.txt"),log);return 1;}
    }
}
