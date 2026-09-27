package com.example.tuno.activities

import android.content.Intent
import android.os.Bundle
import com.google.gson.Gson
import com.google.gson.reflect.TypeToken
import org.fossify.commons.dialogs.PermissionRequiredDialog
import org.fossify.commons.extensions.*
import org.fossify.commons.helpers.NavigationIcon
import org.fossify.commons.helpers.ensureBackgroundThread
import com.example.tuno.R
import com.example.tuno.adapters.AlbumsTracksAdapter
import com.example.tuno.databinding.ActivityAlbumsBinding
import com.example.tuno.extensions.audioHelper
import com.example.tuno.helpers.ALBUM
import com.example.tuno.helpers.ARTIST
import com.example.tuno.models.*

// Artists -> Albums -> Tracks
class AlbumsActivity : SimpleMusicActivity() {

    private val binding by viewBinding(ActivityAlbumsBinding::inflate)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(binding.root)

        setupEdgeToEdge(padBottomSystem = listOf(binding.albumsList, binding.currentTrackBar.root))

        binding.albumsFastscroller.updateColors(getProperPrimaryColor())

        val artistType = object : TypeToken<Artist>() {}.type
        val artist = Gson().fromJson<Artist>(intent.getStringExtra(ARTIST), artistType)
        binding.albumsToolbar.title = artist.title

        ensureBackgroundThread {
            val albums = audioHelper.getArtistAlbums(artist.id)
            val listItems = ArrayList<ListItem>()
            val albumsSectionLabel = resources.getQuantityString(R.plurals.albums_plural, albums.size, albums.size)
            listItems.add(AlbumSection(albumsSectionLabel))
            listItems.addAll(albums)

            val albumTracks = audioHelper.getAlbumTracks(albums)
            val trackFullDuration = albumTracks.sumOf { it.duration }

            var tracksSectionLabel = resources.getQuantityString(R.plurals.tracks_plural, albumTracks.size, albumTracks.size)
            tracksSectionLabel += " • ${trackFullDuration.getFormattedDuration(true)}"
            listItems.add(AlbumSection(tracksSectionLabel))
            listItems.addAll(albumTracks)

            runOnUiThread {
                AlbumsTracksAdapter(this, listItems, binding.albumsList) {
                    hideKeyboard()
                    if (it is Album) {
                        Intent(this, TracksActivity::class.java).apply {
                            putExtra(ALBUM, Gson().toJson(it))
                            startActivity(this)
                        }
                    } else {
                        handleNotificationPermission { granted ->
                            if (granted) {
                                val startIndex = albumTracks.indexOf(it as Track)
                                prepareAndPlay(albumTracks, startIndex)
                            } else {
                                PermissionRequiredDialog(
                                    this,
                                    org.fossify.commons.R.string.allow_notifications_music_player,
                                    { openNotificationSettings() }
                                )
                            }
                        }
                    }
                }.apply {
                    binding.albumsList.adapter = this
                }

                if (areSystemAnimationsEnabled) {
                    binding.albumsList.scheduleLayoutAnimation()
                }
            }
        }

        setupCurrentTrackBar(binding.currentTrackBar.root)
    }

    override fun onResume() {
        super.onResume()
        setupTopAppBar(binding.albumsAppbar, NavigationIcon.Arrow)
        binding.root.background = com.example.tuno.views.HomeGlassDrawable()
        window.setBackgroundDrawable(com.example.tuno.views.HomeGlassDrawable())
        binding.albumsAppbar.setBackgroundColor(android.graphics.Color.TRANSPARENT)
        binding.albumsAppbar.elevation = 0f
        binding.albumsToolbar.setBackgroundResource(R.drawable.tuno_home_glass)
        binding.albumsToolbar.setTitleTextColor(android.graphics.Color.WHITE)
        binding.albumsToolbar.navigationIcon?.mutate()?.setTint(android.graphics.Color.WHITE)
        androidx.core.view.WindowCompat.getInsetsController(window, binding.root).isAppearanceLightStatusBars = false
        androidx.core.view.WindowCompat.getInsetsController(window, binding.root).isAppearanceLightNavigationBars = false
    }
}
