using System.IO;

namespace Tuno.PlaybackProbe.Library;

public readonly record struct PlaylistImportResult(int Matched, int Added);

public static class PlaylistImporter
{
    public static PlaylistImportResult AddFiles(LibraryStore store,long playlist,IEnumerable<string> files)
    {
        var paths=files.Select(LibraryStore.PathKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return AddMatching(store,playlist,track=>paths.Contains(LibraryStore.PathKey(track.Path)));
    }

    public static PlaylistImportResult AddFolder(LibraryStore store,long playlist,string folder)
    {
        var prefix=LibraryStore.PathKey(folder)+Path.DirectorySeparatorChar;
        return AddMatching(store,playlist,track=>LibraryStore.PathKey(track.Path).StartsWith(prefix,StringComparison.OrdinalIgnoreCase));
    }

    private static PlaylistImportResult AddMatching(LibraryStore store,long playlist,Func<LibraryTrack,bool> matches)
    {
        var existing=store.ReadTracks(playlist).Select(track=>track.Id).ToHashSet();
        var ids=store.ReadTracks().Where(track=>!track.Missing && matches(track)).Select(track=>track.Id).ToArray();
        if(ids.Length>0)store.ChangePlaylistTracks(playlist,ids,true);
        return new(ids.Length,ids.Count(id=>!existing.Contains(id)));
    }
}
