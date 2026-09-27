package com.example.tuno.views

import android.content.Context
import android.graphics.Typeface
import android.util.AttributeSet
import android.view.Gravity
import android.view.MotionEvent
import android.view.View
import android.widget.LinearLayout
import android.widget.TextView
import androidx.core.view.doOnLayout
import androidx.core.widget.NestedScrollView
import com.example.tuno.models.LyricLine

class SyncedLyricsView @JvmOverloads constructor(context: Context, attrs: AttributeSet? = null) : NestedScrollView(context, attrs) {
    private val content = LinearLayout(context).apply { orientation = LinearLayout.VERTICAL }
    private var lines: List<LyricLine> = emptyList()
    private var activeIndex = -1
    private var manual = false
    private var normalColor = 0x80FFFFFF.toInt()
    private var activeColor = android.graphics.Color.WHITE
    private var onLineClick: ((Long) -> Unit)? = null
    var onManualBrowsing: ((Boolean) -> Unit)? = null
    private val resume = Runnable { resumeFollowing() }

    init {
        background = null
        isFillViewport = true
        isVerticalFadingEdgeEnabled = true
        setFadingEdgeLength(dp(52))
        overScrollMode = View.OVER_SCROLL_NEVER
        addView(content, LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.WRAP_CONTENT))
    }
    fun setColors(normal: Int, active: Int) { normalColor = normal; activeColor = active; restyle() }
    fun setOnLineClickListener(listener: (Long) -> Unit) { onLineClick = listener }
    fun setLyrics(lyrics: List<LyricLine>) {
        removeCallbacks(resume)
        manual = false
        onManualBrowsing?.invoke(false)
        lines = lyrics
        activeIndex = -1
        content.removeAllViews()
        lyrics.forEach { line ->
            content.addView(TextView(context).apply {
                text = line.text
                gravity = Gravity.CENTER
                textSize = 21f
                setTextColor(normalColor)
                setLineSpacing(dp(3).toFloat(), 1.1f)
                setPadding(dp(12), dp(13), dp(12), dp(13))
                if (line.timeMs != Long.MAX_VALUE) setOnClickListener {
                    onLineClick?.invoke(line.timeMs)
                    resumeFollowing()
                }
            }, LinearLayout.LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.WRAP_CONTENT))
        }
        scrollTo(0, 0)
    }
    fun updatePosition(positionMs: Long, smooth: Boolean = true) {
        if (lines.isEmpty() || lines.first().timeMs == Long.MAX_VALUE) return
        val index = lines.binarySearchBy(positionMs) { it.timeMs }.let { if (it >= 0) it else -it - 2 }
        if (index == activeIndex) return
        val old = activeIndex
        activeIndex = index
        style(old); style(index)
        if (!manual) content.doOnLayout { centerActiveLine(smooth) }
    }
    fun resumeFollowing() {
        removeCallbacks(resume)
        manual = false
        onManualBrowsing?.invoke(false)
        centerActiveLine(true)
    }
    override fun dispatchTouchEvent(event: MotionEvent): Boolean {
        if (event.actionMasked == MotionEvent.ACTION_DOWN) beginBrowsing()
        val handled = super.dispatchTouchEvent(event)
        if (manual && (event.actionMasked == MotionEvent.ACTION_UP || event.actionMasked == MotionEvent.ACTION_CANCEL)) {
            removeCallbacks(resume)
            postDelayed(resume, 4500)
        }
        return handled
    }
    private fun beginBrowsing() { removeCallbacks(resume); manual = true; onManualBrowsing?.invoke(true) }
    override fun onDetachedFromWindow() { removeCallbacks(resume); super.onDetachedFromWindow() }
    override fun onSizeChanged(w: Int, h: Int, oldw: Int, oldh: Int) {
        super.onSizeChanged(w, h, oldw, oldh)
        content.setPadding(0, (h / 2 - dp(36)).coerceAtLeast(0), 0, (h / 2 - dp(36)).coerceAtLeast(0))
        content.doOnLayout { if (!manual) centerActiveLine(false) }
    }
    private fun centerActiveLine(smooth: Boolean) {
        val item = content.getChildAt(activeIndex) ?: return
        val target = (item.top + item.height / 2 - height / 2).coerceAtLeast(0)
        if (smooth) smoothScrollTo(0, target) else scrollTo(0, target)
    }
    private fun restyle() { for (i in 0 until content.childCount) style(i) }
    private fun style(index: Int) {
        val item = content.getChildAt(index) as? TextView ?: return
        val active = index == activeIndex
        item.setTextColor(if (active) activeColor else normalColor)
        item.setTypeface(null, if (active) Typeface.BOLD else Typeface.NORMAL)
        item.animate().scaleX(if (active) 1.025f else 1f).scaleY(if (active) 1.025f else 1f).setDuration(200).start()
    }
    private fun dp(value: Int) = (value * resources.displayMetrics.density).toInt()
}
