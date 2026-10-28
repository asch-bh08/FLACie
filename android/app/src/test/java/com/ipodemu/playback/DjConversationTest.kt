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

    // owner, 28 Oct: one request, one answer, then the mic stops; it listens again only when the DJ asked them something
    @Test fun anAnswerEndsItAndOnlyAQuestionBackListensAgain() {
        assertEquals(DjTurn.END, DjConversation.after(DjAskDo.NONE, asked = false, turn = 1))
        assertEquals(DjTurn.END, DjConversation.after(DjAskDo.UPNEXT, asked = false, turn = 1))
        assertEquals(DjTurn.LISTEN_AGAIN, DjConversation.after(DjAskDo.NONE, asked = true, turn = 1))
    }

    @Test fun choosingWhatTheMusicDoesEndsIt() {
        for (d in listOf(DjAskDo.PLAY, DjAskDo.NEXT, DjAskDo.RESUME, DjAskDo.PAUSE)) assertEquals(DjTurn.END_MUSIC_CHOSEN, DjConversation.after(d, asked = false, turn = 1))
        assertFalse(DjConversation.resumeAtEnd(true, DjTurn.END_MUSIC_CHOSEN))
    }

    @Test fun questionsBackStopAfterTheLastTurn() {
        assertEquals(DjTurn.LISTEN_AGAIN, DjConversation.after(DjAskDo.NONE, asked = true, turn = DjConversation.MAX_TURNS - 1))
        assertEquals(DjTurn.END, DjConversation.after(DjAskDo.NONE, asked = true, turn = DjConversation.MAX_TURNS))
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
