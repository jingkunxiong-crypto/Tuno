package com.example.tuno.activities

import android.os.Bundle
import org.fossify.commons.extensions.getProperPrimaryColor
import org.fossify.commons.extensions.getProperTextColor
import org.fossify.commons.extensions.viewBinding
import org.fossify.commons.helpers.NavigationIcon
import com.example.tuno.R
import com.example.tuno.databinding.ActivityLibraryBinding
import com.example.tuno.fragments.MyViewPagerFragment
import com.example.tuno.helpers.TAB_ALBUMS
import com.example.tuno.helpers.TAB_ARTISTS
import com.example.tuno.helpers.TAB_FOLDERS

/** Secondary library categories stay accessible without occupying the bottom navigation. */
class LibraryActivity : SimpleMusicActivity() {
    companion object { const val EXTRA_TAB = "library_tab" }
    private val binding by viewBinding(ActivityLibraryBinding::inflate)
    private lateinit var content: MyViewPagerFragment

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(binding.root)
        val (layout, title) = when (intent.getIntExtra(EXTRA_TAB, TAB_FOLDERS)) {
            TAB_ALBUMS -> R.layout.fragment_albums to R.string.albums
            TAB_ARTISTS -> R.layout.fragment_artists to R.string.artists
            else -> R.layout.fragment_folders to R.string.folders
        }
        binding.libraryToolbar.setTitle(title)
        content = layoutInflater.inflate(layout, binding.libraryContent, false) as MyViewPagerFragment
        binding.libraryContent.addView(content)
        setupEdgeToEdge(padBottomSystem = listOf(binding.currentTrackBar.root))
        setupCurrentTrackBar(binding.currentTrackBar.root)
    }

    override fun onResume() {
        super.onResume()
        setupTopAppBar(binding.libraryAppbar, NavigationIcon.Arrow)
        binding.root.background = com.example.tuno.views.HomeGlassDrawable()
        window.setBackgroundDrawable(com.example.tuno.views.HomeGlassDrawable())
        binding.libraryAppbar.setBackgroundColor(android.graphics.Color.TRANSPARENT)
        binding.libraryAppbar.elevation = 0f
        binding.libraryToolbar.setBackgroundResource(R.drawable.tuno_home_glass)
        binding.libraryToolbar.setTitleTextColor(android.graphics.Color.WHITE)
        binding.libraryToolbar.navigationIcon?.mutate()?.setTint(android.graphics.Color.WHITE)
        androidx.core.view.WindowCompat.getInsetsController(window, binding.root).isAppearanceLightStatusBars = false
        androidx.core.view.WindowCompat.getInsetsController(window, binding.root).isAppearanceLightNavigationBars = false
        content.setupColors(getProperTextColor(), getProperPrimaryColor())
        content.setupFragment(this)
    }
}
