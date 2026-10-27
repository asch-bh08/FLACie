package com.ipodemu.playback

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** Conversation mode for "talk to the DJ", the same cases as DjConversationTests on the server. */
class DjConversationTest {
    @Test fun goodbyesEndIt() {
        for (s in listOf("that's all", "That's all.", "thats it", "thanks", "Thank you", "thank you DJ", "okay that's all", "no thanks", "bye", "goodbye",
            "never mind", "nevermind", "I'm good", "I’m done", "cheers", "stop listening", "nothing else"))
            assertTrue(s, DjConversation.isGoodbye(s))
    }

    @Test fun requestsThatMerelyContainTheWordsDoNot() {
        for (s in listOf("play that's all folks", "thanks for the memories by fall out boy", "what's this song", "play bye bye bye", "something chill", "", null))
            assertFalse(s.toString(), DjConversation.isGoodbye(s))
    }

    @Test fun anAnswerOrASmallChangeKeepsListening() {
        for (d in listOf(DjAskDo.NONE, DjAskDo.UPNEXT, DjAskDo.VOLUMEUP, DjAskDo.DJON)) assertEquals(DjTurn.LISTEN_AGAIN, DjConversation.after(d, 1))
    }

    @Test fun choosingWhatTheMusicDoesEndsIt() {
        for (d in listOf(DjAskDo.PLAY, DjAskDo.NEXT, DjAskDo.RESUME, DjAskDo.PAUSE)) assertEquals(DjTurn.END_MUSIC_CHOSEN, DjConversation.after(d, 1))
        assertFalse(DjConversation.resumeAtEnd(true, DjTurn.END_MUSIC_CHOSEN))
    }

    @Test fun itStopsByItselfAfterTheLastTurn() {
        assertEquals(DjTurn.LISTEN_AGAIN, DjConversation.after(DjAskDo.NONE, DjConversation.MAX_TURNS - 1))
        assertEquals(DjTurn.END, DjConversation.after(DjAskDo.NONE, DjConversation.MAX_TURNS))
    }

    @Test fun theSongTheMicPausedCarriesOn() {
        assertTrue(DjConversation.resumeAtEnd(true, DjTurn.END))
        assertFalse(DjConversation.resumeAtEnd(false, DjTurn.END))
    }
}
