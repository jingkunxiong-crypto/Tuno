package com.example.tuno.helpers

import com.example.tuno.models.LyricLine

object LrcParser {
    private val timestamp = Regex("""\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?]""")
    private val offset = Regex("""\[offset:([+-]?\d+)]""", RegexOption.IGNORE_CASE)

    fun parse(content: String): List<LyricLine> {
        val offsetMs = offset.find(content)?.groupValues?.getOrNull(1)?.toLongOrNull() ?: 0L
        val synced = buildList {
            content.lineSequence().forEach { rawLine ->
                val matches = timestamp.findAll(rawLine).toList()
                val text = timestamp.replace(rawLine, "").trim()
                if (matches.isNotEmpty() && text.isNotEmpty()) {
                    matches.forEach { match ->
                        val minutes = match.groupValues[1].toLongOrNull() ?: 0L
                        val seconds = match.groupValues[2].toLongOrNull() ?: 0L
                        val fraction = match.groupValues[3]
                        val millis = when (fraction.length) {
                            1 -> fraction.toLong() * 100
                            2 -> fraction.toLong() * 10
                            3 -> fraction.toLong()
                            else -> 0L
                        }
                        add(LyricLine((minutes * 60_000 + seconds * 1_000 + millis + offsetMs).coerceAtLeast(0), text))
                    }
                }
            }
        }.sortedBy { it.timeMs }

        if (synced.isNotEmpty()) return synced

        return content.lineSequence()
            .map(String::trim)
            .filter { it.isNotEmpty() && !it.matches(Regex("""\[[a-zA-Z]+:.*]""")) }
            .map { LyricLine(Long.MAX_VALUE, it) }
            .toList()
    }
}
