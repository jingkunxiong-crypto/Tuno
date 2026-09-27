using System.IO;
using System.Text.Json;

namespace Tuno.PlaybackProbe;

public sealed class DesktopPreferences
{
    public int FilenameMode { get; set; }
    public bool ShowAllMusic { get; set; } = true;
    public bool ShowFavorites { get; set; } = true;
    public bool ShowAlbums { get; set; } = true;
    public bool ShowArtists { get; set; } = true;
    public bool ShowFolders { get; set; } = true;
    public List<string> ExcludedFolders { get; set; } = [];
    public int PlaybackSpeedIndex { get; set; } = 1;
    public double? PlaybackSpeed { get; set; }
    public bool EqualizerEnabled { get; set; }
    public int EqualizerPreset { get; set; } = -1;
    public float[] EqualizerBands { get; set; } = [];

    public static DesktopPreferences Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<DesktopPreferences>(File.ReadAllText(path)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
