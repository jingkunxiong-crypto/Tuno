package com.example.tuno.activities
import android.view.View
import androidx.core.view.doOnPreDraw

import android.animation.ValueAnimator
import android.annotation.SuppressLint
import android.content.Intent
import android.graphics.drawable.BitmapDrawable
import android.graphics.drawable.Drawable
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.provider.MediaStore
import android.util.Size
import android.view.GestureDetector
import android.view.MotionEvent
import android.view.animation.DecelerateInterpolator
import android.view.animation.PathInterpolator
import android.widget.SeekBar
import androidx.core.graphics.drawable.toDrawable
import androidx.core.graphics.scale
import androidx.core.os.postDelayed
import androidx.core.view.GestureDetectorCompat
import androidx.media3.common.MediaItem
import com.bumptech.glide.load.resource.bitmap.CenterCrop
import com.bumptech.glide.load.resource.bitmap.RoundedCorners
import com.bumptech.glide.request.RequestOptions
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import org.fossify.commons.extensions.applyColorFilter
import org.fossify.commons.extensions.beGone
import org.fossify.commons.extensions.beVisible
import org.fossify.commons.extensions.copyToClipboard
import org.fossify.commons.extensions.getColoredDrawableWithColor
import org.fossify.commons.extensions.getFormattedDuration
import org.fossify.commons.extensions.getProperBackgroundColor
import org.fossify.commons.extensions.getProperPrimaryColor
import org.fossify.commons.extensions.getProperTextColor
import org.fossify.commons.extensions.realScreenSize
import org.fossify.commons.extensions.toast
import org.fossify.commons.extensions.updateTextColors
import org.fossify.commons.extensions.value
import org.fossify.commons.extensions.viewBinding
import org.fossify.commons.helpers.MEDIUM_ALPHA
import com.example.tuno.R
import com.example.tuno.databinding.ActivityTrackBinding
import com.example.tuno.extensions.config
import com.example.tuno.extensions.getPlaybackSetting
import com.example.tuno.extensions.getTrackCoverArt
import com.example.tuno.extensions.getTrackFromUri
import com.example.tuno.extensions.isReallyPlaying
import com.example.tuno.extensions.loadGlideResource
import com.example.tuno.extensions.maybeRestartOnPrevious
import com.example.tuno.extensions.nextMediaItem
import com.example.tuno.extensions.sendCommand
import com.example.tuno.extensions.setRepeatMode
import com.example.tuno.extensions.shuffledMediaItemsIndices
import com.example.tuno.extensions.toMediaItem
import com.example.tuno.extensions.toTrack
import com.example.tuno.fragments.PlaybackSpeedFragment
import com.example.tuno.helpers.PlaybackSetting
import com.example.tuno.helpers.SEEK_INTERVAL_S
import com.example.tuno.interfaces.PlaybackSpeedListener
import com.example.tuno.models.Track
import com.example.tuno.playback.CustomCommands
import com.example.tuno.playback.PlaybackService
import java.text.DecimalFormat
import kotlin.math.abs
import kotlin.time.Duration.Companion.milliseconds

class TrackActivity : SimpleControllerActivity(), PlaybackSpeedListener {
    companion object {
        const val EXTRA_INITIAL_TRACK = "initial_track"
        private const val SWIPE_DOWN_THRESHOLD = 100
        private const val SWIPE_HORIZONTAL_THRESHOLD_DP = 72
        private const val SWIPE_MIN_VELOCITY = 500f
        private const val SEEK_COALESCE_INTERVAL_MS = 150L
        private const val UPDATE_INTERVAL_MS = 150L
        private const val COVER_SIDE_MARGIN_DP = 16
        private const val PLAYER_TRANSITION_MS = 310L
    }

    private var isThirdPartyIntent = false
    private lateinit var nextTrackPlaceholder: Drawable

    private val handler = Handler(Looper.getMainLooper())

    private val scope = CoroutineScope(Dispatchers.Default)
    private val audioAnalysis by lazy { com.example.tuno.helpers.AudioEnvelopeRepository(applicationContext) }
    private var analysisJob: Job? = null
    private val neighborAnalysisJobs = mutableListOf<Job>()
    private var seekJob: Job? = null
    private var seekCount = 0
    private var displayedTrack: Track? = null
    private var lyricsController: com.example.tuno.views.FullscreenLyricsController? = null
    private var isSwipeAnimating = false
    private var switchAnimator: ValueAnimator? = null
    private var switchOffset = 0f
    private var queuedSwitches = 0
    private var switchBlend: com.example.tuno.views.CoverBlendDrawable? = null
    private var switchDirection = 0
    private var draggingPage = false
    private data class NeighborPage(val direction: Int, val track: Track, val ui: ActivityTrackBinding)
    private val neighborPages = mutableListOf<NeighborPage>()
    private var neighborKey = ""

