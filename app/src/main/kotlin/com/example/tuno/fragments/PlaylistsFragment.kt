package com.example.tuno.fragments

import android.content.Context
import android.content.Intent
import android.util.AttributeSet
import com.google.gson.Gson
import org.fossify.commons.activities.BaseSimpleActivity
import org.fossify.commons.extensions.*
import org.fossify.commons.helpers.ensureBackgroundThread
import com.example.tuno.R
import com.example.tuno.activities.SimpleActivity
import com.example.tuno.activities.TracksActivity
import com.example.tuno.adapters.PlaylistsAdapter
import com.example.tuno.databinding.FragmentPlaylistsBinding
import com.example.tuno.dialogs.ChangeSortingDialog
import com.example.tuno.dialogs.NewPlaylistDialog
import com.example.tuno.extensions.audioHelper
import com.example.tuno.extensions.config
import com.example.tuno.extensions.mediaScanner
import com.example.tuno.extensions.viewBinding
import com.example.tuno.helpers.PLAYLIST
import com.example.tuno.helpers.TAB_PLAYLISTS
import com.example.tuno.models.Events
import com.example.tuno.models.Playlist
import com.example.tuno.models.sortSafely
import org.greenrobot.eventbus.EventBus

class PlaylistsFragment(context: Context, attributeSet: AttributeSet) : MyViewPagerFragment(context, attributeSet) {
    private var playlists = ArrayList<Playlist>()
    private var searchQuery = ""
    private val binding by viewBinding(FragmentPlaylistsBinding::bind)

    override fun setupFragment(activity: BaseSimpleActivity) {
        binding.playlistsPlaceholder2.setOnClickListener {
            NewPlaylistDialog(activity) {
                EventBus.getDefault().post(Events.PlaylistsUpdated())
            }
        }

        ensureBackgroundThread {
            val playlists = context.audioHelper.getAllPlaylists()
            playlists.forEach {
                it.trackCount = context.audioHelper.getPlaylistTrackCount(it.id)
            }

            playlists.sortSafely(context.config.playlistSorting)
            this.playlists = playlists

            activity.runOnUiThread {
                val scanning = activity.mediaScanner.isScanning()
                updateEmptyState(playlists.isEmpty(), scanning)

                val adapter = binding.playlistsList.adapter
                if (adapter == null) {
                    PlaylistsAdapter(activity, playlists, binding.playlistsList) {
                        activity.hideKeyboard()
                        Intent(activity, TracksActivity::class.java).apply {
                            putExtra(PLAYLIST, Gson().toJson(it))
                            activity.startActivity(this)
                        }
                    }.apply {
                        binding.playlistsList.adapter = this
                    }

                    if (context.areSystemAnimationsEnabled) {
                        binding.playlistsList.scheduleLayoutAnimation()
                    }
                } else {
                    (adapter as PlaylistsAdapter).updateItems(playlists)
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
        val normalizedText = text.normalizeString()
        val filtered = playlists.filter {
            it.title.normalizeString().contains(normalizedText, true)
        }.toMutableList() as ArrayList<Playlist>
        getAdapter()?.updateItems(filtered, text)
        updateEmptyState(filtered.isEmpty(), context.mediaScanner.isScanning())
    }

    private fun updateEmptyState(empty: Boolean, scanning: Boolean) {
        val searching = searchQuery.isNotBlank()
        binding.playlistsEmpty.beVisibleIf(empty)
        binding.playlistsEmptyArt.beVisibleIf(!searching)
        binding.playlistsFastscroller.beVisibleIf(!empty)
        binding.playlistsPlaceholder2.beVisibleIf(!searching && !scanning)
        binding.playlistsPlaceholder.setText(when {
            scanning -> R.string.loading_files
            searching -> R.string.tuno_no_playlist_results
            else -> R.string.tuno_empty_playlists
        })
        binding.playlistsEmptyHint.setText(if (searching) R.string.tuno_no_playlist_results_hint else R.string.tuno_empty_playlists_hint)
    }

    override fun onSearchClosed() {
        onSearchQueryChanged("")
    }

    override fun onSortOpen(activity: SimpleActivity) {
        ChangeSortingDialog(activity, TAB_PLAYLISTS) {
            val adapter = getAdapter() ?: return@ChangeSortingDialog
            playlists.sortSafely(activity.config.playlistSorting)
            adapter.updateItems(playlists, forceUpdate = true)
        }
    }

    override fun setupColors(textColor: Int, adjustedPrimaryColor: Int) {
        binding.playlistsPlaceholder.setTextColor(context.getColor(R.color.tuno_ink))
        binding.playlistsPlaceholder2.setTextColor(context.getColor(R.color.tuno_surface))
        binding.playlistsFastscroller.updateColors(adjustedPrimaryColor)
        getAdapter()?.updateColors(textColor)
    }

    private fun getAdapter() = binding.playlistsList.adapter as? PlaylistsAdapter
}
