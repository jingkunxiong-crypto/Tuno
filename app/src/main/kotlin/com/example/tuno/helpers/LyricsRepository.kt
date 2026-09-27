package com.example.tuno.helpers

import android.content.Context
import com.example.tuno.models.Track
import org.jaudiotagger.audio.AudioFileIO
import org.jaudiotagger.tag.FieldKey
import java.io.File

class LyricsRepository(private val context: Context) {
    private val lyricsDir = File(context.filesDir, "lyrics")

    fun load(track: Track): String? {
        val imported = importedFile(track)
        if (imported.isFile) {
            imported.readText().takeIf { it.isNotBlank() }?.let { return it }
        }

        sidecarFiles(track).firstOrNull { it.isFile && it.canRead() }
            ?.runCatching { readText() }
            ?.getOrNull()
            ?.takeIf { it.isNotBlank() }
            ?.let { return it }

        return readEmbedded(track)
    }

    fun saveImported(track: Track, content: String) {
        lyricsDir.mkdirs()
        importedFile(track).writeText(content)
    }

    private fun importedFile(track: Track) = File(lyricsDir, "${trackKey(track)}.lrc")

    private fun trackKey(track: Track): String = if (track.mediaStoreId != 0L) {
        track.mediaStoreId.toString()
    } else {
        track.path.hashCode().toUInt().toString()
    }

    private fun sidecarFiles(track: Track): List<File> {
        val audio = File(track.path)
        val parent = audio.parentFile ?: return emptyList()
        val stem = audio.nameWithoutExtension
        return listOf(
            File(parent, "$stem.lrc"),
            File(parent, "${track.title} - ${track.artist}.lrc"),
            File(parent, "${track.artist} - ${track.title}.lrc")
        ).distinct()
    }

    private fun readEmbedded(track: Track): String? = runCatching {
        val extension = File(track.path).extension.ifBlank { "mp3" }
        val temp = File.createTempFile("tuno-lyrics-", ".$extension", context.cacheDir)
        try {
            context.contentResolver.openInputStream(track.getUri())?.use { input ->
                temp.outputStream().use(input::copyTo)
            } ?: return@runCatching null
            AudioFileIO.read(temp).tag?.getFirst(FieldKey.LYRICS)?.takeIf { it.isNotBlank() }
        } finally {
            temp.delete()
        }
    }.getOrNull()
}
