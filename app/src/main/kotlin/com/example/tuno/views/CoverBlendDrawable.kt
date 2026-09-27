package com.example.tuno.views

import android.graphics.Canvas
import android.graphics.ColorFilter
import android.graphics.PixelFormat
import android.graphics.Rect
import android.graphics.drawable.Drawable

/** A gesture-controlled crossfade; the background never travels with the cover. */
class CoverBlendDrawable(val from: Drawable, val to: Drawable) : Drawable() {
    var progress = 0f
        set(value) { field = value.coerceIn(0f, 1f); invalidateSelf() }
    override fun onBoundsChange(bounds: Rect) { from.bounds = bounds; to.bounds = bounds }
    override fun draw(canvas: Canvas) {
        from.draw(canvas)
        val layer = canvas.saveLayerAlpha(bounds.left.toFloat(), bounds.top.toFloat(), bounds.right.toFloat(), bounds.bottom.toFloat(), (255 * progress).toInt())
        to.draw(canvas)
        canvas.restoreToCount(layer)
    }
    override fun setAlpha(alpha: Int) {}
    override fun setColorFilter(colorFilter: ColorFilter?) {}
    @Deprecated("Deprecated in Android") override fun getOpacity() = PixelFormat.TRANSLUCENT
}
