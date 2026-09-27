package com.example.tuno.views

import android.graphics.*
import android.graphics.drawable.Drawable
import androidx.core.graphics.drawable.toBitmap

/** Blur the whole cover and retain its dominant hue beneath a light smoke scrim. */
class CoverGlassDrawable(cover: Drawable) : Drawable() {
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG or Paint.FILTER_BITMAP_FLAG)
    private val texture: Bitmap
    init {
        val sample = cover.toBitmap(32, 32)
        val pixels = IntArray(32 * 32)
        val weights = FloatArray(512)
        val reds = IntArray(512); val greens = IntArray(512); val blues = IntArray(512)
        val counts = IntArray(512)
        val hsv = FloatArray(3)
        for (y in 2..29) for (x in 2..29) {
            val c = sample.getPixel(x, y)
            if (Color.alpha(c) < 128) continue
            Color.colorToHSV(c, hsv)
            if (hsv[2] < .12f || hsv[2] > .94f && hsv[1] < .12f) continue
            val bin = (Color.red(c) / 32) * 64 + (Color.green(c) / 32) * 8 + Color.blue(c) / 32
            weights[bin] += .6f + hsv[1]
            reds[bin] += Color.red(c); greens[bin] += Color.green(c); blues[bin] += Color.blue(c); counts[bin]++
        }
        val dominantBin = weights.indices.maxByOrNull { weights[it] } ?: 0
        val count = counts[dominantBin]
        val dominant = if (count > 0) Color.rgb(reds[dominantBin] / count, greens[dominantBin] / count, blues[dominantBin] / count)
            else Color.rgb(90, 94, 104)
        for (y in 0..31) for (x in 0..31) {
            val c = sample.getPixel(x.coerceIn(2, 29), y.coerceIn(2, 29))
            pixels[y * 32 + x] = androidx.core.graphics.ColorUtils.blendARGB(c or Color.BLACK, dominant, .3f)
        }
        repeat(6) {
            val previous = pixels.copyOf()
            for (y in 0..31) for (x in 0..31) {
                var r = 0; var g = 0; var b = 0
                for (dy in -2..2) for (dx in -2..2) {
                    val c = previous[(y + dy).coerceIn(0, 31) * 32 + (x + dx).coerceIn(0, 31)]
                    r += Color.red(c); g += Color.green(c); b += Color.blue(c)
                }
                pixels[y * 32 + x] = Color.rgb(r / 25, g / 25, b / 25)
            }
        }
        texture = Bitmap.createBitmap(pixels, 32, 32, Bitmap.Config.ARGB_8888)
    }
    override fun draw(canvas: Canvas) {
        paint.shader = null
        canvas.drawBitmap(texture, null, bounds, paint)
        paint.shader = LinearGradient(0f, bounds.top.toFloat(), 0f, bounds.bottom.toFloat(),
            intArrayOf(0x60181B22, 0x78181B22, 0x9812161D.toInt()), null, Shader.TileMode.CLAMP)
        canvas.drawRect(bounds, paint)
        paint.shader = null
    }
    override fun setAlpha(alpha: Int) { paint.alpha = alpha }
    override fun setColorFilter(colorFilter: ColorFilter?) { paint.colorFilter = colorFilter }
    @Deprecated("Deprecated in Android")
    override fun getOpacity() = PixelFormat.OPAQUE
}
