package com.example.tuno.extensions

import android.os.Bundle
import androidx.media3.session.MediaController
import com.example.tuno.playback.CustomCommands

fun MediaController.sendCommand(command: CustomCommands, extras: Bundle = Bundle.EMPTY) = sendCustomCommand(command.sessionCommand, extras)
