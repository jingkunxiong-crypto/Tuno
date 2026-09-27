package com.example.tuno.activities

import android.os.Bundle
import androidx.appcompat.app.AppCompatDelegate
import androidx.core.graphics.ColorUtils
import org.fossify.commons.activities.BaseSimpleActivity
import org.fossify.commons.extensions.getProperBackgroundColor
import com.example.tuno.R
import com.example.tuno.helpers.REPOSITORY_NAME
import com.example.tuno.extensions.config

open class SimpleActivity : BaseSimpleActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        config.backgroundColor = android.graphics.Color.rgb(24, 26, 30)
        config.textColor = android.graphics.Color.rgb(240, 241, 244)
        config.primaryColor = android.graphics.Color.WHITE
        // Fossify stores its own appearance preference. Keep night-qualified resources in sync.
        delegate.localNightMode = if (ColorUtils.calculateLuminance(getProperBackgroundColor()) < 0.5) {
            AppCompatDelegate.MODE_NIGHT_YES
        } else {
            AppCompatDelegate.MODE_NIGHT_NO
        }
        super.onCreate(savedInstanceState)
        if (this is MainActivity || this is TrackActivity) {
            // Keep both ends of the cover transition on the same refresh-rate preference.
            @Suppress("DEPRECATION")
            val screen = windowManager.defaultDisplay
            val currentMode = screen.mode
            val refreshRate = screen.supportedModes
                .filter { it.physicalWidth == currentMode.physicalWidth && it.physicalHeight == currentMode.physicalHeight }
                .map { it.refreshRate }
                .filter { it <= 120.1f }
                .maxOrNull() ?: currentMode.refreshRate
            window.attributes = window.attributes.apply { preferredRefreshRate = refreshRate }
        }
    }

    override fun onPostResume() {
        super.onPostResume()
        if (this is TrackActivity) return
        val content = findViewById<android.view.ViewGroup>(android.R.id.content)
        val root = content.getChildAt(0) ?: return
        fun clearFlatBackgrounds(view: android.view.View) {
            val color = (view.background as? android.graphics.drawable.ColorDrawable)?.color
            if (color == getProperBackgroundColor() || color == getColor(R.color.tuno_background)) {
                view.background = null
            }
            if (view is android.view.ViewGroup) for (i in 0 until view.childCount) clearFlatBackgrounds(view.getChildAt(i))
        }
        clearFlatBackgrounds(root)
        root.setBackgroundResource(R.drawable.tuno_graphite_background)
        window.setBackgroundDrawableResource(R.drawable.tuno_graphite_background)
        if (this is LibraryActivity || this is QueueActivity || this is AlbumsActivity || this is TracksActivity || this is SettingsActivity || this is ExcludedFoldersActivity || this is EqualizerActivity) {
            // Apply after the base activity's resume styling. These screens keep a
            // glass toolbar at every scroll offset instead of primary-color lifting.
            fun styleLibraryBar(view: android.view.View) {
                if (view is org.fossify.commons.views.MyAppBarLayout) {
                    view.isLiftOnScroll = false
                    view.setLifted(false)
                    view.stateListAnimator = null
                    view.elevation = 0f
                    view.setBackgroundColor(android.graphics.Color.TRANSPARENT)
                }
                if (view is com.google.android.material.appbar.MaterialToolbar) {
                    view.setBackgroundResource(R.drawable.tuno_home_glass)
                    view.setTitleTextColor(android.graphics.Color.WHITE)
                    view.setNavigationIconTint(android.graphics.Color.WHITE)
                    view.navigationIcon?.mutate()?.setColorFilter(android.graphics.Color.WHITE, android.graphics.PorterDuff.Mode.SRC_IN)
                    view.overflowIcon?.mutate()?.setColorFilter(android.graphics.Color.WHITE, android.graphics.PorterDuff.Mode.SRC_IN)
                    for (i in 0 until view.menu.size()) view.menu.getItem(i).icon?.mutate()?.setTint(android.graphics.Color.WHITE)
                }
                if (view is android.view.ViewGroup) for (i in 0 until view.childCount) styleLibraryBar(view.getChildAt(i))
            }
            styleLibraryBar(root)
            @Suppress("DEPRECATION")
            window.statusBarColor = android.graphics.Color.TRANSPARENT
            androidx.core.view.WindowCompat.getInsetsController(window, root).apply {
                isAppearanceLightStatusBars = false
                isAppearanceLightNavigationBars = false
            }
        }
    }

    override fun getAppIconIDs() = arrayListOf(
        R.mipmap.ic_launcher_red,
        R.mipmap.ic_launcher_pink,
        R.mipmap.ic_launcher_purple,
        R.mipmap.ic_launcher_deep_purple,
        R.mipmap.ic_launcher_indigo,
        R.mipmap.ic_launcher_blue,
        R.mipmap.ic_launcher_light_blue,
        R.mipmap.ic_launcher_cyan,
        R.mipmap.ic_launcher_teal,
        R.mipmap.ic_launcher,
        R.mipmap.ic_launcher_light_green,
        R.mipmap.ic_launcher_lime,
        R.mipmap.ic_launcher_yellow,
        R.mipmap.ic_launcher_amber,
        R.mipmap.ic_launcher_orange,
        R.mipmap.ic_launcher_deep_orange,
        R.mipmap.ic_launcher_brown,
        R.mipmap.ic_launcher_blue_grey,
        R.mipmap.ic_launcher_grey_black
    )

    override fun getAppLauncherName() = getString(R.string.app_launcher_name)

    override fun getRepositoryName() = REPOSITORY_NAME
}
