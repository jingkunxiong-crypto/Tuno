package com.example.tuno.extensions

import android.os.Handler
import android.os.Looper

fun Looper.post(callback: () -> Unit) = Handler(this).post(callback)
