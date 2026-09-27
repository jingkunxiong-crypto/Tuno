package com.example.tuno.fragments

import android.app.Activity
import android.content.Context
import android.util.AttributeSet
import org.fossify.commons.activities.BaseSimpleActivity
import org.fossify.commons.dialogs.PermissionRequiredDialog
import org.fossify.commons.extensions.*
import org.fossify.commons.helpers.ensureBackgroundThread
import com.example.tuno.R
import com.example.tuno.activities.SimpleActivity
import com.example.tuno.adapters.TracksAdapter
import com.example.tuno.databinding.FragmentTracksBinding
import com.example.tuno.dialogs.ChangeSortingDialog
import com.example.tuno.extensions.audioHelper
import com.example.tuno.extensions.config
import com.example.tuno.extensions.mediaScanner
import com.example.tuno.extensions.viewBinding
import com.example.tuno.helpers.TAB_TRACKS
import com.example.tuno.models.Track
import com.example.tuno.models.Events
import org.greenrobot.eventbus.EventBus
import com.example.tuno.models.sortSafely

// Artists -> Albums -> Tracks
class TracksFragment(context: Context, attributeSet: AttributeSet) : MyViewPagerFragment(context, attributeSet) {
    private var tracks = ArrayList<Track>()
    private var searchQuery = ""
    private val binding by viewBinding(FragmentTracksBinding::bind)

    override fun setupFragment(activity: BaseSimpleActivity) {
        binding.scanTracks.setOnClickListener { EventBus.getDefault().post(Events.RefreshFragments()) }
        binding.shuffleTracks.setOnClickListener {
            val matching = matchingTracks()
            if (matching.isNotEmpty()) {
                activity.handleNotificationPermission { granted ->
                    if (granted) prepareAndPlay(matching.shuffled())
                    else activity.openNotificationSettings()
                }
            }
        }
        ensureBackgroundThread {
            tracks = context.audioHelper.getAllTracks()

            val excludedFolders = context.config.excludedFolders
            tracks = tracks.filter {
                !excludedFolders.contains(it.path.getParentPath())
            }.toMutableList() as ArrayList<Track>

            activity.runOnUiThread {
                val scanning = activity.mediaScanner.isScanning()
                updateEmptyState(scanning)
                val adapter = binding.tracksList.adapter
                if (adapter == null) {
                    TracksAdapter(activity = activity, recyclerView = binding.tracksList, sourceType = TracksAdapter.TYPE_TRACKS, items = tracks) {
                        activity.hideKeyboard()
                        activity.handleNotificationPermission { granted ->
                            if (granted) {
                                val startIndex = tracks.indexOf(it as Track)
                                prepareAndPlay(tracks, startIndex)
                            } else {
                                if (context is Activity) {
                                    PermissionRequiredDialog(
                                        activity,
                                        org.fossify.commons.R.string.allow_notifications_music_player,
                                        { activity.openNotificationSettings() }
                                    )
                                }
                            }
                        }
                    }.apply {
                        binding.tracksList.adapter = this
                    }

                    if (context.areSystemAnimationsEnabled) {
                        binding.tracksList.scheduleLayoutAnimation()
                    }
                } else {
                    (adapter as TracksAdapter).updateItems(tracks)
                }
                onSearchQueryChanged(searchQuery)
            }
        }
    }

    override fun finishActMode() {
        getAdapter()?.finishActMode()
    }

    override fun onSearchQueryChanged(text: String) {
        searchQuery = text
        getAdapter()?.updateItems(matchingTracks(), text)
        updateEmptyState(context.mediaScanner.isScanning())
    }

    private fun matchingTracks(): ArrayList<Track> {
        val normalizedText = searchQuery.normalizeString()
        return ArrayList(
            tracks.filter { track ->
                val title = track.title.normalizeString()
                val artistAlbum = "${track.artist} - ${track.album}".normalizeString()
                title.contains(normalizedText, ignoreCase = true) ||
                    artistAlbum.contains(normalizedText, ignoreCase = true)
            }
        )
    }

    private fun updateEmptyState(scanning: Boolean) {
        val empty = matchingTracks().isEmpty()
        val searching = searchQuery.isNotBlank()
        binding.tracksEmpty.beVisibleIf(empty)
        binding.tracksEmptyArt.beVisibleIf(!searching)
        binding.tracksFastscroller.beVisibleIf(!empty)
        binding.tracksCount.text = context.getString(R.string.tuno_song_count, tracks.size)
        binding.shuffleTracks.isEnabled = !empty && !scanning
        binding.shuffleTracks.beVisibleIf(tracks.isNotEmpty())
        binding.scanTracks.beVisibleIf(!searching)
        binding.scanTracks.isEnabled = !scanning
        binding.tracksPlaceholder.setText(when {
            scanning -> R.string.loading_files
            searching -> R.string.tuno_no_results
            else -> R.string.tuno_empty_music
        })
        binding.tracksEmptyHint.setText(if (searching) R.string.tuno_no_results_hint else R.string.tuno_empty_music_hint)
    }

    override fun onSearchClosed() {
        onSearchQueryChanged("")
    }

    override fun onSortOpen(activity: SimpleActivity) {
        ChangeSortingDialog(activity, TAB_TRACKS) {
            val adapter = getAdapter() ?: return@ChangeSortingDialog
            tracks.sortSafely(activity.config.trackSorting)
            adapter.updateItems(matchingTracks(), searchQuery, forceUpdate = true)
        }
    }

    override fun setupColors(textColor: Int, adjustedPrimaryColor: Int) {
        binding.tracksPlaceholder.setTextColor(context.getColor(R.color.tuno_ink))
        binding.tracksFastscroller.updateColors(adjustedPrimaryColor)
        getAdapter()?.updateColors(textColor)
    }

    private fun getAdapter() = binding.tracksList.adapter as? TracksAdapter
}
