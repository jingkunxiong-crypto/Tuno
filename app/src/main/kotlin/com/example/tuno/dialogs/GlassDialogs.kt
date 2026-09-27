package com.example.tuno.dialogs

import android.app.Activity
import android.graphics.Color
import android.view.View
import android.view.ViewGroup
import android.widget.TextView
import android.widget.CompoundButton
import androidx.appcompat.app.AlertDialog
import com.example.tuno.R
import org.fossify.commons.models.RadioItem

fun styleGlassDialog(dialog: AlertDialog) {
    dialog.window?.apply {
        setBackgroundDrawableResource(R.drawable.tuno_glass_popup)
        setDimAmount(.35f)
        if (android.os.Build.VERSION.SDK_INT >= 31) setBackgroundBlurRadius(48)
    }
    fun style(view: View) {
        if (view.background is android.graphics.drawable.ColorDrawable) view.background = null
        if (view is TextView) view.setTextColor(Color.WHITE)
        if (view is CompoundButton) view.buttonTintList = android.content.res.ColorStateList.valueOf(Color.WHITE)
        if (view is ViewGroup) for (i in 0 until view.childCount) style(view.getChildAt(i))
    }
    dialog.window?.decorView?.let(::style)
}

/** Same immediate selection behavior as the original radio chooser. */
class GlassRadioGroupDialog(activity: Activity, items: List<RadioItem>, selected: Int, callback: (Any) -> Unit) {
    init {
        val dialog = AlertDialog.Builder(activity)
            .setSingleChoiceItems(items.map { it.title }.toTypedArray(), items.indexOfFirst { it.id == selected }) { dialog, index ->
                dialog.dismiss()
                callback(items[index].id)
            }
            .setNegativeButton(org.fossify.commons.R.string.cancel, null)
            .create()
        dialog.show()
        styleGlassDialog(dialog)
    }
}