    private val binding by viewBinding(ActivityTrackBinding::inflate)
    private var enteringFromCover = false
    private var pendingCoverUpdate: (() -> Unit)? = null
    private var playerMotionAnimator: ValueAnimator? = null
    private var playerMotionTarget: Float? = null
    private var playerMotionProgress = 1f
    private var motionScene: com.example.tuno.helpers.CoverTransitionPreview.Scene? = null
    private var motionBackdrop: android.widget.ImageView? = null
    private var motionBottomBar: android.widget.ImageView? = null
    private var titleStartX = 0f
    private var titleStartY = 0f
    private var titleStartScale = 1f
    private var closingPlayer = false
    private var coverStartX = 0f
    private var coverStartY = 0f
    private var coverStartScale = 1f
    private val timelineMotionViews by lazy {
        arrayOf<View>(
            binding.activityTrackProgressbar,
            binding.activityTrackProgressCurrent,
            binding.activityTrackProgressMax,
            binding.activityTrackSpeed,
            binding.activityTrackSpeedClickArea,
            binding.audioQuality
        )
    }
    private val metadataMotionViews by lazy {
        arrayOf<View>(binding.activityTrackTitle, binding.activityTrackArtist)
    }
    private val controlMotionViews by lazy {
        arrayOf<View>(
            binding.activityTrackToggleShuffle,
            binding.activityTrackPrevious,
            binding.activityTrackPlayPause,
            binding.activityTrackNext,
            binding.activityTrackPlaybackSetting
        )
    }
    private val panelMotionViews by lazy {
        arrayOf<View>(binding.nextTrackHolder)
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        fun coverTransition(
            started: () -> Unit,
            finished: () -> Unit = {}
        ) = android.transition.TransitionSet().apply {
            ordering = android.transition.TransitionSet.ORDERING_TOGETHER
            addTransition(android.transition.ChangeBounds())
            addTransition(android.transition.ChangeTransform())
            addTransition(android.transition.ChangeImageTransform())
            duration = PLAYER_TRANSITION_MS
            interpolator = PathInterpolator(0.32f, 0f, 0.25f, 1f)
            addListener(object : android.transition.Transition.TransitionListener {
                override fun onTransitionStart(transition: android.transition.Transition) = started()
                override fun onTransitionEnd(transition: android.transition.Transition) = finished()
                override fun onTransitionCancel(transition: android.transition.Transition) = finished()
                override fun onTransitionPause(transition: android.transition.Transition) {}
                override fun onTransitionResume(transition: android.transition.Transition) {}
            })
        }
        window.sharedElementEnterTransition = coverTransition(::startPlayerEntrance, ::finishCoverEntry)
        window.sharedElementReturnTransition = coverTransition(::startPlayerExit)
        window.allowEnterTransitionOverlap = true
        window.allowReturnTransitionOverlap = true
        window.enterTransition = android.transition.Fade().apply { duration = 170L }
        window.returnTransition = android.transition.Fade().apply { duration = 210L }
        setContentView(binding.root)
        motionScene = com.example.tuno.helpers.CoverTransitionPreview.takeScene()
        motionScene?.let { scene ->
            binding.root.background = null
            val parent = binding.root.parent as android.view.ViewGroup
            motionBackdrop = android.widget.ImageView(this).apply {
                scaleType = android.widget.ImageView.ScaleType.FIT_XY
                setImageBitmap(scene.bitmap)
                importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
                parent.addView(this, 0, android.view.ViewGroup.LayoutParams(-1, -1))
            }
            scene.barBounds?.let { rect ->
                motionBottomBar = android.widget.ImageView(this).apply {
                    scaleType = android.widget.ImageView.ScaleType.FIT_XY
                    setImageBitmap(scene.bar)
                    importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
                    parent.addView(this, 1, android.widget.FrameLayout.LayoutParams(rect.width().toInt(), rect.height().toInt()).apply {
                        leftMargin = rect.left.toInt()
                        topMargin = rect.top.toInt() - scene.originY
                    })
                }
            }
        }
        val shadowCornerFraction = intent.getFloatExtra("cover_corner_fraction",
            resources.getDimension(org.fossify.commons.R.dimen.rounded_corner_radius_small) / dp(52))
        binding.activityTrackArtCard.addOnLayoutChangeListener { view, _, _, _, _, _, _, _, _ ->
            binding.activityTrackArtCard.radius = view.width * shadowCornerFraction
        }
        if (android.os.Build.VERSION.SDK_INT >= 28) {
            binding.activityTrackArtCard.outlineAmbientShadowColor = android.graphics.Color.BLACK
            binding.activityTrackArtCard.outlineSpotShadowColor = android.graphics.Color.BLACK
        }
        if (motionScene == null) binding.root.setBackgroundResource(R.drawable.tuno_graphite_background)
        binding.playerBackground.setBackgroundResource(R.drawable.tuno_graphite_background)
        styleGlassPanels(binding)
        intent.getStringExtra("cover_transition")?.let {
            enteringFromCover = true
            setPlayerMotionProgress(0f)
            binding.activityTrackImage.transitionName = it
            com.example.tuno.helpers.CoverTransitionPreview.take(it)?.let { preview ->
                binding.activityTrackImage.setImageDrawable(preview)
                binding.playerBackground.background = com.example.tuno.views.CoverGlassDrawable(preview)
            }
            binding.activityTrackImage.doOnPreDraw {
                if (closingPlayer) return@doOnPreDraw
                motionScene?.let { scene ->
                    val location = IntArray(2)
                    binding.activityTrackArtCard.getLocationOnScreen(location)
                    coverStartX = scene.cover.left - location[0]
                    coverStartY = scene.cover.top - location[1]
                    coverStartScale = scene.cover.width() / binding.activityTrackArtCard.width
                    binding.activityTrackArtCard.pivotX = 0f
                    binding.activityTrackArtCard.pivotY = 0f
                    motionBackdrop?.pivotX = scene.cover.centerX()
                    motionBackdrop?.pivotY = scene.cover.centerY() - scene.originY
                    scene.title?.let { rect ->
                        binding.activityTrackTitle.getLocationOnScreen(location)
                        titleStartScale = scene.titleSize / binding.activityTrackTitle.textSize
                        titleStartX = rect.centerX() - (location[0] + binding.activityTrackTitle.width / 2f)
                        titleStartY = rect.centerY() - (location[1] + binding.activityTrackTitle.height / 2f)
                    }
                }
                setPlayerMotionProgress(0f)
                startPlayerEntrance()
            }
        }
        setupEdgeToEdge(
            padTopSystem = listOf(binding.activityTrackHolder),
            padBottomSystem = listOf(binding.root)
        )
        nextTrackPlaceholder =
            resources.getColoredDrawableWithColor(R.drawable.ic_headset, getProperTextColor())
        setupButtons()
        lyricsController = com.example.tuno.views.FullscreenLyricsController(this, binding)
        setupFlingListener()

        binding.apply {
            activityTrackAppbar.beGone()
            activityTrackImage.setOnClickListener {
                if (lyricsController?.active == true) lyricsController?.setOpen(false) else closePlayer()
            }
            activityTrackImage.contentDescription = getString(R.string.tuno_cover_return)
            nextTrackHolder.setOnClickListener {
                startActivity(Intent(applicationContext, QueueActivity::class.java))
            }

            isThirdPartyIntent = intent.action == Intent.ACTION_VIEW
            if (isThirdPartyIntent && (savedInstanceState == null || PlaybackService.currentMediaItem == null)) {
                initThirdPartyIntent()
                return
            }

            val initialTrack = getInitialTrack()
            setupTrackInfo(initialTrack?.toMediaItem() ?: PlaybackService.currentMediaItem)
            setupNextTrackInfo(PlaybackService.nextMediaItem)
            updatePlayPause(PlaybackService.isPlaying)
            updatePlayerState()
        }
    }

