package com.example.tuno.views

import android.graphics.*
import android.graphics.drawable.Drawable

/** Static, softly diffused light behind the translucent library surfaces. */
class HomeGlassDrawable : Drawable() {
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG)
    private var base: Shader? = null
    private var coolLight: Shader? = null
    private var silverLight: Shader? = null

    override fun onBoundsChange(bounds: Rect) {
        val w = bounds.width().toFloat().coerceAtLeast(1f)
        val h = bounds.height().toFloat().coerceAtLeast(1f)
        base = LinearGradient(0f, 0f, w * .7f, h,
            intArrayOf(Color.rgb(48, 58, 70), Color.rgb(28, 35, 45), Color.rgb(17, 22, 29)), null, Shader.TileMode.CLAMP)
        coolLight = RadialGradient(w * .95f, h * .30f, w * .9f,
            intArrayOf(0x425C8095, 0x005C8095), null, Shader.TileMode.CLAMP)
        silverLight = RadialGradient(w * .05f, h * .70f, w * .8f,
            intArrayOf(0x284F626D, 0x004F626D), null, Shader.TileMode.CLAMP)
    }

    override fun draw(canvas: Canvas) {
        for (shader in arrayOf(base, coolLight, silverLight)) {
            paint.shader = shader
            canvas.drawRect(bounds, paint)
        }
    }
    override fun setAlpha(alpha: Int) { paint.alpha = alpha; invalidateSelf() }
    override fun setColorFilter(colorFilter: ColorFilter?) { paint.colorFilter = colorFilter; invalidateSelf() }
    @Deprecated("Deprecated in Android")
    override fun getOpacity() = PixelFormat.OPAQUE
}
