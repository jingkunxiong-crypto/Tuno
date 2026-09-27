using System.IO;
namespace Tuno.PlaybackProbe.Library;

public sealed record LibraryTrack(long Id, string Path, string Title, string Artist, string Album,
    long DurationMs, long Size, long ModifiedTicks, bool Favorite, bool Missing, string? Warning)
{
    public string Duration => TimeSpan.FromMilliseconds(Math.Max(0, DurationMs)).ToString(DurationMs>=3600000?@"hh\:mm\:ss":@"mm\:ss");
    public string FavoriteText => Favorite ? "♥" : "";
    public string StateText => Missing ? "文件不可用" : Warning is null ? "" : "标签未完整读取";
    public string FileName => System.IO.Path.GetFileName(Path);
}
public sealed record TrackMetadata(string Path, string Title, string Artist, string Album,
    long DurationMs, long Size, long ModifiedTicks, byte[]? Cover, string? Warning);
public sealed record Playlist(long Id, string Name, int TrackCount = 0)
{
    public string TrackCountLabel => $"{TrackCount} 首歌曲";
    public override string ToString() => Name;
}
public enum PlaybackMode { RepeatOff, RepeatPlaylist, RepeatTrack, StopAfterCurrent }
public sealed record PlaybackSnapshot(long[] TrackIds, int Index, long PositionMs, int Volume, bool Shuffle=false,
    PlaybackMode Mode=PlaybackMode.RepeatOff);
public sealed record ScanProgress(int Visited, int Updated, int Warnings, string FileName);
public sealed record ScanResult(int Visited, int Updated, int Warnings);