    override fun onResume() {
        super.onResume()
        updateTextColors(binding.activityTrackHolder)
        binding.activityTrackTitle.setTextColor(getProperTextColor())
        binding.activityTrackArtist.setTextColor(getProperTextColor())
        updatePlayerState()
        updateTrackInfo()
    }

    override fun onPause() {
        super.onPause()
        cancelProgressUpdate()
    }

    override fun onStop() {
        super.onStop()
        binding.playbackModeHint.removeCallbacks(hidePlaybackModeHint)
        binding.playbackModeHint.visibility = View.GONE
        cancelProgressUpdate()
    }

    override fun onDestroy() {
        super.onDestroy()
        lyricsController?.destroy()
        switchAnimator?.cancel()
        analysisJob?.cancel()
        neighborAnalysisJobs.forEach { it.cancel() }
        playerMotionAnimator?.cancel()
        cancelProgressUpdate()
        if (isThirdPartyIntent && !isChangingConfigurations) {
            withPlayer {
                if (!isReallyPlaying) {
                    sendCommand(CustomCommands.CLOSE_PLAYER)
                }
            }
        }
    }

    private fun finishCoverEntry() {
        if (!enteringFromCover) return
        enteringFromCover = false
        playerMotionAnimator?.cancel()
        playerMotionTarget = null
        setPlayerMotionProgress(1f)
        pendingCoverUpdate?.invoke()
        pendingCoverUpdate = null
        if (!isFinishing && !isDestroyed) {
            binding.root.postDelayed({ prepareNeighborPages() }, 140L)
        }
    }

    @Suppress("DEPRECATION")
    private fun getInitialTrack(): Track? = if (android.os.Build.VERSION.SDK_INT >= 33) {
        intent.getSerializableExtra(EXTRA_INITIAL_TRACK, Track::class.java)
    } else {
        intent.getSerializableExtra(EXTRA_INITIAL_TRACK) as? Track
    }

    private fun startPlayerEntrance() = animatePlayerMotionTo(1f)

    private fun startPlayerExit() = animatePlayerMotionTo(0f)

    private fun closePlayer() {
        if (closingPlayer) return
        queuedSwitches = 0
        if (switchAnimator?.isRunning == true) switchAnimator?.end()
        binding.activityTrackArtCard.pivotX = 0f
        binding.activityTrackArtCard.pivotY = 0f
        if (motionScene == null) { finishAfterTransition(); return }
        closingPlayer = true
        enteringFromCover = false
        pendingCoverUpdate = null
        neighborPages.forEach { it.ui.root.visibility = View.GONE }
        if (playerMotionProgress <= 0f) {
            playerMotionAnimator?.cancel()
            finish()
            @Suppress("DEPRECATION")
            overridePendingTransition(0, 0)
            return
        }
        startPlayerExit()
    }

    private fun animatePlayerMotionTo(target: Float) {
        if (playerMotionAnimator?.isRunning == true && playerMotionTarget == target) return
        if (playerMotionProgress == target && playerMotionAnimator?.isRunning != true) return
        playerMotionAnimator?.cancel()
        playerMotionTarget = target
        val distance = kotlin.math.abs(target - playerMotionProgress)
        playerMotionAnimator = ValueAnimator.ofFloat(playerMotionProgress, target).apply {
            duration = (PLAYER_TRANSITION_MS * distance).toLong().coerceAtLeast(1L)
            interpolator = PathInterpolator(0.32f, 0f, 0.25f, 1f)
            addUpdateListener { setPlayerMotionProgress(it.animatedValue as Float) }
            addListener(object : android.animation.AnimatorListenerAdapter() {
                private var cancelled = false
                override fun onAnimationCancel(animation: android.animation.Animator) { cancelled = true }
                override fun onAnimationEnd(animation: android.animation.Animator) {
                    if (cancelled) return
                    if (target == 1f) finishCoverEntry() else if (closingPlayer) {
                        finish()
                        @Suppress("DEPRECATION")
                        overridePendingTransition(0, 0)
                    }
                }
            })
            start()
        }
    }

    private fun setPlayerMotionProgress(progress: Float) {
        playerMotionProgress = progress.coerceIn(0f, 1f)
        if (motionScene != null) {
            val p = playerMotionProgress
            binding.activityTrackArtCard.apply {
                translationX = coverStartX * (1f - p)
                translationY = coverStartY * (1f - p)
                scaleX = coverStartScale + (1f - coverStartScale) * p
                scaleY = scaleX
                // Keep the shadow cached while only the cover transform changes.
            }
            motionBackdrop?.apply {
                scaleX = 1f + 1.4f * p
                scaleY = scaleX
                alpha = 1f - ((p - 0.20f) / 0.65f).coerceIn(0f, 1f)
            }
            motionBottomBar?.apply {
                val disappear = (p / 0.72f).coerceIn(0f, 1f)
                translationY = dp(80) * disappear
                alpha = 1f - disappear
            }
        }
        fun interval(start: Float, end: Float): Float =
            ((playerMotionProgress - start) / (end - start)).coerceIn(0f, 1f)

        val background = interval(0.04f, 0.62f)
        val timeline = interval(0.18f, 0.68f)
        val metadata = interval(0.27f, 0.78f)
        val controls = interval(0.40f, 0.90f)
        val panels = interval(0.50f, 1f)

        binding.playerBackground.alpha = background
        timelineMotionViews.forEach {
            it.alpha = timeline
            it.translationY = dp(8) * (1f - timeline)
        }
        metadataMotionViews.forEach {
            it.alpha = metadata
            it.translationY = dp(12) * (1f - metadata)
        }
        if (motionScene?.title != null) {
            binding.activityTrackTitle.apply {
                alpha = 1f
                translationX = titleStartX * (1f - playerMotionProgress)
                translationY = titleStartY * (1f - playerMotionProgress)
                scaleX = titleStartScale + (1f - titleStartScale) * playerMotionProgress
                scaleY = scaleX
            }
        }
        controlMotionViews.forEach {
            it.alpha = controls
            it.scaleX = 0.90f + controls * 0.10f
            it.scaleY = 0.90f + controls * 0.10f
        }
        panelMotionViews.forEach {
            it.alpha = panels
            it.translationY = dp(16) * (1f - panels)
        }
    }

