using System.IO;

namespace Tuno.PlaybackProbe.Library;

public enum LibraryCategory { None, Albums, Artists, Folders }

public sealed record CategoryEntry(string Key, string Name, int Count)
{
    public string DisplayName => $"{Name}  ·  {Count}";
}

public static class LibraryCategories
{
    public static string Title(LibraryCategory category) => category switch
    {
        LibraryCategory.Albums => "专辑",
        LibraryCategory.Artists => "艺术家",
        LibraryCategory.Folders => "文件夹",
        _ => "歌单"
    };

    public static string Key(LibraryTrack track, LibraryCategory category) => category switch
    {
        LibraryCategory.Albums => $"{Value(track.Album,"未知专辑")}\u001f{Value(track.Artist,"未知艺术家")}",
        LibraryCategory.Artists => string.IsNullOrWhiteSpace(track.Artist) ? "未知艺术家" : track.Artist.Trim(),
        LibraryCategory.Folders => Path.GetDirectoryName(track.Path) ?? "",
        _ => ""
    };

    public static IReadOnlyList<CategoryEntry> Build(IEnumerable<LibraryTrack> tracks, LibraryCategory category)
    {
        if(category==LibraryCategory.None)return [];
        return tracks.GroupBy(t=>Key(t,category),StringComparer.OrdinalIgnoreCase)
            .Select(group=>new CategoryEntry(group.Key,DisplayName(group.Key,category),group.Count()))
            .OrderBy(entry=>entry.Name,StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static string DisplayName(string key,LibraryCategory category)
    {
        if(category==LibraryCategory.Albums)
        {
            var parts=key.Split('\u001f',2);
            return $"{parts[0]} · {parts[1]}";
        }
        if(category!=LibraryCategory.Folders)return key;
        var name=Path.GetFileName(key.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name)?key:name;
    }

    private static string Value(string value,string fallback)=>string.IsNullOrWhiteSpace(value)?fallback:value.Trim();
}
