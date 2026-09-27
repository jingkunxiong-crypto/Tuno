package com.example.tuno.views

import android.app.Activity
import android.content.Context
import android.provider.MediaStore
import android.util.AttributeSet
import android.widget.RelativeLayout
import androidx.media3.common.MediaItem
import com.bumptech.glide.Glide
import com.bumptech.glide.load.resource.bitmap.CenterCrop
import com.bumptech.glide.load.resource.bitmap.RoundedCorners
import com.bumptech.glide.request.RequestOptions
import org.fossify.commons.extensions.*
import com.example.tuno.R
import com.example.tuno.databinding.ViewCurrentTrackBarBinding
import com.example.tuno.extensions.*

class CurrentTrackBar(context: Context, attributeSet: AttributeSet) : RelativeLayout(context, attributeSet) {
    private val binding by viewBinding(ViewCurrentTrackBarBinding::bind)

    fun initialize(togglePlayback: () -> Unit) {
        binding.currentTrackPlayPause.setOnClickListener {
            togglePlayback()
        }
    }

    fun updateColors() {
        setBackgroundResource(R.drawable.tuno_player_card)
        binding.currentTrackLabel.setTextColor(context.getColor(R.color.tuno_ink))
        binding.currentTrackArtist.setTextColor(context.getColor(R.color.tuno_muted))
    }

    fun updateCurrentTrack(mediaItem: MediaItem?) {
        val track = mediaItem?.toTrack()
        if (track == null) {
            fadeOut()
            return
        } else {
            fadeIn()
        }

        val artist = if (track.artist.trim().isNotEmpty() && track.artist != MediaStore.UNKNOWN_STRING) {
            track.artist
        } else {
            context.getString(R.string.tuno_local_audio)
        }

        binding.currentTrackLabel.text = track.title
        binding.currentTrackArtist.text = artist
        val cornerRadius = resources.getDimension(org.fossify.commons.R.dimen.rounded_corner_radius_small).toInt()
        val currentTrackPlaceholder = context.getDrawable(R.drawable.tuno_record)
        val options = RequestOptions()
            .error(currentTrackPlaceholder)
            .override(1024, 1024)
            .dontAnimate()
            .transform(CenterCrop(), RoundedCorners((cornerRadius * 1024 / (68 * resources.displayMetrics.density)).toInt().coerceAtLeast(1)))

        context.getTrackCoverArt(track) { coverArt ->
            (context as? Activity)?.ensureActivityNotDestroyed {
                Glide.with(this)
                    .load(coverArt)
                    .apply(options)
                    .into(findViewById(R.id.current_track_image))
            }
        }
    }

    fun updateTrackState(isPlaying: Boolean) {
        binding.currentTrackPlayPause.updatePlayPauseIcon(isPlaying, context.getColor(R.color.tuno_accent))
    }
}
