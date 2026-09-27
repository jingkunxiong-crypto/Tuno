package com.example.tuno.views

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.os.SystemClock
import android.util.AttributeSet
import android.view.MotionEvent
import androidx.appcompat.widget.AppCompatSeekBar
import kotlin.math.ceil
import kotlin.math.floor
import kotlin.math.max

/** Fixed centre playhead; the audio envelope travels beneath it on display vsync. */
class WaveformSeekBar(context: Context, attrs: AttributeSet?) : AppCompatSeekBar(context, attrs) {
    private val ink = Paint(Paint.ANTI_ALIAS_FLAG)
    private var clockPosition = 0L
    private var clockTime = 0L
    private var playing = false
    private var speed = 1f
    private var touchStartX = 0f
    private var touchStartPosition = 0L
    private var dataArrived = 0L
    private var incoming: FloatArray? = null
    private var incomingDuration = 1
    private var transition = 0f
    fun setTransition(values: FloatArray?, duration: Int, fraction: Float) {
        incoming = values
        incomingDuration = duration.coerceAtLeast(1)
        transition = if (values == null) 0f else fraction
        invalidate()
    }
    var peaks: FloatArray? = null
        set(value) {
            if (field == null && value != null) dataArrived = SystemClock.elapsedRealtime()
            field = value
            invalidate()
        }
    var isScrubbing = false
        private set
    private val windowMs = 30_000f

    init { background = null; thumb = null; splitTrack = false; setPadding(0, 0, 0, 0) }

    fun setPlaybackClock(position: Long, active: Boolean, playbackSpeed: Float) {
        if (isScrubbing) return
        clockPosition = position
        clockTime = SystemClock.elapsedRealtime()
        playing = active
        speed = playbackSpeed
        invalidate()
    }

    private fun position(): Long = if (isScrubbing) progress * 1000L else
        (clockPosition + if (playing) ((SystemClock.elapsedRealtime() - clockTime) * speed).toLong() else 0L)
            .coerceIn(0L, max.coerceAtLeast(1) * 1000L)

    override fun onDetachedFromWindow() { playing = false; super.onDetachedFromWindow() }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        // Map dragging the rolling envelope to the stock SeekBar's accessible seek listener.
        if (event.actionMasked == MotionEvent.ACTION_DOWN) {
            touchStartPosition = position()
            touchStartX = event.x
            isScrubbing = true
            parent.requestDisallowInterceptTouchEvent(true)
        }
        val target = (touchStartPosition - (event.x - touchStartX) / width.coerceAtLeast(1) * windowMs)
            .toLong().coerceIn(0L, max.coerceAtLeast(1) * 1000L)
        val mapped = MotionEvent.obtain(event)
        mapped.setLocation(target.toFloat() / (max.coerceAtLeast(1) * 1000L) * width, event.y)
        val handled = super.onTouchEvent(mapped)
        mapped.recycle()
        if (event.actionMasked == MotionEvent.ACTION_UP || event.actionMasked == MotionEvent.ACTION_CANCEL) {
            clockPosition = progress * 1000L
            clockTime = SystemClock.elapsedRealtime()
            isScrubbing = false
            parent.requestDisallowInterceptTouchEvent(false)
        }
        invalidate()
        return handled
    }

    override fun onDraw(canvas: Canvas) {
        val values = peaks
        val now = SystemClock.elapsedRealtime()
        if (values != null) drawEnvelope(canvas, values, position().toFloat(), max.coerceAtLeast(1) * 1000f, (1f - transition) * ((now - dataArrived) / 220f).coerceIn(0f, 1f))
        incoming?.takeIf { transition > 0f }?.let { drawEnvelope(canvas, it, 0f, incomingDuration * 1000f, transition) }
        if ((playing || now - dataArrived < 220) && isShown && alpha > 0f) postInvalidateOnAnimation()
        if (values != null || transition > 0f) return
        if (values == null) {
            ink.color = 0x38FFFFFF
            ink.strokeWidth = resources.displayMetrics.density * 2
            ink.strokeCap = Paint.Cap.ROUND
            canvas.drawLine(2f, height / 2f, width - 2f, height / 2f, ink)
            return
        }
    }

    private fun drawEnvelope(canvas: Canvas, values: FloatArray, current: Float, duration: Float, opacity: Float) {
        if (opacity <= 0f || values.isEmpty()) return
        val start = current - windowMs / 2
        val barMs = 650f
        val step = width * barMs / windowMs
        val barWidth = step * .70f
        for (bar in floor(start / barMs).toInt()..ceil((start + windowMs) / barMs).toInt()) {
            val time = bar * barMs
            if (time < 0 || time >= duration) continue
            val first = (time / duration * values.size).toInt().coerceIn(0, values.lastIndex)
            val last = ((time + barMs) / duration * values.size).toInt().coerceIn(first + 1, values.size)
            var peak = 0f
            for (j in first until last) peak = max(peak, values[j])
            if (peak <= 0f) continue
            val x = (time - start) / windowMs * width
            val barHeight = max(resources.displayMetrics.density * 4, peak * height * .94f)
            val relativeX = x / width.coerceAtLeast(1)
            // Past bars dissolve over the left half; both outer edges fade out smoothly.
            val past = if (relativeX < .5f) (.10f + .90f * (relativeX / .5f).coerceIn(0f, 1f)) else 1f
            val edge = (minOf(relativeX, 1f - relativeX) / .10f).coerceIn(0f, 1f)
            ink.color = android.graphics.Color.WHITE
            ink.alpha = (220 * past * edge * opacity).toInt().coerceIn(0, 255)
            canvas.drawRoundRect(x, (height - barHeight) / 2, x + barWidth, (height + barHeight) / 2, barWidth / 2, barWidth / 2, ink)
        }
    }
}
