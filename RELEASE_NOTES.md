# What's changed

<!-- Update this list together with user-visible changes under src/. -->

- **StreamDecky has an installer.** It installs StreamDecky into Program Files,
  adds it to the Start menu and to Apps in Windows Settings, and StreamDecky
  updates itself through it from then on. The zip is still in every release,
  and a copy extracted from it keeps updating itself as before. To move to the
  installer, run it and then delete the old StreamDecky folder; your profiles
  and settings stay where they are.
- **New setting, "Run StreamDecky as administrator"** in Settings › General.
  While a game or program running as administrator has focus, Windows drops the
  keys StreamDecky types and presses into it. Running StreamDecky as
  administrator too lets them through. Installed with the installer, it also
  starts as administrator when you sign in, without asking.
- When a button's keys go to a program running as administrator, StreamDecky
  says so once in a notification and points to the new setting.
- **The MicMixer music widget connects when StreamDecky runs as
  administrator.** It used to stay offline.
- After an update, StreamDecky shows what's new once. **About** links to the
  same list.
- Every window uses the thin scrollbar, and screen readers announce the
  icon-only buttons.