    private fun setupTrackInfo(item: MediaItem?) {
        if (isSwipeAnimating) return
        val track = item?.toTrack() ?: return
        binding.root.post { if (!enteringFromCover) prepareNeighborPages() }

        if (displayedTrack?.path != track.path) {
            lyricsController?.setTrack(track)
            setupTopArt(track)
            displayedTrack = track
            loadAudioAnalysis(track)
        }
        binding.apply {
            activityTrackTitle.text = track.title
            activityTrackArtist.text = track.artist
            activityTrackTitle.setOnLongClickListener {
                copyToClipboard(activityTrackTitle.value)
                true
            }

            activityTrackArtist.setOnLongClickListener {
                copyToClipboard(activityTrackArtist.value)
                true
            }

            activityTrackProgressbar.max = track.duration
            activityTrackProgressMax.text = track.duration.getFormattedDuration()
        }
    }

    private fun initThirdPartyIntent() {
        getTrackFromUri(intent.data) { track ->
            runOnUiThread {
                if (track != null) {
                    prepareAndPlay(listOf(track), startActivity = false)
                } else {
                    toast(org.fossify.commons.R.string.unknown_error_occurred)
                    finish()
                }
            }
        }
    }

    private fun setupButtons() = binding.apply {
        activityTrackToggleShuffle.setOnClickListener { toggleShuffle() }
        activityTrackPrevious.setOnClickListener { seekToPrevious() }
        activityTrackPlayPause.setOnClickListener { togglePlayback() }
        activityTrackNext.setOnClickListener { seekToNext() }
        activityTrackProgressCurrent.setOnClickListener { seekBack() }
        activityTrackProgressMax.setOnClickListener { seekForward() }
        activityTrackPlaybackSetting.setOnClickListener { togglePlaybackSetting() }
        activityTrackSpeedClickArea.setOnClickListener { showPlaybackSpeedPicker() }
        setupShuffleButton()
        setupPlaybackSettingButton()
        setupSeekbar()

        arrayOf(activityTrackPrevious, activityTrackPlayPause, activityTrackNext).forEach {
            it.applyColorFilter(getProperTextColor())
        }
    }

    private fun loadAudioAnalysis(track: Track) {
        val preview = neighborPages.firstOrNull { it.track.path == track.path && it.ui.activityTrackProgressbar.peaks != null }?.ui
        analysisJob?.cancel()
        neighborAnalysisJobs.forEach { it.cancel() }
        neighborAnalysisJobs.clear()
        binding.activityTrackProgressbar.peaks = preview?.activityTrackProgressbar?.peaks
        binding.audioQuality.text = preview?.audioQuality?.text ?: ""
        binding.audioQuality.alpha = if (preview == null) 0f else 1f
        analysisJob = scope.launch(Dispatchers.IO) {
            // Let navigation establish its first frames before starting an independent decoder.
            if (audioAnalysis.cached(track) == null) kotlinx.coroutines.delay(160)
            try {
                val result = audioAnalysis.load(track, onPartial = { peaks ->
                    withContext(Dispatchers.Main) {
                        if (displayedTrack?.path == track.path && !isDestroyed) binding.activityTrackProgressbar.peaks = peaks
                    }
                }) { label ->
                    withContext(Dispatchers.Main) {
                        if (displayedTrack?.path == track.path && !isDestroyed) {
                            binding.audioQuality.text = label
                            binding.audioQuality.alpha = 1f
                        }
                    }
                }
                withContext(Dispatchers.Main) {
                    if (displayedTrack?.path == track.path && !isDestroyed) {
                        binding.activityTrackProgressbar.peaks = result.peaks

                    }
                }
            } catch (cancelled: kotlinx.coroutines.CancellationException) {
                throw cancelled
            } catch (error: Exception) {
                android.util.Log.w("TunoWaveform", "Audio envelope unavailable", error)
            }
        }
    }

    private fun loadNeighborAnalysis(neighbor: NeighborPage) {
        neighborAnalysisJobs += scope.launch(Dispatchers.IO) {
            try {
                // Prepare uncached neighbors off the UI thread; publish partial envelopes while decoding.
                val result = audioAnalysis.load(neighbor.track, onPartial = { peaks ->
                    withContext(Dispatchers.Main) {
                        if (!isDestroyed && neighborPages.contains(neighbor)) {
                            neighbor.ui.activityTrackProgressbar.peaks = peaks
                            if (switchOffset != 0f) movePages(switchOffset)
                        }
                    }
                }) { }
                withContext(Dispatchers.Main) {
                    if (!isDestroyed && neighborPages.contains(neighbor)) {
                        neighbor.ui.activityTrackProgressbar.max = neighbor.track.duration
                        neighbor.ui.activityTrackProgressbar.peaks = result.peaks
                        neighbor.ui.audioQuality.text = result.description
                    }
                }
            } catch (cancelled: kotlinx.coroutines.CancellationException) {
                throw cancelled
            } catch (_: Exception) { /* The preview remains a usable progress line. */ }
        }
    }

    override fun onBackPressedCompat(): Boolean {
        if (lyricsController?.active == true) {
            lyricsController?.setOpen(false)
            return true
        }
        closePlayer()
        return true
    }

