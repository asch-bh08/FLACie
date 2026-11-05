package com.ipodemu.player

import android.speech.SpeechRecognizer
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** The on-device recogniser can fail at once (no language pack, busy, disconnected): the mic then tries the online one instead of giving up (owner, 5 Nov: "a big focus on the microphone"). */
class MicRetryTest {
    @Test fun a_broken_on_device_recogniser_is_tried_again_online() {
        for (e in listOf(SpeechRecognizer.ERROR_SERVER, SpeechRecognizer.ERROR_CLIENT, SpeechRecognizer.ERROR_RECOGNIZER_BUSY, 11, 12, 13, 14)) assertTrue("error $e", MicSpeech.retryOnline(e))
    }

    @Test fun nothing_said_or_no_microphone_is_not_retried() {
        for (e in listOf(SpeechRecognizer.ERROR_NO_MATCH, SpeechRecognizer.ERROR_SPEECH_TIMEOUT, SpeechRecognizer.ERROR_INSUFFICIENT_PERMISSIONS, SpeechRecognizer.ERROR_AUDIO, SpeechRecognizer.ERROR_NETWORK)) assertFalse("error $e", MicSpeech.retryOnline(e))
    }
}
