package com.example.tuno.activities

import android.content.Intent
import android.os.Bundle
import androidx.appcompat.app.AppCompatActivity
import org.fossify.commons.extensions.baseConfig
import org.fossify.commons.extensions.isAutoTheme
import org.fossify.commons.extensions.isSystemInDarkMode
import org.fossify.commons.extensions.syncGlobalConfig

class SplashActivity : AppCompatActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // Tuno supports local APK installation; the upstream splash checks can show a store prompt.
        syncGlobalConfig {
            if (isAutoTheme()) {
                val dark = isSystemInDarkMode()
                baseConfig.textColor = getColor(
                    if (dark) org.fossify.commons.R.color.theme_dark_text_color
                    else org.fossify.commons.R.color.theme_light_text_color
                )
                baseConfig.backgroundColor = getColor(
                    if (dark) org.fossify.commons.R.color.theme_dark_background_color
                    else org.fossify.commons.R.color.theme_light_background_color
                )
            }
            startActivity(Intent(this, MainActivity::class.java))
            finish()
        }
    }
}
