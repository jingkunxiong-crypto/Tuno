package com.example.tuno.fragments

import android.graphics.drawable.LayerDrawable
import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.core.content.res.ResourcesCompat
import com.google.android.material.bottomsheet.BottomSheetDialogFragment
import org.fossify.commons.extensions.*
import org.fossify.commons.views.MySeekBar
import org.fossify.commons.views.MyTextView
import com.example.tuno.R
import com.example.tuno.databinding.FragmentPlaybackSpeedBinding
import com.example.tuno.extensions.config
import com.example.tuno.helpers.Config
import com.example.tuno.interfaces.PlaybackSpeedListener

class PlaybackSpeedFragment : BottomSheetDialogFragment() {
    private val MIN_PLAYBACK_SPEED = 0.25f
    private val MAX_PLAYBACK_SPEED = 3f
    private val MAX_PROGRESS = 324
    private val HALF_PROGRESS = MAX_PROGRESS / 2
    private val STEP = 0.05f

    private var seekBar: MySeekBar? = null
    private var listener: PlaybackSpeedListener? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setStyle(STYLE_NORMAL, R.style.CustomBottomSheetDialogTheme)
    }

    override fun onCreateView(inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?): View {
        val config = requireContext().config
        val binding = FragmentPlaybackSpeedBinding.inflate(inflater, container, false)
        val background = ResourcesCompat.getDrawable(resources, org.fossify.commons.R.drawable.bottom_sheet_bg, requireContext().theme)
        (background as LayerDrawable).findDrawableByLayerId(org.fossify.commons.R.id.bottom_sheet_background)
            .applyColorFilter(requireContext().getProperBackgroundColor())

        binding.apply {
            seekBar = playbackSpeedSeekbar
            root.setBackgroundDrawable(background)
            requireContext().updateTextColors(playbackSpeedHolder)
            playbackSpeedSlow.applyColorFilter(requireContext().getProperTextColor())
            playbackSpeedFast.applyColorFilter(requireContext().getProperTextColor())
            playbackSpeedSlow.setOnClickListener { reduceSpeed() }
            playbackSpeedFast.setOnClickListener { increaseSpeed() }
            initSeekbar(playbackSpeedSeekbar, playbackSpeedLabel, config)
        }

        return binding.root
    }

    private fun initSeekbar(seekbar: MySeekBar, speedLabel: MyTextView, config: Config) {
        seekbar.max = MAX_PROGRESS
        // Reconstruct position from the actual speed, including older saved settings.
        val speed = config.playbackSpeed.coerceIn(MIN_PLAYBACK_SPEED, MAX_PLAYBACK_SPEED)
        seekbar.progress = if (speed <= 1f) {
            ((speed - MIN_PLAYBACK_SPEED) / (1f - MIN_PLAYBACK_SPEED) * HALF_PROGRESS).toInt()
        } else {
            (HALF_PROGRESS + (speed - 1f) / (MAX_PLAYBACK_SPEED - 1f) * HALF_PROGRESS).toInt()
        }
        speedLabel.text = formatPlaybackSpeed(speed)
        var adjusting = false
        var snapped = false
        var previousRaw = seekbar.progress
        seekbar.setOnSeekBarChangeListener(object : android.widget.SeekBar.OnSeekBarChangeListener {
            override fun onStartTrackingTouch(bar: android.widget.SeekBar) {
                previousRaw = bar.progress
                snapped = bar.progress == HALF_PROGRESS
            }

            override fun onProgressChanged(bar: android.widget.SeekBar, progress: Int, fromUser: Boolean) {
                if (adjusting) return
                var target = progress
                if (fromUser) {
                    val distance = kotlin.math.abs(progress - HALF_PROGRESS)
                    val crossed = (previousRaw < HALF_PROGRESS && progress >= HALF_PROGRESS) ||
                        (previousRaw > HALF_PROGRESS && progress <= HALF_PROGRESS)
                    if (!snapped && (distance <= 5 || crossed)) {
                        snapped = true
                        bar.performHapticFeedback(android.view.HapticFeedbackConstants.CLOCK_TICK)
                        target = HALF_PROGRESS
                    } else if (snapped && distance <= 14) {
                        target = HALF_PROGRESS
                    } else {
                        snapped = false
                    }
                    previousRaw = progress
                }
                if (target != progress) {
                    adjusting = true
                    bar.progress = target
                    adjusting = false
                }
                val newSpeed = getPlaybackSpeed(target)
                config.playbackSpeedProgress = target
                speedLabel.text = formatPlaybackSpeed(newSpeed)
                if (config.playbackSpeed != newSpeed) {
                    config.playbackSpeed = newSpeed
                    listener?.updatePlaybackSpeed(newSpeed)
                }
            }

            override fun onStopTrackingTouch(bar: android.widget.SeekBar) {
                snapped = false
            }
        })
    }
    private fun getPlaybackSpeed(progress: Int): Float {
        var playbackSpeed = when {
            progress < HALF_PROGRESS -> {
                val lowerProgressPercent = progress / HALF_PROGRESS.toFloat()
                val lowerProgress = (1 - MIN_PLAYBACK_SPEED) * lowerProgressPercent + MIN_PLAYBACK_SPEED
                lowerProgress
            }

            progress > HALF_PROGRESS -> {
                val upperProgressPercent = progress / HALF_PROGRESS.toFloat() - 1
                val upperDiff = MAX_PLAYBACK_SPEED - 1
                upperDiff * upperProgressPercent + 1
            }

            else -> 1f
        }
        playbackSpeed = Math.min(Math.max(playbackSpeed, MIN_PLAYBACK_SPEED), MAX_PLAYBACK_SPEED)
        val stepMultiplier = 1 / STEP
        return Math.round(playbackSpeed * stepMultiplier) / stepMultiplier
    }

    private fun reduceSpeed() {
        var currentProgress = seekBar?.progress ?: return
        val currentSpeed = requireContext().config.playbackSpeed
        while (currentProgress > 0) {
            val newSpeed = getPlaybackSpeed(--currentProgress)
            if (newSpeed != currentSpeed) {
                seekBar!!.progress = currentProgress
                break
            }
        }
    }

    private fun increaseSpeed() {
        var currentProgress = seekBar?.progress ?: return
        val currentSpeed = requireContext().config.playbackSpeed
        while (currentProgress < MAX_PROGRESS) {
            val newSpeed = getPlaybackSpeed(++currentProgress)
            if (newSpeed != currentSpeed) {
                seekBar!!.progress = currentProgress
                break
            }
        }
    }

    private fun formatPlaybackSpeed(value: Float) = "×${java.text.DecimalFormat("0.0#").format(value)}"

    fun setListener(playbackSpeedListener: PlaybackSpeedListener) {
        listener = playbackSpeedListener
    }
}
