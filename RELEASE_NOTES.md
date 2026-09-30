# What's changed

<!-- Update this list together with user-visible changes under src/. -->

- **StreamDecky has an installer.** It installs StreamDecky into Program Files,
  adds it to the Start menu and to Apps in Windows Settings, and StreamDecky
  updates itself through it from then on. The zip is still in every release,
  and a copy extracted from it keeps updating itself as before. To move to the
  installer, run it and then delete the old StreamDecky folder; your profiles
  and settings stay where they are. If the old copy is still running, Setup
  asks you to exit it first.
- **New setting, "Run StreamDecky as administrator"** in Settings › General.
  While a game or program running as administrator has focus, Windows drops the
  keys StreamDecky types and presses into it. Running StreamDecky as
  administrator too lets them through. Installed with the installer, it also
  starts as administrator when you sign in, without asking.
- When a button's keys go to a program running as administrator, StreamDecky
  says so once in a notification and points to the new setting.
- **The MicMixer music widget connects when StreamDecky runs as
  administrator.** It used to stay offline.
- **The music library shows what is playing.** The current track has a green
  mark and a bold title, and its button pauses and resumes it instead of
  starting it over.
- The music widget explains why MicMixer cannot start music, such as an empty
  music library, instead of showing a code like `empty_library`.
- After an update, StreamDecky shows what's new once. **About** links to the
  same list.
- **Page tabs can be styled.** Settings › Overlay sets the color of the
  current tab, the tab text and the tab bar, the bar's opacity and the text
  size, with a preview. Each page can also have its own tab color, set from
  the page row's ⋯ menu: its tab shows it faded, and in full while the page
  is open.
- **A tidier main window.** Each of the profile and page rows keeps its add
  button and moves the rest into a ⋯ menu, including new and deleted virtual
  layouts, which could only be deleted while one was open. The page dropdown
  sits between the arrows, and the grid size lives in Settings › Overlay only.
- Right-click menus are dark, like the rest of StreamDecky, and buttons no longer
  turn pale blue with unreadable text when you point at them.
- The overlay only rings a button when gamepad support is on, where the ring is
  the gamepad cursor. It no longer shows the button selected in the editor,
  and opening the overlay no longer changes that selection.
- Colors are picked in a dark color picker of StreamDecky's own, with a
  gradient, presets and a hex field, instead of the Windows dialog. It keeps
  the last ten colors you chose until StreamDecky closes, so several buttons
  are quick to give the same color. The color swatches work with the keyboard
  and screen readers.
- **The text helper stays readable over any background.** Only its backdrop
  follows the overlay opacity; the writing box and buttons are solid, and the
  button row has the same tint as the rest of the widget.
- Notes areas and the note text size moved from the main window to their own
  page in Settings.
- Every window uses the thin scrollbar, and screen readers announce the
  icon-only buttons.
