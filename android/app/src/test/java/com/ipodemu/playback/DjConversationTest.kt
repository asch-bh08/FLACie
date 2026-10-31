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

    // owner, 31 Oct: smart turn-taking. A plain control ends it at once; a normal answer leaves a short window; a question back a longer one
    @Test fun aPlainControlEndsItAtOnceWithNoListeningAgain() {
        for (d in listOf(DjAskDo.UPNEXT, DjAskDo.VOLUMEUP, DjAskDo.VOLUMEDOWN, DjAskDo.DJON, DjAskDo.DJOFF, DjAskDo.SEARCH))
            assertEquals(DjTurn.END, DjConversation.after(d, asked = false, turn = 1))
        assertEquals(DjTurn.END, DjConversation.after(DjAskDo.NONE, asked = false, turn = 1, intent = "ThumbsDown"))
    }

    @Test fun aNormalAnswerListensAgainForAShortWhile() {
        assertEquals(DjTurn.LISTEN_AGAIN, DjConversation.after(DjAskDo.NONE, asked = false, turn = 1))
        assertEquals(DjTurn.LISTEN_AGAIN, DjConversation.after(DjAskDo.NONE, asked = false, turn = 1, intent = "Answer"))
        assertEquals(4000L, DjConversation.FOLLOW_UP_MS)
        assertEquals(DjConversation.FOLLOW_UP_MS, DjConversation.listenWindowMs(asked = false))
    }

    @Test fun aQuestionBackListensAgainForLonger() {
        assertEquals(DjTurn.LISTEN_AGAIN, DjConversation.after(DjAskDo.NONE, asked = true, turn = 1, intent = "Clarify"))
        assertEquals(8000L, DjConversation.ANSWER_WINDOW_MS)
        assertEquals(DjConversation.ANSWER_WINDOW_MS, DjConversation.listenWindowMs(asked = true))
    }

    @Test fun aGoodbyeEndsItWithAnytimeAndOverASecondOfQuietEndsThePersonsTurn() {
        assertEquals(DjTurn.END, DjConversation.after(DjAskDo.NONE, asked = false, turn = 1, intent = "Goodbye"))
        assertEquals("Anytime.", DjConversation.ANYTIME)
        assertEquals(1200, DjConversation.END_OF_SPEECH_MS)
    }

    @Test fun choosingWhatTheMusicDoesEndsIt() {
        for (d in listOf(DjAskDo.PLAY, DjAskDo.NEXT, DjAskDo.RESUME, DjAskDo.PAUSE)) assertEquals(DjTurn.END_MUSIC_CHOSEN, DjConversation.after(d, asked = false, turn = 1))
        assertFalse(DjConversation.resumeAtEnd(true, DjTurn.END_MUSIC_CHOSEN))
    }

    @Test fun listeningAgainStopsAfterTheLastTurn() {
        assertEquals(DjTurn.LISTEN_AGAIN, DjConversation.after(DjAskDo.NONE, asked = true, turn = DjConversation.MAX_TURNS - 1))
        assertEquals(DjTurn.LISTEN_AGAIN, DjConversation.after(DjAskDo.NONE, asked = false, turn = DjConversation.MAX_TURNS - 1))
        assertEquals(DjTurn.END, DjConversation.after(DjAskDo.NONE, asked = true, turn = DjConversation.MAX_TURNS))
        assertEquals(DjTurn.END, DjConversation.after(DjAskDo.NONE, asked = false, turn = DjConversation.MAX_TURNS))
    }

    @Test fun theSongTheMicPausedCarriesOn() {
        assertTrue(DjConversation.resumeAtEnd(true, DjTurn.END))
        assertFalse(DjConversation.resumeAtEnd(false, DjTurn.END))
    }

    @Test fun theServersQuestionIsReadFromTheAnswer() {
        assertTrue(DjAskAnswer.parse("""{"do":"none","say":"Do you mean your hearted songs?","intent":"Clarify"}""").asked)
        assertFalse(DjAskAnswer.parse("""{"do":"none","say":"This is Creep.","intent":"WhatsThis"}""").asked)
    }
}
