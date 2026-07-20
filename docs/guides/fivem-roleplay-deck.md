# Build a FiveM roleplay deck

This guide sets up StreamDecky for a roleplay server: one-press emotes, ready
`/me` and `/do` lines, and a dispatch form — all on an overlay that opens on top
of the game and types back into the chat box.

If you just want a working starting point, import the
[FiveM Roleplay template](../templates/fivem-roleplay.json) (**Settings →
Import Profile**) and skip to [Adapt it to your server](#adapt-it-to-your-server).
The rest of this guide explains how it is built so you can extend it.

## Before you start

- Install and run StreamDecky, and set an overlay hotkey you can reach mid-scene
  (**Settings → overlay hotkey**). Something like `Ctrl+F12` or a spare mouse
  button works well.
- Know your server's chat key. On most FiveM servers it is `T`. The examples
  below assume `T`; change it if your server differs.
- Know how your server's emotes are triggered. Many use an emote resource with
  `/e <name>` commands (`/e handsup`, `/e dance`). Some use a radial menu
  instead. This guide uses `/e` commands; adapt the command text if your server
  is different.

## 1. One button that plays an emote

The core trick is a **Multi-Action** button that opens the chat, types a
command, and presses Enter:

1. Select an empty slot and set **Action** to *Multi-Action*.
2. Add three steps:
   - **Key Press** — `t` (opens the chat box).
   - **Delay** — `150` ms (gives the chat box time to focus).
   - **Text Input** — text `/e handsup`, mode *Paste from clipboard*, and tick
     *Press Enter after*.
3. Give it a title (`Hands Up`), an icon (🙌), and a color.

Press your overlay hotkey in-game, click the button, and the character raises
their hands. Duplicate the button (`Ctrl+C` / `Ctrl+V`) and change only the
command text to build out `Point`, `Wave`, `Sit`, `Kneel`, and so on.

> Prefer *Paste from clipboard* for chat commands — it is instant and reliable.
> Switch a button to *Simulate typing* only if your server blocks pasted text.

## 2. A hidden page of extra emotes

A full deck of dances would crowd the main page. Put them on a **virtual
layout** — a hidden page you reach by button:

1. Create a new virtual layout (call it `Emotes`).
2. Fill it with more Multi-Action emote buttons (`/e dance`, `/e dance2`,
   `/e lean`, `/e clap`, …).
3. Add a **Switch Layout** button on it that targets your main page, titled
   `← Scene`, so you can get back.
4. On the main page, add a **Switch Layout** button titled `Dances` that targets
   the `Emotes` layout.

Now the main deck stays clean and the dances are one hop away.

## 3. Ready-made `/me` and `/do` lines

Typed roleplay is faster from the **quick-text** panel than from the keyboard.

1. Open the quick-text editor and add a few categories: `/me actions`,
   `/do details`, `Comms`.
2. Add lines under each, for example:
   - `/me checks the person for a pulse and breathing.`
   - `/do The engine is still warm to the touch.`
   - `Dispatch, show me en route.`
3. Set the quick-text **action pipeline** once so a click delivers the whole
   line: **Key Press** `t` → **Delay** `150` → **Text Input** with the text
   field left empty (the clicked line fills it), *Paste from clipboard*, *Press
   Enter after*.

In the overlay, the quick-text panel is searchable, so even a long list stays
usable mid-scene. Need a one-off variation? Edit the line inline in the overlay
— it won't change the saved version.

## 4. A dispatch form for repeated reports

For structured text — dispatch entries, reports, invoices — use a **form**:

1. In the forms editor, create a form (`Dispatch report`).
2. Add fields: a **Choice** field `Call type` (traffic stop, robbery, medical,
   backup), and **Text** fields `Location`, `Units`, and a multiline `Notes`.
   Mark `Call type` and `Location` as required.
3. Add a **Counter** named `case` starting at `1001` for the case number.
4. Set the output template:

   ```text
   [MDT #{case}] {type} — {location}
   Units: {units}
   Notes: {notes}
   ```

5. Enable the Copy button so you can paste the entry wherever it needs to go.

Turn on *Remember values* for `Location` and `Units` and the form will suggest
what you typed before.

## 5. Scene notes

Pin a sticky note (radio channel, plate, suspect or patient details) on a note
page. Notes float over the game independently of the button pages, so they stay
put while you flip between decks.

## Adapt it to your server

- **Emotes do nothing?** Open the button and change `/e <name>` to the command
  your server actually uses, or the chat key from `t` to your server's key.
- **Pasting is blocked?** Switch that button's Text Input step to *Simulate
  typing*.
- **Different roles.** Duplicate the profile per character or job (police, EMS,
  civilian) from **Settings**, and export/import to share a deck with friends.

Want music on the same overlay without alt-tabbing? If you also run
[MicMixer](https://benjibutten.github.io/MicMixer/), the overlay's music widget
remote-controls its player — see the MicMixer guides for routing music into the
game.
