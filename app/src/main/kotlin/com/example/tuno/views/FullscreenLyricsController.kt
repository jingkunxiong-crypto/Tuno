package com.example.tuno.views

import android.animation.ValueAnimator
import android.view.MotionEvent
import android.view.View
import android.view.animation.PathInterpolator
import androidx.activity.result.contract.ActivityResultContracts
import androidx.constraintlayout.widget.ConstraintLayout
import com.example.tuno.R
import com.example.tuno.activities.TrackActivity
import com.example.tuno.databinding.ActivityTrackBinding
import com.example.tuno.databinding.ViewFullscreenLyricsBinding
import com.example.tuno.helpers.LrcParser
import com.example.tuno.helpers.LyricsRepository
import com.example.tuno.models.Track
import kotlinx.coroutines.*

class FullscreenLyricsController(private val activity: TrackActivity, private val player: ActivityTrackBinding) {
    private val ui = ViewFullscreenLyricsBinding.inflate(activity.layoutInflater)
    private val repository = LyricsRepository(activity.applicationContext)
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private var loadJob: Job? = null
    private var track: Track? = null
    private var loadedPath: String? = null
    private var animator: ValueAnimator? = null
    private var playing = false
    private var touchY = 0f
    private var startProgress = 0f
    var progress = 0f
        private set
    val active get() = progress > 0f
    private val controls = (0 until player.root.childCount).map { player.root.getChildAt(it) }
        .filter { it !== player.activityTrackArtCard && it !== player.playerBackground }
    private val alphas = controls.associateWith { it.alpha }.toMutableMap()
    private val launcher = activity.registerForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        val selected = track
        if (uri != null && selected != null) {
            loadJob?.cancel()
            loadJob = scope.launch {
                try {
                    val text = withContext(Dispatchers.IO) {
                        activity.contentResolver.openInputStream(uri)?.bufferedReader()?.use { reader ->
                            val result = StringBuilder()
                            val buffer = CharArray(4096)
                            while (true) {
                                val count = reader.read(buffer)
                                if (count < 0) break
                                require(result.length + count <= 1_000_000) { "歌词文件过大" }
                                result.append(buffer, 0, count)
                            }
                            result.toString()
                        }.orEmpty()
                    }
                    require(LrcParser.parse(text).isNotEmpty()) { "没有读到可用歌词" }
                    withContext(Dispatchers.IO) { repository.saveImported(selected, text) }
                    if (track?.path == selected.path) { loadedPath = null; loadJob = null; loadLyrics() }
                } catch (cancelled: CancellationException) { throw cancelled }
                catch (_: Exception) { ui.lyricsMessage.text = "无法读取歌词，请选择 LRC 或文本文件"; ui.lyricsMessage.visibility = View.VISIBLE }
            }
        }
    }

    init {
        player.root.addView(ui.root, ConstraintLayout.LayoutParams(0, 0).apply {
            startToStart = 0; endToEnd = 0; topToTop = 0; bottomToBottom = 0
        })
        ui.root.visibility = View.GONE
        // Above the elevated queue shortcut, below the shared cover (18dp).
        ui.root.translationZ = dp(12).toFloat()
        ui.lyricsImport.setOnClickListener { launcher.launch(arrayOf("text/*", "application/octet-stream", "application/x-subrip")) }
        ui.lyricsMessage.setOnClickListener { ui.lyricsImport.performClick() }
        ui.lyricsFollow.setOnClickListener { ui.fullscreenLyrics.resumeFollowing() }
        ui.fullscreenLyrics.onManualBrowsing = { manual -> ui.lyricsFollow.visibility = if (manual) View.VISIBLE else View.INVISIBLE }
        ui.fullscreenLyrics.setOnLineClickListener { time ->
            activity.withPlayer { seekTo(time) }
            ui.fullscreenLyrics.updatePosition(time, false)
        }
        ui.lyricsMiniPlay.setOnClickListener { activity.togglePlayback() }
        ui.lyricsMiniCover.setOnClickListener { setOpen(false) }
        ui.lyricsHeader.setOnTouchListener { _, event -> handleHeaderTouch(event) }
    }

    fun setTrack(value: Track) {
        if (track?.path == value.path) return
        track = value
        loadJob?.cancel()
        loadedPath = null
        ui.lyricsSong.text = value.title
        ui.lyricsArtist.text = value.artist
        ui.lyricsMiniTitle.text = value.title
        ui.fullscreenLyrics.setLyrics(emptyList())
        if (active) loadLyrics()
    }

    private fun loadLyrics() {
        val selected = track ?: return
        if (loadedPath == selected.path || loadJob?.isActive == true) return
        ui.lyricsMessage.text = "正在读取歌词…"
        ui.lyricsMessage.visibility = View.VISIBLE
        loadJob = scope.launch {
            val lines = try { withContext(Dispatchers.IO) { repository.load(selected)?.let(LrcParser::parse).orEmpty() } }
                catch (cancelled: CancellationException) { throw cancelled }
                catch (_: Exception) { emptyList() }
            if (track?.path != selected.path) return@launch
            loadedPath = selected.path
            ui.fullscreenLyrics.setLyrics(lines)
            ui.lyricsMessage.text = "暂无歌词\n点击这里导入 LRC 歌词"
            ui.lyricsMessage.visibility = if (lines.isEmpty()) View.VISIBLE else View.GONE
            activity.withPlayer { ui.fullscreenLyrics.updatePosition(currentPosition, false) }
        }
    }

    fun update(position: Long, isPlaying: Boolean) {
        playing = isPlaying
        if (!active) return
        ui.fullscreenLyrics.updatePosition(position)
        ui.lyricsMiniPlay.setImageResource(if (playing) R.drawable.tuno_wave_pause else R.drawable.tuno_wave_play)
        ui.lyricsMiniCover.setImageDrawable(player.activityTrackImage.drawable)
        applyProgress(progress)
    }

    fun setOpen(open: Boolean) {
        animator?.cancel()
        if (open) loadLyrics()
        animator = ValueAnimator.ofFloat(progress, if (open) 1f else 0f).apply {
            duration = (340 * kotlin.math.abs((if (open) 1f else 0f) - progress)).toLong().coerceAtLeast(100)
            interpolator = PathInterpolator(.25f, 0f, .25f, 1f)
            addUpdateListener { applyProgress(it.animatedValue as Float) }
            start()
        }
    }

    fun drag(fraction: Float) { animator?.cancel(); if (!active) loadLyrics(); applyProgress(fraction.coerceIn(0f, 1f)) }

    private fun applyProgress(value: Float) {
        if (progress == 0f && value > 0f) controls.forEach { alphas[it] = it.alpha }
        progress = value
        ui.root.visibility = if (value == 0f) View.GONE else View.VISIBLE
        ui.root.alpha = value
        if (value > 0f) { ui.root.bringToFront(); player.activityTrackArtCard.bringToFront() }
        val card = player.activityTrackArtCard
        card.pivotX = 0f; card.pivotY = 0f
        card.scaleX = 1f + (dp(48).toFloat() / card.width.coerceAtLeast(1) - 1f) * value
        card.scaleY = card.scaleX
        card.translationX = (player.root.paddingLeft + dp(20) - card.left) * value
        card.translationY = (player.root.paddingTop + dp(24) - card.top) * value
        controls.forEach { it.alpha = (alphas[it] ?: 1f) * (1f - value) }
        player.nextTrackHolder.visibility = if (value == 1f) View.INVISIBLE else View.VISIBLE
        player.activityTrackTitle.translationY = -dp(60) * value
        player.activityTrackArtist.translationY = -dp(60) * value
        ui.lyricsMiniCover.setImageDrawable(player.activityTrackImage.drawable)
    }

    fun handleHeaderTouch(event: MotionEvent): Boolean {
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> { animator?.cancel(); touchY = event.rawY; startProgress = progress }
            MotionEvent.ACTION_MOVE -> drag(startProgress - (event.rawY - touchY) / dp(260))
            MotionEvent.ACTION_UP -> {
                val delta = event.rawY - touchY
                if (kotlin.math.abs(delta) < dp(8)) setOpen(false) else setOpen(progress > .65f && delta < dp(70))
            }
            MotionEvent.ACTION_CANCEL -> setOpen(startProgress > .5f)
        }
        return true
    }
    fun destroy() { animator?.cancel(); scope.cancel() }
    private fun dp(value: Int) = (value * activity.resources.displayMetrics.density).toInt()
}
