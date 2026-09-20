# What's changed

<!-- Update this list together with user-visible changes under src/. -->

- Settings is no longer one long scroll: the window is wider and split into
  Overlay, Input, Text Helper, Quick Answers and General, picked from a list
  down the left side. It also never opens larger than your desktop, whatever
  your screen size or display scaling.
- The text helper can send what you wrote straight into another program. Send
  (or Ctrl+Alt+Enter, AltGr+Enter on a Nordic layout) closes the overlay, hands
  the keyboard back to whatever had it, and runs a step list you set up per
  profile — the same editor the clipboard action uses. A TextInput step can put
  {{text}} anywhere in the line, so a game chat is one key press to open it plus
  "/me {{text}}" with Enter after. The button stays hidden until you add steps.
- If another program already owns your overlay hotkey, Windows refuses it
  silently and the overlay simply never opens. A tray notification now says so
  and points at Settings, both at startup and whenever you change the key.
- The tray menu has an Open log folder entry, and the log rotates at 2 MB
  instead of growing forever.
- A profile store that cannot be read is recovered from the startup backup
  instead of leaving you with an empty deck, and the unreadable file is kept
  under a timestamped name instead of being overwritten by the next save.
- Saved data is flushed to disk before the new file replaces the old one, so a
  power cut can no longer leave an empty profile store behind.
- Closing the app no longer waits forever on a stalled disk.
- Moving the selection around with a gamepad no longer rewrote the profile file
  several times a second.
- The overlay no longer stops responding when a stick is pushed all the way
  left or down.
- A key step that fails part way through no longer leaves Ctrl, Shift or Alt
  stuck down for the rest of the session, and Pause/Break is sent properly
  instead of doing nothing.
- Typing into a maximized window no longer un-maximizes it first.
- When another program is holding the clipboard, a paste step stops the action
  instead of pasting whatever happened to be there, and copying quick text no
  longer fails with an error.
- An error in a click or a timer no longer takes the whole deck down and skips
  the save on the way out; it is written to the log instead.
- Update checks back off after a failure instead of calling GitHub again on
  every start, and an install that fails after you agreed to it now says so
  instead of the progress window just disappearing.
- Starting a second copy of StreamDecky raises the window you already have,
  faster and without the window flashing or animating up from the taskbar.
- Gamepad polling no longer works through empty controller slots thirty times a
  second when no controller is connected.
