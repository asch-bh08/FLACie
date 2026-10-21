package com.ipodemu.player

import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.composed
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusDirection
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusProperties
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.input.key.KeyEventType
import androidx.compose.ui.input.key.key
import androidx.compose.ui.input.key.onPreviewKeyEvent
import androidx.compose.ui.input.key.type
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.text.TextRange
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.TextFieldValue
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/** The one focus ring of the app (D-pad / gamepad): a 2.5 dp border, white on dark surfaces, the accent on white ones. */
val FocusRingWidth = 2.5.dp

/** Draws the ring when [focused] is true. */
fun Modifier.ringWhen(focused: Boolean, shape: Shape, color: Color = Color.White): Modifier =
    if (focused) this.border(FocusRingWidth, color, shape) else this

/** Tracks focus of this element (or anything inside it) and draws the shared ring around it. */
fun Modifier.focusRing(shape: Shape, color: Color = Color.White): Modifier = composed {
    var f by remember { mutableStateOf(false) }
    this.onFocusChanged { f = it.hasFocus }.ringWhen(f, shape, color)
}

/** Which way a D-pad key moves focus out of a text field, or null if the field should keep the key. [selStart]/[selEnd] are the
 * cursor: Left/Right only leave from the very start/end of the text, Up/Down always leave. */
fun leaveDirection(key: Key, selStart: Int, selEnd: Int, length: Int): FocusDirection? = when (key) {
    Key.DirectionDown -> FocusDirection.Down
    Key.DirectionUp -> FocusDirection.Up
    Key.DirectionLeft -> if (selStart == 0 && selEnd == 0) FocusDirection.Left else null
    Key.DirectionRight -> if (selStart == length && selEnd == length) FocusDirection.Right else null
    else -> null
}

/**
 * A search/filter box that is safe for a D-pad or gamepad: focus can land on it and leave again in every direction, and the soft keyboard
 * only opens when it is clicked or confirmed (Enter / centre), never just because focus passed over it.
 * While not editing the text field cannot take focus (a transparent target on top of it takes it instead).
 */
@Composable
fun SearchPill(
    value: String, onValueChange: (String) -> Unit, placeholder: String, modifier: Modifier = Modifier,
    shape: Shape = RoundedCornerShape(16.dp), background: Color = Palette.surface2, borderColor: Color = Color(0x22FFFFFF),
    height: Dp = 46.dp, textSize: Float = 15f, onSubmit: () -> Unit = {}, trailing: @Composable () -> Unit = {},
) {
    val sc = LocalScheme.current
    val focus = LocalFocusManager.current
    val keyboard = LocalSoftwareKeyboardController.current
    var editing by remember { mutableStateOf(false) }
    var inside by remember { mutableStateOf(false) }
    var refocusTarget by remember { mutableStateOf(false) }
    val fieldFr = remember { FocusRequester() }
    val targetFr = remember { FocusRequester() }
    var tfv by remember { mutableStateOf(TextFieldValue(value, TextRange(value.length))) }
    if (tfv.text != value) tfv = TextFieldValue(value, TextRange(value.length))
    LaunchedEffect(editing) {
        if (editing) { try { fieldFr.requestFocus() } catch (_: Exception) {}; keyboard?.show() }
        else if (refocusTarget) { refocusTarget = false; try { targetFr.requestFocus() } catch (_: Exception) {} }
    }
    Row(
        modifier.fillMaxWidth().height(height).clip(shape).background(background)
            .onFocusChanged { inside = it.hasFocus }
            .border(if (inside) FocusRingWidth else 1.dp, if (inside) Color.White else if (value.isNotEmpty()) sc.accent else borderColor, shape)
            .padding(horizontal = 14.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        GlyphIcon(Glyph.SEARCH, Modifier.size(18.dp), sc.onBgDim)
        Box(Modifier.padding(start = 10.dp).weight(1f).fillMaxHeight(), contentAlignment = Alignment.CenterStart) {
            if (value.isEmpty()) Txt(placeholder, size = textSize + 1f, color = sc.onBgDim)
            BasicTextField(
                tfv, { tfv = it; if (it.text != value) onValueChange(it.text) }, singleLine = true, cursorBrush = SolidColor(sc.accent),
                textStyle = TextStyle(color = sc.onBg, fontSize = textSize.sp), readOnly = !editing,
                keyboardOptions = KeyboardOptions(autoCorrect = false, imeAction = ImeAction.Search),
                keyboardActions = KeyboardActions(onSearch = { keyboard?.hide(); refocusTarget = true; editing = false; onSubmit() }),
                modifier = Modifier.fillMaxWidth()
                    .semantics { contentDescription = placeholder }   // the placeholder is drawn beside the field, so a screen reader would find it unnamed
                    .focusProperties { canFocus = editing }
                    .focusRequester(fieldFr)
                    .onFocusChanged { if (!it.isFocused && editing) { editing = false; keyboard?.hide() } }
                    .onPreviewKeyEvent { ev ->
                        if (!editing || ev.type != KeyEventType.KeyDown) return@onPreviewKeyEvent false
                        if (ev.key == Key.DirectionCenter) { keyboard?.show(); return@onPreviewKeyEvent true }
                        val dir = leaveDirection(ev.key, tfv.selection.start, tfv.selection.end, tfv.text.length) ?: return@onPreviewKeyEvent false
                        keyboard?.hide(); focus.moveFocus(dir); true
                    },
            )
            if (!editing) Box(
                Modifier.matchParentSize().focusRequester(targetFr)
                    .semantics { contentDescription = placeholder }   // the tap that starts typing, named as the field it opens
                    .clickable(remember { MutableInteractionSource() }, null) { editing = true },
            )
        }
        trailing()
    }
}