    private fun setupNextTrackInfo(item: MediaItem?) {
        val track = item?.toTrack()
        if (track == null) {
            binding.nextTrackHolder.beGone()
            return
        }

        binding.nextTrackHolder.beVisible()
        val artist =
            if (track.artist.trim().isNotEmpty() && track.artist != MediaStore.UNKNOWN_STRING) {
                " • ${track.artist}"
            } else {
                ""
            }

        @SuppressLint("SetTextI18n")
        binding.nextTrackLabel.text = "${getString(R.string.next_track)} ${track.title}$artist"

        getTrackCoverArt(track) { coverArt ->
            val cornerRadius =
                resources.getDimension(org.fossify.commons.R.dimen.rounded_corner_radius_small)
                    .toInt()
            val wantedSize = resources.getDimension(R.dimen.song_image_size).toInt()

            // change cover image manually only once loaded successfully to avoid blinking at fails and placeholders
            loadGlideResource(
                model = coverArt,
                options = RequestOptions().transform(CenterCrop(), RoundedCorners(cornerRadius)),
                size = Size(wantedSize, wantedSize),
                onLoadFailed = {
                    runOnUiThread {
                        binding.nextTrackImage.setImageDrawable(nextTrackPlaceholder)
                    }
                },
                onResourceReady = {
                    runOnUiThread {
                        binding.nextTrackImage.setImageDrawable(it)
                    }
                }
            )
        }
    }

    private fun setupTopArt(track: Track) {
        neighborPages.firstOrNull { it.track.path == track.path && it.ui.playerBackground.background is com.example.tuno.views.CoverGlassDrawable }?.let {
            binding.activityTrackImage.setImageDrawable(it.ui.activityTrackImage.drawable)
            binding.playerBackground.background = it.ui.playerBackground.background
            binding.activityTrackImage.post { startPostponedEnterTransition() }
            return
        }
        getTrackCoverArt(track) { coverArt ->
            val wantedSize = realScreenSize.x - dp(COVER_SIDE_MARGIN_DP * 2)
            // Preserve the list thumbnail's corner proportions throughout shared-element scaling.
            val defaultCornerFraction = resources.getDimension(org.fossify.commons.R.dimen.rounded_corner_radius_small) / dp(52)
            val cornerFraction = intent.getFloatExtra("cover_corner_fraction", defaultCornerFraction)
            val coverCornerRadius = (wantedSize * cornerFraction).toInt().coerceAtLeast(1)

            // change cover image manually only once loaded successfully to avoid blinking at fails and placeholders
            loadGlideResource(
                model = coverArt,
                options = RequestOptions().transform(CenterCrop(), RoundedCorners(coverCornerRadius)),
                size = Size(wantedSize, wantedSize),
                onLoadFailed = {
                    val drawable = resources.getDrawable(R.drawable.ic_headset)
                    val placeholder = getResizedDrawable(drawable, wantedSize)
                    placeholder.applyColorFilter(getProperTextColor())

                    runOnUiThread {
                        val applyCover = {
                            if (displayedTrack?.path == track.path && !isDestroyed && !closingPlayer) {
                                binding.activityTrackImage.setImageDrawable(placeholder)
                                binding.playerBackground.setBackgroundResource(R.drawable.tuno_graphite_background)
                            }
                        }
                        if (enteringFromCover) pendingCoverUpdate = applyCover else applyCover()
                    }
                },
                onResourceReady = {
                    runOnUiThread {
                        val applyCover = {
                            if (displayedTrack?.path == track.path && !isDestroyed && !closingPlayer) {
                                binding.activityTrackImage.setImageDrawable(it)
                                binding.playerBackground.background = com.example.tuno.views.CoverGlassDrawable(it)
                            }
                        }
                        if (enteringFromCover) pendingCoverUpdate = applyCover else applyCover()
                    }
                }
            )
        }
    }

