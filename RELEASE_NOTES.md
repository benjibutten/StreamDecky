# What's changed

<!-- Update this list together with user-visible changes under src/. -->

- Added a text helper widget to the overlay: a roomy, dyslexia-friendly writing
  box with your choice of installed font, text size, and either a warm off-white
  or a dark writing area.
- Fix spelling (or Ctrl+Enter in the box) replaces what you wrote with a
  respelled version from DeepSeek, keeping your language, wording, and any
  /commands. Undo brings your own words straight back, and Copy always copies
  the box as it stands.
- Ask (or Ctrl+Shift+Enter) answers a short question without touching what you
  wrote. The answer appears in its own card underneath, and Clear empties both
  at once.
- Quick answers can be checked against the web with a Brave Search API key.
  Every answer says whether it was sourced or came from the model alone, and
  links the sources it used.
- Added a text helper section in Settings for the DeepSeek and Brave Search API
  keys, each with a Test Key button that only ever sends a built-in sample, plus
  the model, thinking levels, when to search, and editable prompts that can be
  reset to their defaults.
- API keys are protected with Windows DPAPI for your Windows account, stored
  outside the profile store, and never included in a profile export.
- The text helper takes the caret as soon as it appears, keeps its text across
  closing and reopening the overlay, and is draggable, resizable from both
  bottom corners, and remembered per profile.
- Updated the privacy policy to cover exactly what the text helper sends to
  DeepSeek and Brave Search, and when.
- Updated application and test dependencies.
