package com.example.tuno.helpers

import android.graphics.drawable.Drawable

/** One-shot handoff of the already loaded list thumbnail; never retains a View or Activity. */
object CoverTransitionPreview {
    data class Scene(val bitmap: android.graphics.Bitmap, val cover: android.graphics.RectF, val originY: Int,
        val title: android.graphics.RectF?, val titleSize: Float,
        val bar: android.graphics.Bitmap?, val barBounds: android.graphics.RectF?)
    private var scene: Scene? = null
    fun capture(root: android.view.View, source: android.view.View) {
        val origin = IntArray(2)
        val position = IntArray(2)
        root.getLocationOnScreen(origin)
        source.getLocationOnScreen(position)
        val bitmap = android.graphics.Bitmap.createBitmap((root.width / 2).coerceAtLeast(1), (root.height / 2).coerceAtLeast(1), android.graphics.Bitmap.Config.ARGB_8888)
        val canvas = android.graphics.Canvas(bitmap)
        canvas.scale(bitmap.width.toFloat() / root.width, bitmap.height.toFloat() / root.height)
        val alpha = source.alpha
        fun bounds(view: android.view.View): android.graphics.RectF {
            val xy = IntArray(2)
            view.getLocationOnScreen(xy)
            return android.graphics.RectF(xy[0].toFloat(), xy[1].toFloat(), (xy[0] + view.width).toFloat(), (xy[1] + view.height).toFloat())
        }
        val row = source.parent as? android.view.ViewGroup
        val title = row?.findViewById<android.widget.TextView>(com.example.tuno.R.id.track_title)
            ?: row?.findViewById<android.widget.TextView>(com.example.tuno.R.id.current_track_label)
        val titleAlpha = title?.alpha ?: 1f
        val titleBounds = title?.let { view ->
            bounds(view).apply {
                val textLayout = view.layout
                if (textLayout != null && textLayout.lineCount > 0) {
                    val textOrigin = left + view.totalPaddingLeft - view.scrollX
                    val visibleLeft = left + view.totalPaddingLeft
                    val visibleRight = right - view.totalPaddingRight
                    left = (textOrigin + textLayout.getLineLeft(0)).coerceIn(visibleLeft, visibleRight)
                    right = (textOrigin + textLayout.getLineRight(0)).coerceIn(left, visibleRight)
                }
            }
        }
        val bar = root.findViewById<android.view.View>(com.example.tuno.R.id.current_track_bar)?.takeIf { it.isShown && it.width > 0 && it.height > 0 }
        val barAlpha = bar?.alpha ?: 1f
        var barBitmap: android.graphics.Bitmap? = null
        try {
            source.alpha = 0f
            title?.alpha = 0f
            if (bar != null) {
                barBitmap = android.graphics.Bitmap.createBitmap(bar.width, bar.height, android.graphics.Bitmap.Config.ARGB_8888)
                bar.draw(android.graphics.Canvas(barBitmap!!))
                bar.alpha = 0f
            }
            root.draw(canvas)
        } finally {
            source.alpha = alpha
            title?.alpha = titleAlpha
            bar?.alpha = barAlpha
        }
        scene = Scene(bitmap, android.graphics.RectF(position[0].toFloat(), position[1].toFloat(), (position[0] + source.width).toFloat(), (position[1] + source.height).toFloat()), origin[1], titleBounds, title?.textSize ?: 0f, barBitmap, bar?.let(::bounds))
    }
    fun takeScene(): Scene? = scene.also { scene = null }
    private var name: String? = null
    private var image: Drawable? = null
    fun put(transitionName: String, drawable: Drawable?) {
        name = transitionName
        image = drawable?.constantState?.newDrawable()?.mutate()
    }
    fun take(transitionName: String): Drawable? {
        val result = image.takeIf { name == transitionName }
        name = null
        image = null
        return result
    }
}
