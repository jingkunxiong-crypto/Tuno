using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
namespace Tuno.PlaybackProbe.Library;

public sealed class LibraryStore
{
    public string DatabasePath { get; }
    public LibraryStore(string directory)
    {
        Directory.CreateDirectory(directory);
        DatabasePath = Path.Combine(directory, "library.db");
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS tracks(
              id INTEGER PRIMARY KEY, path_key TEXT NOT NULL UNIQUE, path TEXT NOT NULL,
              title TEXT NOT NULL, artist TEXT NOT NULL, album TEXT NOT NULL, duration_ms INTEGER NOT NULL,
              size INTEGER NOT NULL, modified_ticks INTEGER NOT NULL, favorite INTEGER NOT NULL DEFAULT 0,
              missing INTEGER NOT NULL DEFAULT 0, cover BLOB, warning TEXT);
            CREATE TABLE IF NOT EXISTS folders(path_key TEXT PRIMARY KEY, path TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS playlists(id INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE);
            CREATE TABLE IF NOT EXISTS playlist_tracks(
              playlist_id INTEGER NOT NULL REFERENCES playlists(id) ON DELETE CASCADE,
              track_id INTEGER NOT NULL REFERENCES tracks(id) ON DELETE CASCADE,
              PRIMARY KEY(playlist_id, track_id));
            CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT NOT NULL);
            PRAGMA user_version=1;
            """;
        cmd.ExecuteNonQuery();
    }
    private SqliteConnection Open()
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = DatabasePath, ForeignKeys = true, DefaultTimeout = 10 }.ToString());
        db.Open();
        return db;
    }
    public static string PathKey(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
    private static SqliteCommand Command(SqliteConnection db, string sql, params (string, object?)[] values)
    {
        var cmd = db.CreateCommand(); cmd.CommandText = sql;
        foreach (var (key, value) in values) cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);
        return cmd;
    }
    public List<LibraryTrack> ReadTracks(long? playlist = null)
    {
        using var db = Open();
        using var cmd = Command(db, """
            SELECT id,path,title,artist,album,duration_ms,size,modified_ticks,favorite,missing,warning FROM tracks
            WHERE $playlist IS NULL OR id IN (SELECT track_id FROM playlist_tracks WHERE playlist_id=$playlist)
            ORDER BY title COLLATE NOCASE,id
            """, ("$playlist", playlist));
        using var r = cmd.ExecuteReader(); var rows = new List<LibraryTrack>();
        while (r.Read()) rows.Add(new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),
            r.GetInt64(5),r.GetInt64(6),r.GetInt64(7),r.GetBoolean(8),r.GetBoolean(9),r.IsDBNull(10)?null:r.GetString(10)));
        return rows;
    }
    public void CommitScan(IEnumerable<TrackMetadata> rows, string? folder, CancellationToken token)
    {
        using var db = Open(); using var tx = db.BeginTransaction();
        foreach (var row in rows)
        {
            token.ThrowIfCancellationRequested();
            using var cmd = Command(db, """
                INSERT INTO tracks(path_key,path,title,artist,album,duration_ms,size,modified_ticks,cover,warning)
                VALUES($key,$path,$title,$artist,$album,$duration,$size,$modified,$cover,$warning)
                ON CONFLICT(path_key) DO UPDATE SET path=excluded.path,title=excluded.title,artist=excluded.artist,
                album=excluded.album,duration_ms=excluded.duration_ms,size=excluded.size,
                modified_ticks=excluded.modified_ticks,cover=excluded.cover,warning=excluded.warning,missing=0
                """, ("$key",PathKey(row.Path)),("$path",row.Path),("$title",row.Title),("$artist",row.Artist),
                ("$album",row.Album),("$duration",row.DurationMs),("$size",row.Size),("$modified",row.ModifiedTicks),
                ("$cover",row.Cover),("$warning",row.Warning));
            cmd.Transaction=tx; cmd.ExecuteNonQuery();
        }
        if (folder is not null)
        {
            using var cmd = Command(db,"INSERT OR IGNORE INTO folders(path_key,path) VALUES($key,$path)",
                ("$key",PathKey(folder)),("$path",Path.GetFullPath(folder)));
            cmd.Transaction=tx; cmd.ExecuteNonQuery();
        }
        token.ThrowIfCancellationRequested(); tx.Commit();
    }
    public void RefreshAvailability(CancellationToken token)
    {
        var tracks=ReadTracks();
        using var db=Open(); using var tx=db.BeginTransaction();
        foreach(var track in tracks)
        {
            token.ThrowIfCancellationRequested();
            var missing=!File.Exists(track.Path);
            if(missing==track.Missing)continue;
            using var cmd=Command(db,"UPDATE tracks SET missing=$missing WHERE id=$id",("$missing",missing),("$id",track.Id));
            cmd.Transaction=tx;cmd.ExecuteNonQuery();
        }
        token.ThrowIfCancellationRequested();tx.Commit();
    }
    public string[] Folders()
    {
        using var db=Open();using var cmd=Command(db,"SELECT path FROM folders ORDER BY path");
        using var r=cmd.ExecuteReader();var rows=new List<string>();while(r.Read())rows.Add(r.GetString(0));return rows.ToArray();
    }
    public byte[]? Cover(long id)
    {
        using var db=Open();using var cmd=Command(db,"SELECT cover FROM tracks WHERE id=$id",("$id",id));
        return cmd.ExecuteScalar() as byte[];
    }
    public void SetFavorite(long id,bool favorite)
    {
        using var db=Open();using var cmd=Command(db,"UPDATE tracks SET favorite=$favorite WHERE id=$id",("$favorite",favorite),("$id",id));cmd.ExecuteNonQuery();
    }
    public List<Playlist> Playlists()
    {
        using var db=Open();using var cmd=Command(db,"""
            SELECT p.id,p.name,COUNT(pt.track_id)
            FROM playlists p LEFT JOIN playlist_tracks pt ON pt.playlist_id=p.id
            GROUP BY p.id,p.name ORDER BY p.name
            """);
        using var r=cmd.ExecuteReader();var rows=new List<Playlist>();while(r.Read())rows.Add(new(r.GetInt64(0),r.GetString(1),r.GetInt32(2)));return rows;
    }
    public long CreatePlaylist(string name)
    {
        name=name.Trim();if(name.Length is 0 or >80)throw new ArgumentException("歌单名称需要 1–80 个字符。");
        using var db=Open();using var cmd=Command(db,"INSERT INTO playlists(name) VALUES($name) RETURNING id",("$name",name));
        return (long)cmd.ExecuteScalar()!;
    }
    public void RenamePlaylist(long id,string name)
    {
        name=name.Trim();if(name.Length is 0 or >80)throw new ArgumentException("歌单名称需要 1–80 个字符。");
        using var db=Open();using var cmd=Command(db,"UPDATE playlists SET name=$name WHERE id=$id",("$name",name),("$id",id));
        if(cmd.ExecuteNonQuery()!=1)throw new KeyNotFoundException("歌单已不存在。");
    }
    public void DeletePlaylist(long id)
    {
        using var db=Open();using var cmd=Command(db,"DELETE FROM playlists WHERE id=$id",("$id",id));
        if(cmd.ExecuteNonQuery()!=1)throw new KeyNotFoundException("歌单已不存在。");
    }
    public void ChangePlaylistTracks(long playlist,IEnumerable<long> ids,bool add)
    {
        using var db=Open();using var tx=db.BeginTransaction();
        foreach(var id in ids.Distinct())
        {
            using var cmd=Command(db,add?"INSERT OR IGNORE INTO playlist_tracks(playlist_id,track_id) VALUES($p,$t)":"DELETE FROM playlist_tracks WHERE playlist_id=$p AND track_id=$t",("$p",playlist),("$t",id));
            cmd.Transaction=tx;cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
    public void SavePlayback(PlaybackSnapshot state)
    {
        using var db=Open();using var cmd=Command(db,"INSERT INTO settings(key,value) VALUES('playback',$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value",("$value",JsonSerializer.Serialize(state)));cmd.ExecuteNonQuery();
    }
    public PlaybackSnapshot? LoadPlayback()
    {
        using var db=Open();using var cmd=Command(db,"SELECT value FROM settings WHERE key='playback'");
        try{return cmd.ExecuteScalar() is string json?JsonSerializer.Deserialize<PlaybackSnapshot>(json):null;}
        catch(JsonException){return null;}
    }
}
