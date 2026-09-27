package com.example.tuno.helpers

import android.content.Context
import android.media.AudioFormat
import android.media.MediaCodec
import android.media.MediaExtractor
import android.media.MediaFormat
import android.net.Uri
import com.example.tuno.models.Track
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import java.io.File
import java.nio.ByteOrder
import java.security.MessageDigest
import java.util.Locale
import kotlin.math.sqrt

/** Offline analysis: no microphone access and no dependency on the playback session. */
class AudioEnvelopeRepository(private val context: Context) {
    data class Result(val description: String, val peaks: FloatArray)
    companion object { private val lock = Mutex(); private const val BINS = 2048 }

    private fun cacheFile(track: Track): File {
        val source = File(track.path)
        val identity = "v2:${track.path}:${source.length()}:${source.lastModified()}:${track.duration}"
        val hash = MessageDigest.getInstance("SHA-256").digest(identity.toByteArray()).joinToString("") { "%02x".format(it) }
        val directory = File(context.cacheDir, "waveforms").apply { mkdirs() }
        return File(directory, hash)
    }

    fun cached(track: Track): Result? {
        val cache = cacheFile(track)
        if (cache.exists()) {
            val cached = runCatching { cache.inputStream().buffered().let { java.io.DataInputStream(it) }.use { input ->
                Result(input.readUTF(), FloatArray(BINS) { input.readFloat().also { require(it.isFinite() && it in 0f..1f) } })
            } }.getOrNull()
            if (cached != null) return cached
        }
        return null
    }

    suspend fun load(track: Track, onPartial: suspend (FloatArray) -> Unit = {}, onMetadata: suspend (String) -> Unit): Result = lock.withLock {
        cached(track)?.let { onMetadata(it.description); return@withLock it }
        val cache = cacheFile(track)
        val extractor = MediaExtractor()
        var codec: MediaCodec? = null
        try {
            if (track.path.startsWith("content://")) extractor.setDataSource(context, Uri.parse(track.path), null)
            else extractor.setDataSource(track.path)
            val index = (0 until extractor.trackCount).first { extractor.getTrackFormat(it).getString(MediaFormat.KEY_MIME)?.startsWith("audio/") == true }
            extractor.selectTrack(index)
            val format = extractor.getTrackFormat(index)
            val mime = requireNotNull(format.getString(MediaFormat.KEY_MIME))
            val rate = format.getInteger(MediaFormat.KEY_SAMPLE_RATE)
            val bits = if (format.containsKey(MediaFormat.KEY_BIT_RATE)) format.getInteger(MediaFormat.KEY_BIT_RATE) else 0
            val kind = when (mime) { "audio/mpeg" -> "MP3"; "audio/mp4a-latm" -> "AAC"; "audio/raw" -> "PCM"; else -> mime.substringAfter('/').uppercase(Locale.ROOT) }
            val label = listOfNotNull(String.format(Locale.ROOT, "%s kHz", (rate / 1000.0).toString().removeSuffix(".0")), bits.takeIf { it > 0 }?.let { "${it / 1000} kbps" }, kind).joinToString(" · ")
            onMetadata(label)
            val duration = if (format.containsKey(MediaFormat.KEY_DURATION)) format.getLong(MediaFormat.KEY_DURATION) else track.duration * 1_000_000L
            require(duration > 0)
            val decoder = MediaCodec.createDecoderByType(mime).also { codec = it }
            decoder.configure(format, null, null, 0)
            decoder.start()
            val energy = DoubleArray(BINS)
            val samples = IntArray(BINS)
            fun envelope(): FloatArray {
                val rms = FloatArray(BINS) { if (samples[it] > 0) sqrt(energy[it] / samples[it]).toFloat() else 0f }
                val maximum = rms.maxOrNull()?.coerceAtLeast(.00001f) ?: 1f
                return FloatArray(BINS) { (rms[it] / maximum).coerceIn(0f, 1f) }
            }
            val info = MediaCodec.BufferInfo()
            var inputDone = false
            var outputDone = false
            var channels = format.getInteger(MediaFormat.KEY_CHANNEL_COUNT)
            var sampleRate = rate
            var encoding = AudioFormat.ENCODING_PCM_16BIT
            var lastOutput = android.os.SystemClock.elapsedRealtime()
            var lastPartial = lastOutput
            while (!outputDone) {
                currentCoroutineContext().ensureActive()
                check(android.os.SystemClock.elapsedRealtime() - lastOutput < 15_000) { "Audio decoder stalled" }
                if (!inputDone) {
                    val slot = decoder.dequeueInputBuffer(5000)
                    if (slot >= 0) {
                        val buffer = requireNotNull(decoder.getInputBuffer(slot)).apply { clear() }
                        val size = extractor.readSampleData(buffer, 0)
                        if (size < 0) { decoder.queueInputBuffer(slot, 0, 0, 0, MediaCodec.BUFFER_FLAG_END_OF_STREAM); inputDone = true }
                        else { decoder.queueInputBuffer(slot, 0, size, extractor.sampleTime, 0); extractor.advance() }
                    }
                }
                val slot = decoder.dequeueOutputBuffer(info, 5000)
                if (slot == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                    val output = decoder.outputFormat
                    channels = output.getInteger(MediaFormat.KEY_CHANNEL_COUNT)
                    sampleRate = output.getInteger(MediaFormat.KEY_SAMPLE_RATE)
                    encoding = if (output.containsKey(MediaFormat.KEY_PCM_ENCODING)) output.getInteger(MediaFormat.KEY_PCM_ENCODING) else AudioFormat.ENCODING_PCM_16BIT
                    require(encoding == AudioFormat.ENCODING_PCM_16BIT || encoding == AudioFormat.ENCODING_PCM_FLOAT)
                } else if (slot >= 0) {
                    lastOutput = android.os.SystemClock.elapsedRealtime()
                    val buffer = decoder.getOutputBuffer(slot)
                    if (buffer != null && info.size > 0) {
                        buffer.order(ByteOrder.LITTLE_ENDIAN)
                        val bytes = if (encoding == AudioFormat.ENCODING_PCM_FLOAT) 4 else 2
                        val frames = info.size / (bytes * channels)
                        // Sample all channels every 16 PCM frames; retain time positions across the whole file.
                        for (frame in 0 until frames step 16) {
                            val time = info.presentationTimeUs + frame * 1_000_000L / sampleRate
                            val bin = (time * BINS / duration).toInt().coerceIn(0, BINS - 1)
                            for (channel in 0 until channels) {
                                val offset = info.offset + (frame * channels + channel) * bytes
                                val value = if (bytes == 4) buffer.getFloat(offset).toDouble() else buffer.getShort(offset) / 32768.0
                                if (value.isFinite()) { energy[bin] += value * value; samples[bin]++ }
                            }
                        }
                    }
                    outputDone = info.flags and MediaCodec.BUFFER_FLAG_END_OF_STREAM != 0
                    decoder.releaseOutputBuffer(slot, false)
                    if (lastOutput - lastPartial >= 350) {
                        onPartial(envelope())
                        lastPartial = lastOutput
                    }
                }
            }
            val result = Result(label, envelope())
            currentCoroutineContext().ensureActive()
            runCatching {
                java.io.DataOutputStream(cache.outputStream().buffered()).use { out -> out.writeUTF(label); result.peaks.forEach(out::writeFloat) }
                cache.parentFile?.listFiles()?.sortedByDescending { it.lastModified() }?.drop(160)?.forEach { it.delete() }
            }
            result
        } finally {
            codec?.let { runCatching { it.stop() }; it.release() }
            extractor.release()
        }
    }
}
