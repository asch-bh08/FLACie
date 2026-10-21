package com.ipodemu.library

import java.io.IOException
import java.net.ConnectException
import java.net.NoRouteToHostException
import java.net.SocketTimeoutException
import java.net.UnknownHostException
import javax.net.ssl.SSLException

/**
 * What a person is told when something fails, as on the web (FriendlyError.cs): what happened in plain words and what to try. An exception's own text
 * ("Unable to resolve host ...", "HTTP 404") is never shown; only the sentences FLACie writes itself (an IOException with a whole sentence) pass through.
 */
object FriendlyError {
    private const val GENERIC = "Something went wrong. Try again."
    private val httpCode = Regex("""\bHTTP (\d{3})\b""")

    fun of(e: Throwable, what: String? = null): String {
        val line = line(e)
        return if (what == null) line else what + ": " + line.replaceFirstChar { it.lowercase() }
    }

    private fun line(e: Throwable): String = when (e) {
        is SocketTimeoutException -> "The server took too long to answer. Try again in a moment."
        is UnknownHostException, is ConnectException, is NoRouteToHostException -> "Can't reach the server. Check the address and that it's switched on."
        is SSLException -> "Couldn't make a secure connection to the server. Check whether its address starts with https or http."
        is IOException -> {
            val m = e.message.orEmpty()
            val code = httpCode.find(m)?.groupValues?.get(1)?.toIntOrNull()
            when {
                code == 401 || code == 403 -> "The server refused the sign-in. Check the user name and password."
                code == 404 -> "The server doesn't have that. Check the address."
                code != null && code >= 500 -> "The server had a problem answering. Try again in a moment."
                code != null -> "Can't reach the server. Check the address and that it's switched on."
                // a sentence FLACie wrote itself ("The code expired. Try again.")
                m.length > 12 && m.contains(' ') && m.first().isUpperCase() && !m.contains("Exception") && !m.contains("://") -> m
                else -> e.cause?.let { line(it) } ?: GENERIC
            }
        }
        else -> e.cause?.let { line(it) } ?: GENERIC
    }
}
