package com.example.tuno.views

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.util.AttributeSet
import org.fossify.commons.views.MySeekBar

/** The speed scale has normal playback at its midpoint. */
class SpeedSeekBar(context: Context, attrs: AttributeSet) : MySeekBar(context, attrs) {
    private val markerPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.WHITE }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val x = paddingLeft + (width - paddingLeft - paddingRight) / 2f
        val y = paddingTop + (height - paddingTop - paddingBottom) / 2f
        canvas.drawCircle(x, y, 3f * resources.displayMetrics.density, markerPaint)
    }
}
