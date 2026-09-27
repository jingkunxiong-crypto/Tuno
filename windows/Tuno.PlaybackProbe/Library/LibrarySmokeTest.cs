using System.IO;
namespace Tuno.PlaybackProbe.Library;

internal static class LibrarySmokeTest
{
    public static async Task<int> Run(string directory,string[] sources)
    {
        Directory.CreateDirectory(directory);var log=new List<string>();
        // Each run uses a fresh test-only database and audio copies; never changes the user's library or music.
        var run=Path.Combine(Path.GetFullPath(directory),"library-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        void Check(bool condition,string name){if(!condition)throw new Exception(name);log.Add("PASS "+name);}
        try
        {
            if(sources.Length<2)throw new ArgumentException("Provide two existing MP3 paths.");
            var root=Path.Combine(run,"音乐样本");var nested=Path.Combine(root,"子文件夹");Directory.CreateDirectory(nested);
            var first=Path.Combine(root,"第一首.mp3");var second=Path.Combine(nested,"第二首.mp3");
            File.Copy(sources[0],first);File.Copy(sources[1],second);
            using(var tagged=TagLib.File.Create(first)){tagged.Tag.Title="测试曲目一";tagged.Tag.Performers=["测试歌手"];tagged.Tag.Album="测试专辑";tagged.Save();}
            using(var tagged=TagLib.File.Create(second)){tagged.Tag.Title="测试曲目二";tagged.Tag.Performers=["另一位歌手"];tagged.Tag.Album="测试专辑";tagged.Save();}
            File.WriteAllText(Path.Combine(nested,"忽略.txt"),"not audio");
            var store=new LibraryStore(Path.Combine(run,"data"));var scanner=new LibraryScanner(store);
            var result=await scanner.ScanFolderAsync(root,null,CancellationToken.None);
            Check(result.Visited==2 && result.Updated==2,"recursive scan / non-audio ignored");
            var excludedStore=new LibraryStore(Path.Combine(run,"excluded-data"));
            var excludedResult=await new LibraryScanner(excludedStore,[nested]).ScanFolderAsync(root,null,CancellationToken.None);
            Check(excludedResult.Visited==1 && excludedStore.ReadTracks().Count==1 && excludedStore.ReadTracks()[0].Path==first,
                "excluded folder skipped during recursive scan");
            var preferencePath=Path.Combine(run,"preferences.json");
            new DesktopPreferences{FilenameMode=2,ShowAlbums=false,ExcludedFolders=[nested],PlaybackSpeedIndex=3,PlaybackSpeed=1.35,EqualizerEnabled=true,EqualizerPreset=-1,EqualizerBands=[1f,-2f]}.Save(preferencePath);
            var savedPreferences=DesktopPreferences.Load(preferencePath);
            Check(savedPreferences.FilenameMode==2 && !savedPreferences.ShowAlbums && savedPreferences.ExcludedFolders.Single()==nested &&
                savedPreferences.PlaybackSpeedIndex==3 && savedPreferences.PlaybackSpeed==1.35 && savedPreferences.EqualizerEnabled && savedPreferences.EqualizerBands.SequenceEqual([1f,-2f]),
                "desktop playback and library preferences persist");
            Check(PlaybackSpeedScale.FromProgress(0)==0.25 && PlaybackSpeedScale.FromProgress(162)==1 &&
                PlaybackSpeedScale.FromProgress(324)==3 && Math.Abs(PlaybackSpeedScale.FromProgress(PlaybackSpeedScale.ToProgress(1.35))-1.35)<0.001,
                "Android-style speed rail maps 1x to center and preserves 0.05x steps");
            var tracks=store.ReadTracks();Check(tracks.Count==2 && tracks.All(t=>t.DurationMs>0),"real MP3 tags and duration");
            var a=tracks.Single(t=>t.Path==first);var b=tracks.Single(t=>t.Path==second);
            Check(a.Title.Length>0 && a.Artist!="未知歌手","title and artist tags");
            var folders=LibraryCategories.Build(tracks,LibraryCategory.Folders);
            Check(folders.Count==2 && folders.All(f=>f.Count==1) &&
                folders.Any(f=>f.Key==root) && folders.Any(f=>f.Key==nested),"folder navigation groups tracks by actual containing folder");
            Check(LibraryCategories.Build(tracks,LibraryCategory.Albums).Sum(g=>g.Count)==2 &&
                LibraryCategories.Build(tracks,LibraryCategory.Artists).Sum(g=>g.Count)==2,
                "album and artist navigation includes every track");
            var sameAlbum=tracks.Select((t,i)=>t with{Album="同名专辑",Artist=i==0?"歌手甲":"歌手乙"}).ToArray();
            Check(LibraryCategories.Build(sameAlbum,LibraryCategory.Albums).Count==2,
                "same album title from different artists remains separate");
            log.Add($"INFO metadata: {a.Title} / {a.Artist} / {a.Album} / {a.Duration}");
            Check(store.Folders().Length==1,"folder persisted");
            store.SetFavorite(a.Id,true);var playlist=store.CreatePlaylist("测试歌单 ' 中文");
            store.ChangePlaylistTracks(playlist,[a.Id,b.Id,a.Id],true);
            Check(store.ReadTracks(playlist).Count==2,"playlist membership / duplicate insertion ignored");
            Check(store.Playlists().Single(p=>p.Id==playlist).TrackCount==2,
                "playlist overview shows the current song count");
            var importPlaylist=store.CreatePlaylist("导入测试");
            var fileImport=PlaylistImporter.AddFiles(store,importPlaylist,[first]);
            Check(fileImport==new PlaylistImportResult(1,1) && store.ReadTracks(importPlaylist).Single().Id==a.Id,
                "file import adds selected audio to current playlist");
            var repeatedImport=PlaylistImporter.AddFiles(store,importPlaylist,[first]);
            Check(repeatedImport==new PlaylistImportResult(1,0),"repeated file import does not duplicate playlist membership");
            var folderImport=PlaylistImporter.AddFolder(store,importPlaylist,root);
            Check(folderImport==new PlaylistImportResult(2,1) && store.ReadTracks(importPlaylist).Count==2,
                "folder import includes nested audio in current playlist");
            var repeat=await scanner.ScanFolderAsync(root,null,CancellationToken.None);
            Check(repeat.Updated==0 && store.ReadTracks().Count==2,"repeat scan incremental / no duplicate songs");
            await scanner.ImportFilesAsync([first.ToUpperInvariant(),first],null,CancellationToken.None);
            Check(store.ReadTracks().Count==2,"case-insensitive Windows path deduplication");
            File.SetLastWriteTimeUtc(first,DateTime.UtcNow.AddMinutes(-1));
            await scanner.ScanFolderAsync(root,null,CancellationToken.None);
            Check(store.ReadTracks().Single(t=>t.Id==a.Id).Favorite && store.ReadTracks(playlist).Count==2,"metadata refresh preserves ID / favorites / playlists");
            var shuffle=new ShuffleOrder(new Random(7));
            var next=shuffle.Next(3,0);var following=shuffle.Next(3,next);
            Check(next!=0 && following!=0 && following!=next && shuffle.Previous(following)==next,
                "shuffle visits distinct tracks before repeating and previous returns through history");
            var noRepeat=new ShuffleOrder(new Random(7));
            var firstPick=QueueNavigator.Next(3,0,PlaybackMode.RepeatOff,true,true,noRepeat);
            var secondPick=QueueNavigator.Next(3,firstPick,PlaybackMode.RepeatOff,true,true,noRepeat);
            Check(firstPick!=secondPick && QueueNavigator.Next(3,secondPick,PlaybackMode.RepeatOff,true,true,noRepeat)==-1,
                "shuffle with repeat off stops after every track has played");
            Check(QueueNavigator.Next(3,2,PlaybackMode.RepeatPlaylist,false,true,noRepeat)==0 &&
                QueueNavigator.Next(3,2,PlaybackMode.RepeatOff,false,true,noRepeat)==-1 &&
                QueueNavigator.Next(3,1,PlaybackMode.RepeatTrack,false,true,noRepeat)==1 &&
                QueueNavigator.Next(3,1,PlaybackMode.RepeatTrack,false,false,noRepeat)==2 &&
                QueueNavigator.Next(3,1,PlaybackMode.StopAfterCurrent,false,true,noRepeat)==-1,
                "repeat and stop-after-current boundaries");
            var reordered=new List<long>{a.Id,b.Id,999};
            var current=QueueOrder.Move(reordered,0,2,1);
            Check(current==0 && reordered.SequenceEqual(new[]{b.Id,999L,a.Id}),
                "queue reorder keeps the current track when another track moves across it");
            current=QueueOrder.Move(reordered,0,2,current);
            Check(current==2 && reordered.SequenceEqual(new[]{999L,a.Id,b.Id}),
                "moving the current track updates its queue index");
            store.SavePlayback(new([b.Id,a.Id],1,12000,27,true,PlaybackMode.RepeatTrack));
            var reopened=new LibraryStore(Path.Combine(run,"data"));var saved=reopened.LoadPlayback();
            Check(reopened.ReadTracks().Count==2 && reopened.ReadTracks().Single(t=>t.Id==a.Id).Favorite,"database reopen restores songs and favorite");
            Check(saved?.Index==1 && saved.PositionMs==12000 && saved.Volume==27 && saved.Shuffle &&
                saved.Mode==PlaybackMode.RepeatTrack && saved.TrackIds.SequenceEqual(new[]{b.Id,a.Id}),
                "queue / position / volume / playback mode survive reopen");
            var oldSnapshot=System.Text.Json.JsonSerializer.Deserialize<PlaybackSnapshot>(
                "{\"TrackIds\":[],\"Index\":-1,\"PositionMs\":0,\"Volume\":35,\"Shuffle\":true}");
            Check(oldSnapshot?.Mode==PlaybackMode.RepeatOff,"older playback snapshot defaults to repeat off");
            reopened.ChangePlaylistTracks(playlist,[a.Id],false);
            Check(reopened.ReadTracks(playlist).Count==1 && reopened.Playlists().Single(p=>p.Id==playlist).TrackCount==1 && File.Exists(first),
                "remove from playlist updates the overview count and preserves original audio");
            reopened.RenamePlaylist(playlist,"重命名后的歌单");
            Check(reopened.Playlists().Single(p=>p.Id==playlist).Name=="重命名后的歌单" &&
                reopened.ReadTracks(playlist).Single().Id==b.Id,
                "rename playlist preserves ID and membership");
            reopened.DeletePlaylist(playlist);
            Check(reopened.Playlists().All(p=>p.Id!=playlist) && reopened.ReadTracks().Count==2 &&
                File.Exists(first) && File.Exists(second) && reopened.LoadPlayback()?.TrackIds.Length==2,
                "delete playlist removes membership without deleting audio, tracks, or playback queue");
            var moved=second+".offline";File.Move(second,moved);
            await scanner.ScanFolderAsync(root,null,CancellationToken.None);
            Check(store.ReadTracks().Single(t=>t.Id==b.Id).Missing && store.ReadTracks().Count==2,"missing file flagged without losing library entry");
            File.Move(moved,second);await scanner.ScanFolderAsync(root,null,CancellationToken.None);
            Check(!store.ReadTracks().Single(t=>t.Id==b.Id).Missing,"file returning clears missing flag");
            using var cts=new CancellationTokenSource();cts.Cancel();
            try{await scanner.ScanFolderAsync(root,null,cts.Token);throw new Exception("Cancellation ignored");}
            catch(OperationCanceledException){log.Add("PASS cancellation");}
            Check(store.ReadTracks().Count==2,"cancel keeps committed library consistent");
            File.WriteAllText(Path.Combine(root,"损坏.mp3"),"not a real MP3");
            var broken=await scanner.ScanFolderAsync(root,null,CancellationToken.None);
            Check(broken.Warnings>=1 && store.ReadTracks().Any(t=>t.Warning is not null),"bad tags reported without aborting scan");
            Check(store.ReadTracks().Count==3,"bad-tag file has fallback title");
            log.Add("INFO test database: "+reopened.DatabasePath);
            File.WriteAllLines(Path.Combine(directory,"library-result.txt"),log);return 0;
        }
        catch(Exception ex){log.Add("FAIL "+ex);File.WriteAllLines(Path.Combine(directory,"library-result.txt"),log);return 1;}
    }
}