    @SuppressLint("ClickableViewAccessibility")
    private fun setupFlingListener() {
        val slop = android.view.ViewConfiguration.get(this).scaledTouchSlop
        fun listener(onTap: (() -> Unit)? = null): android.view.View.OnTouchListener {
            var startX = 0f
            var startY = 0f
            var tracking = false
            var horizontal = false
            var vertical = false
            var allowSwipe = false
            var lyricsTouch = false
            var velocity: android.view.VelocityTracker? = null
            return android.view.View.OnTouchListener { _, event ->
                if (event.actionMasked == MotionEvent.ACTION_DOWN) lyricsTouch = lyricsController?.active == true
                if (lyricsTouch) return@OnTouchListener lyricsController?.handleHeaderTouch(event) ?: true
                if (event.actionMasked == MotionEvent.ACTION_DOWN) {
                    velocity?.recycle()
                    velocity = null
                    tracking = !isSwipeAnimating && !closingPlayer
                    allowSwipe = !enteringFromCover
                    horizontal = false
                    vertical = false
                    startX = event.rawX
                    startY = event.rawY
                    if (tracking) velocity = android.view.VelocityTracker.obtain()
                }
                if (!tracking) return@OnTouchListener true
                // Screen coordinates stay stable while the touched view moves with the page.
                val sample = MotionEvent.obtain(event)
                sample.setLocation(event.rawX, event.rawY)
                velocity?.addMovement(sample)
                sample.recycle()
                val dx = event.rawX - startX
                val dy = event.rawY - startY
                when (event.actionMasked) {
                    MotionEvent.ACTION_MOVE -> {
                        if (allowSwipe && !horizontal && !vertical && (abs(dx) > slop || abs(dy) > slop)) {
                            horizontal = abs(dx) > abs(dy) * 1.25f
                            vertical = !horizontal
                        }
                        if (horizontal) {
                            draggingPage = true
                            movePages(dx.coerceIn(-binding.root.width.toFloat(), binding.root.width.toFloat()))
                        } else if (vertical && onTap != null && dy < 0 && allowSwipe) {
                            lyricsController?.drag(-dy / dp(260))
                        }
                    }
                    MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                        velocity?.computeCurrentVelocity(1000)
                        val speed = velocity?.xVelocity ?: 0f
                        val cancelled = event.actionMasked == MotionEvent.ACTION_CANCEL
                        draggingPage = false
                        if (horizontal) {
                            val commit = !cancelled && (abs(dx) > binding.root.width * 0.22f ||
                                (abs(dx) > dp(24) && abs(speed) > dp(500) && speed * dx > 0))
                            if (commit) {
                                animateTrackSwipe(next = dx < 0)
                            } else {
                                settlePages(0f) { }
                            }
                        } else if (onTap != null && vertical && (dy < 0 || lyricsController?.active == true)) {
                            lyricsController?.setOpen(!cancelled && -dy > dp(65))
                        } else if (!cancelled && dy > dp(80) && abs(dy) > abs(dx)) {
                            closePlayer()
                        } else if (!cancelled && abs(dx) <= slop && abs(dy) <= slop) {
                            onTap?.invoke()
                        }
                        tracking = false
                        velocity?.recycle()
                        velocity = null
                    }
                }
                true
            }
        }
        binding.activityTrackHolder.setOnTouchListener(listener())
        binding.activityTrackImage.setOnTouchListener(listener { binding.activityTrackImage.performClick() })
    }

    private fun animateTrackSwipe(next: Boolean) {
        if (closingPlayer || binding.root.width == 0) return
        if (enteringFromCover) finishCoverEntry()
        val direction = if (next) 1 else -1
        if (isSwipeAnimating) {
            queuedSwitches = (queuedSwitches + direction).coerceIn(-8, 8)
            return
        }
        prepareNeighborPages()
        val target = neighborPages.firstOrNull { it.direction == direction }
        if (target == null) { settlePages(0f) { }; return }
        // Submit playback immediately, while the old visual is retained for the outgoing card.
        isSwipeAnimating = true
        seekJob?.cancel()
        seekCount = 0
        seekByCount(direction)
        settlePages(-direction * binding.root.width.toFloat()) {
            isSwipeAnimating = false
            setupTrackInfo(target.track.toMediaItem())
            binding.activityTrackImage.setImageDrawable(target.ui.activityTrackImage.drawable)
            binding.playerBackground.background = target.ui.playerBackground.background
            switchBlend = null
            switchDirection = 0
            movePages(0f)
            withPlayer { setupNextTrackInfo(nextMediaItem); updatePlayerState() }
            neighborKey = ""
            prepareNeighborPages()
            if (queuedSwitches != 0 && !closingPlayer) {
                val queuedDirection = if (queuedSwitches > 0) 1 else -1
                queuedSwitches -= queuedDirection
                binding.root.post { animateTrackSwipe(queuedDirection > 0) }
            }
        }
    }

    private fun transformSong(ui: ActivityTrackBinding, position: Float) {
        val distance = abs(position).coerceIn(0f, 1f)
        ui.activityTrackArtCard.apply {
            pivotX = width / 2f
            pivotY = height / 2f
            cameraDistance = dp(2400).toFloat()
            translationX = position * binding.root.width * .86f
            rotationY = -position * 26f
            scaleX = 1f - .13f * distance
            scaleY = scaleX
            alpha = 1f - .85f * distance
        }
        listOf(ui.activityTrackTitle, ui.activityTrackArtist).forEach {
            it.translationX = position * binding.root.width * .64f
            it.alpha = (1f - distance * 1.6f).coerceIn(0f, 1f)
        }
    }

    private fun movePages(offset: Float) {
        switchOffset = offset
        val position = offset / binding.root.width.coerceAtLeast(1)
        val direction = if (offset < 0) 1 else -1
        val target = neighborPages.firstOrNull { it.direction == direction }
        transformSong(binding, position)
        neighborPages.forEach {
            it.ui.root.visibility = if (offset != 0f && it.direction == direction) View.VISIBLE else View.INVISIBLE
            transformSong(it.ui, position + it.direction)
        }
        if (offset != 0f && target != null) {
            if (switchDirection != direction || switchBlend == null) {
                val previous = switchBlend?.from ?: binding.playerBackground.background
                switchBlend = com.example.tuno.views.CoverBlendDrawable(previous, target.ui.playerBackground.background)
                switchDirection = direction
                binding.playerBackground.background = switchBlend
            }
            switchBlend?.progress = abs(position)
        } else {
            switchBlend?.let { binding.playerBackground.background = it.from }
            switchBlend = null
            switchDirection = 0
        }
        binding.activityTrackProgressbar.apply {
            alpha = 1f
            scaleY = 1f
            setTransition(target?.ui?.activityTrackProgressbar?.peaks, target?.track?.duration ?: 1, abs(position).coerceIn(0f, 1f))
        }
    }

    private fun settlePages(target: Float, complete: () -> Unit) {
        switchAnimator?.cancel()
        isSwipeAnimating = true
        binding.activityTrackProgressbar.animate().cancel()
        switchAnimator = ValueAnimator.ofFloat(switchOffset, target).apply {
            duration = (330 * abs(target - switchOffset) / binding.root.width.coerceAtLeast(1)).toLong().coerceIn(100L, 330L)
            interpolator = PathInterpolator(.25f, 0f, .25f, 1f)
            addUpdateListener { movePages(it.animatedValue as Float) }
            addListener(object : android.animation.AnimatorListenerAdapter() {
                private var cancelled = false
                override fun onAnimationCancel(animation: android.animation.Animator) { cancelled = true }
                override fun onAnimationEnd(animation: android.animation.Animator) {
                    if (cancelled) return
                    isSwipeAnimating = false
                    complete()
                }
            })
            start()
        }
    }
    private fun prepareNeighborPages() {
        if (lyricsController?.active == true) return
        if (isFinishing || isDestroyed || isSwipeAnimating || draggingPage || binding.root.width == 0) return
        withPlayer {
            if (mediaItemCount == 0) return@withPlayer
            val order = if (shuffleModeEnabled) shuffledMediaItemsIndices else (0 until mediaItemCount).toList()
            val position = order.indexOf(currentMediaItemIndex)
            if (position < 0) return@withPlayer
            val tracks = listOf(-1, 1).mapNotNull { direction ->
                getMediaItemAt(order[rotateIndex(order.size, position + direction)]).toTrack()?.let { direction to it }
            }
            val key = "$currentMediaItemIndex/" + tracks.joinToString { it.second.path }
            if (neighborKey == key) return@withPlayer
            neighborKey = key
            neighborAnalysisJobs.forEach { it.cancel() }
            neighborAnalysisJobs.clear()
            val parent = binding.root.parent as android.view.ViewGroup
            neighborPages.forEach { parent.removeView(it.ui.root) }
            neighborPages.clear()
            tracks.sortedByDescending { it.first }.forEach { (direction, track) ->
                val ui = ActivityTrackBinding.inflate(layoutInflater)
                val neighbor = NeighborPage(direction, track, ui)
                neighborPages.add(neighbor)
                ui.root.setPadding(binding.root.paddingLeft, binding.root.paddingTop, binding.root.paddingRight, binding.root.paddingBottom)
                ui.root.background = null
                ui.playerBackground.setBackgroundResource(R.drawable.tuno_graphite_background)
                ui.root.importantForAccessibility = android.view.View.IMPORTANT_FOR_ACCESSIBILITY_NO_HIDE_DESCENDANTS
                ui.root.visibility = View.INVISIBLE
                parent.addView(ui.root, android.view.ViewGroup.LayoutParams(binding.root.width, binding.root.height))
                for (childIndex in 0 until ui.root.childCount) {
                    val child = ui.root.getChildAt(childIndex)
                    if (child !== ui.activityTrackArtCard && child !== ui.activityTrackTitle && child !== ui.activityTrackArtist) child.visibility = View.INVISIBLE
                }
                ui.root.measure(View.MeasureSpec.makeMeasureSpec(binding.root.width, View.MeasureSpec.EXACTLY), View.MeasureSpec.makeMeasureSpec(binding.root.height, View.MeasureSpec.EXACTLY))
                ui.root.layout(0, 0, binding.root.width, binding.root.height)
                updateTextColors(ui.root)
                ui.activityTrackAppbar.beGone()
                ui.activityTrackTitle.text = track.title
                ui.activityTrackArtist.text = track.artist
                ui.activityTrackProgressMax.text = track.duration.getFormattedDuration()
                ui.activityTrackSpeed.text = binding.activityTrackSpeed.text
                ui.activityTrackPlayPause.setImageResource(R.drawable.tuno_wave_pause)
                listOf(ui.activityTrackPrevious, ui.activityTrackNext, ui.activityTrackToggleShuffle, ui.activityTrackPlaybackSetting).forEach { icon ->
                    icon.applyColorFilter(getProperTextColor())

                }
                ui.activityTrackArtCard.layoutParams = ui.activityTrackArtCard.layoutParams.apply {
                    width = binding.activityTrackArtCard.width; height = binding.activityTrackArtCard.height
                }
                ui.activityTrackArtCard.radius = binding.activityTrackArtCard.radius
                loadNeighborAnalysis(neighbor)
                styleGlassPanels(ui)
                ui.nextTrackHolder.setPadding(0, 0, 0, binding.nextTrackHolder.paddingBottom)
                val nextTrack = getMediaItemAt(order[rotateIndex(order.size, position + direction + 1)]).toTrack() ?: track
                ui.nextTrackLabel.text = "下一首： ${nextTrack.title} · ${nextTrack.artist}"
                val size = binding.activityTrackArtCard.width.coerceAtLeast(dp(148))
                getTrackCoverArt(track) { art ->
                    loadGlideResource(art, RequestOptions().transform(CenterCrop(), RoundedCorners(binding.activityTrackArtCard.radius.toInt().coerceAtLeast(1))), Size(size, size),
                        onLoadFailed = {}, onResourceReady = { drawable ->
                            runOnUiThread {
                                if (neighborPages.contains(neighbor)) {
                                    ui.activityTrackImage.setImageDrawable(drawable)
                                    ui.playerBackground.background = com.example.tuno.views.CoverGlassDrawable(drawable)
                                }
                            }
                        })
                }

            }
        }
    }

    private fun styleGlassPanels(ui: ActivityTrackBinding) {
        // Native elevation shadows show through this translucent card as a rectangular patch.
        ui.nextTrackHolder.setBackgroundResource(R.drawable.tuno_floating_glass)
        ui.nextTrackHolder.elevation = dp(10).toFloat()
    }

    private fun toggleShuffle() {
        val isShuffleEnabled = !config.isShuffleEnabled
        config.isShuffleEnabled = isShuffleEnabled
        showPlaybackModeHint(if (isShuffleEnabled) R.string.shuffle_enabled else R.string.shuffle_disabled)
        setupShuffleButton()
        withPlayer {
            shuffleModeEnabled = config.isShuffleEnabled
            setupNextTrackInfo(nextMediaItem)
        }
    }

    private fun setupShuffleButton(isShuffleEnabled: Boolean = config.isShuffleEnabled) {
        binding.activityTrackToggleShuffle.apply {
            applyColorFilter(if (isShuffleEnabled) getProperPrimaryColor() else getProperTextColor())
            alpha = if (isShuffleEnabled) 1f else MEDIUM_ALPHA
            contentDescription =
                getString(if (isShuffleEnabled) R.string.disable_shuffle else R.string.enable_shuffle)
        }
    }

    private fun seekBack() {
        binding.activityTrackProgressbar.progress += -SEEK_INTERVAL_S
        withPlayer { seekBack() }
    }

    private fun seekForward() {
        binding.activityTrackProgressbar.progress += SEEK_INTERVAL_S
        withPlayer { seekForward() }
    }

    private fun togglePlaybackSetting() {
        val newPlaybackSetting = config.playbackSetting.nextPlaybackOption
        config.playbackSetting = newPlaybackSetting
        showPlaybackModeHint(newPlaybackSetting.descriptionStringRes)
        setupPlaybackSettingButton()
        withPlayer {
            setRepeatMode(newPlaybackSetting)
        }
    }

    private val hidePlaybackModeHint = Runnable { binding.playbackModeHint.visibility = View.GONE }

    private fun showPlaybackModeHint(message: Int) {
        binding.playbackModeHint.apply {
            removeCallbacks(hidePlaybackModeHint)
            setText(message)
            visibility = View.VISIBLE
            bringToFront()
            postDelayed(hidePlaybackModeHint, 1400L)
        }
    }

    private fun maybeUpdatePlaybackSettingButton(playbackSetting: PlaybackSetting) {
        if (config.playbackSetting != PlaybackSetting.STOP_AFTER_CURRENT_TRACK) {
            setupPlaybackSettingButton(playbackSetting)
        }
    }

    private fun setupPlaybackSettingButton(playbackSetting: PlaybackSetting = config.playbackSetting) {
        binding.activityTrackPlaybackSetting.apply {
            contentDescription = getString(playbackSetting.contentDescriptionStringRes)
            setImageResource(playbackSetting.iconRes)

            val isRepeatOff = playbackSetting == PlaybackSetting.REPEAT_OFF

            alpha = if (isRepeatOff) MEDIUM_ALPHA else 1f
            applyColorFilter(if (isRepeatOff) getProperTextColor() else getProperPrimaryColor())
        }
    }

    private fun setupSeekbar() {
        updatePlaybackSpeed(config.playbackSpeed)

        binding.activityTrackProgressbar.setOnSeekBarChangeListener(
            object : SeekBar.OnSeekBarChangeListener {
                override fun onProgressChanged(seekBar: SeekBar, progress: Int, fromUser: Boolean) {
                    val formattedProgress = progress.getFormattedDuration()
                    binding.activityTrackProgressCurrent.text = formattedProgress
                }

                override fun onStartTrackingTouch(seekBar: SeekBar) {}

                override fun onStopTrackingTouch(seekBar: SeekBar) = withPlayer {
                    seekTo(seekBar.progress * 1000L)
                }
            })
    }

    private fun showPlaybackSpeedPicker() {
        val fragment = PlaybackSpeedFragment()
        fragment.show(supportFragmentManager, PlaybackSpeedFragment::class.java.simpleName)
        fragment.setListener(this)
    }

    override fun updatePlaybackSpeed(speed: Float) {
        @SuppressLint("SetTextI18n")
        binding.activityTrackSpeed.text = "×${DecimalFormat("0.0#").format(speed)}"
        withPlayer {
            setPlaybackSpeed(speed)
        }
    }

    private fun getResizedDrawable(drawable: Drawable, wantedHeight: Int): Drawable {
        val bitmap = (drawable as BitmapDrawable).bitmap
        val bitmapResized = bitmap.scale(wantedHeight, wantedHeight, false)
        return bitmapResized.toDrawable(resources)
    }

    override fun onPlaybackStateChanged(playbackState: Int) = updatePlayerState()

    override fun onIsPlayingChanged(isPlaying: Boolean) = updatePlayerState()

    override fun onRepeatModeChanged(repeatMode: Int) {
        maybeUpdatePlaybackSettingButton(getPlaybackSetting(repeatMode))
    }

    override fun onShuffleModeEnabledChanged(shuffleModeEnabled: Boolean) {
        setupShuffleButton(shuffleModeEnabled)
    }

    override fun onMediaItemTransition(mediaItem: MediaItem?, reason: Int) {
        super.onMediaItemTransition(mediaItem, reason)
        if (mediaItem == null) {
            finish()
        } else {
            binding.activityTrackProgressbar.progress = 0
            updateTrackInfo()
        }
    }

    private fun updateTrackInfo() {
        withPlayer {
            setupTrackInfo(currentMediaItem)
            setupNextTrackInfo(nextMediaItem)
        }
    }

    private fun updatePlayerState() {
        withPlayer {
            val isPlaying = isReallyPlaying
            if (isPlaying) {
                scheduleProgressUpdate()
            } else {
                cancelProgressUpdate()
            }

            updateProgress(currentPosition)
            updatePlayPause(isPlaying)
            setupShuffleButton(shuffleModeEnabled)
            maybeUpdatePlaybackSettingButton(getPlaybackSetting(repeatMode))
        }
    }

    private fun scheduleProgressUpdate() {
        cancelProgressUpdate()
        withPlayer {
            val delayInMillis = (UPDATE_INTERVAL_MS / config.playbackSpeed).toLong()
            handler.postDelayed(delayInMillis = delayInMillis) {
                updateProgress(currentPosition)
                scheduleProgressUpdate()
            }
        }
    }

    private fun cancelProgressUpdate() {
        handler.removeCallbacksAndMessages(null)
    }

    private fun updateProgress(currentPosition: Long) {
        if (binding.activityTrackProgressbar.isScrubbing) return
        withPlayer {
            binding.activityTrackProgressbar.setPlaybackClock(currentPosition, isReallyPlaying, playbackParameters.speed)
            lyricsController?.update(currentPosition, isReallyPlaying)
        }
        binding.activityTrackProgressbar.progress =
            currentPosition.milliseconds.inWholeSeconds.toInt()
    }

    private fun updatePlayPause(isPlaying: Boolean) {
        binding.activityTrackPlayPause.setImageResource(if (isPlaying) R.drawable.tuno_wave_pause else R.drawable.tuno_wave_play)
        binding.activityTrackPlayPause.applyColorFilter(getProperTextColor())
    }

    private fun dp(value: Int) = (value * resources.displayMetrics.density).toInt()

    private fun seekToNext() = animateTrackSwipe(true)

    private fun seekToPrevious() {
        withPlayer {
            if (maybeRestartOnPrevious()) return@withPlayer
            animateTrackSwipe(false)
        }
    }

    private fun seekToPreviousTrack() {
        animateTrackSwipe(false)
    }

    /**
     * This is here so the player can quickly seek next/previous without doing too much work.
     * It probably won't be needed once https://github.com/androidx/media/issues/81 is resolved.
     */
    private fun seekWithDelay() {
        seekJob?.cancel()
        seekJob = scope.launch {
            delay(timeMillis = SEEK_COALESCE_INTERVAL_MS)
            if (seekCount != 0) {
                seekByCount(seekCount)
            }
        }
    }

    private fun seekByCount(count: Int) {
        withPlayer {
            if (currentMediaItem == null) {
                return@withPlayer
            }

            val currentIndex = currentMediaItemIndex
            val mediaItemCount = mediaItemCount
            val seekIndex = if (shuffleModeEnabled) {
                val shuffledIndex = shuffledMediaItemsIndices.indexOf(currentIndex)
                val seekIndex = rotateIndex(mediaItemCount, shuffledIndex + count)
                shuffledMediaItemsIndices.getOrNull(seekIndex) ?: return@withPlayer
            } else {
                rotateIndex(mediaItemCount, currentIndex + count)
            }

            play()
            seekTo(seekIndex, 0)
            seekCount = 0
        }
    }

    private fun rotateIndex(total: Int, index: Int): Int {
        return (index % total + total) % total
    }
}
