# Recovery, User Flows, and Troubleshooting

This document collects the most common operational scenarios around profiles, autosave, the overlay, startup registration, and local development.

## Important paths

- Primary profile store: `%LOCALAPPDATA%\StreamDecky\profiles.json`
- Backup: `%LOCALAPPDATA%\StreamDecky\profiles.backup.json`
- Legacy profile: `%LOCALAPPDATA%\StreamDecky\profile.json`
- Log file: `%LOCALAPPDATA%\StreamDecky\logs\streamdecky.log`

## User flow: create a new profile

1. Select the current profile from the profile picker in the main window.
2. Add a new profile and give it a clear name.
3. Adjust grid size, overlay behavior, hotkey, and gamepad settings in Settings.
4. Add pages or virtual layouts depending on whether you need pager navigation or direct jumps.
5. Fill in buttons, sticky notes, and quick text for the active profile.
6. Confirm that the save indicator moves from unsaved changes to saved.

## User flow: export and import a profile

1. Open Settings for the profile you want to export.
2. Export the profile to a JSON file.
3. On the target machine or in another installation, open Settings and choose Import.
4. The imported profile is added as a separate profile in the store file.
5. Switch to the imported profile and verify layouts, sticky notes, and quick text before deleting the original.

## Recovery: save failure or a stuck unsaved state

Symptoms:

- The save indicator reports that changes could not be saved.
- The save indicator does not return to saved after editing stops.

Actions:

1. Read the error text shown in the save indicator tooltip in the main window.
2. Check `%LOCALAPPDATA%\StreamDecky\logs\streamdecky.log`.
3. Confirm that `%LOCALAPPDATA%\StreamDecky` is writable and that the file is not locked by another process or backup sync tool.
4. Restart the app and confirm whether the last successful save is still present.
5. If the latest change is missing, restore manually from `profiles.backup.json`.

## Recovery: data from a newer app version is loaded

Symptoms:

- The app opens profile data, but saving fails with a schema-version error.
- You downgraded the app or copied a profile from a newer build.

Actions:

1. Do not keep editing and retrying saves in the older app.
2. Reopen the data in a StreamDecky version that supports the newer schema version.
3. Export critical profiles from the newer build if you need a manual checkpoint.
4. If you must keep using the older build, restore an older compatible `profiles.json` or `profiles.backup.json` first.

This behavior is intentional. StreamDecky blocks saving newer-schema data in older builds to avoid overwriting fields it does not understand.

## Recovery: restore from backup after a corrupted profile file

Symptoms:

- The app starts with an empty default profile or falls back to legacy data.
- The JSON file cannot be read or is clearly corrupted.

Actions:

1. Exit the app fully from the tray menu to avoid additional writes.
2. Make a copy of the current `profiles.json` for investigation before changing anything.
3. Rename `profiles.backup.json` to `profiles.json`.
4. Start the app and confirm that profiles, pages, and notes look correct.
5. Export critical profiles separately once the app opens correctly again.

## Recovery: startup or tray launch does not work

Symptoms:

- `Start with Windows` is enabled but the app does not start after sign-in.
- The app appears to start but no main window is visible.

Actions:

1. Check that the registry value exists under `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`.
2. Confirm that the command points to the current executable and includes `--minimized`.
3. If the app was moved to a new folder, open it once and toggle `Start with Windows` off and on so the registry value is rewritten.
4. Check the system tray to see whether the app is already running in the background.
5. Read the log file if startup registry sync or process startup appears to have failed.

## Recovery: overlay or hotkey does not respond

Symptoms:

- The global hotkey does not open the overlay.
- The overlay opens inconsistently or loses focus unexpectedly.

Actions:

1. Open Settings and record the hotkey again to force re-registration.
2. Avoid combinations already claimed by Windows, drivers, or other overlay tools.
3. Confirm that the app is still running in the tray.
4. Try opening the overlay from the main window to separate hotkey problems from overlay problems.
5. If the problem only happens in one game or application: the app listens for the hotkey through two independent paths — a normal global hotkey registration and a raw-input fallback that keeps working in games that block global hotkeys (and the Windows key) while focused. If neither path responds inside a game, exclusive fullscreen mode or anti-cheat may still be the cause; try the game's borderless/windowed display mode.

## Recovery: import succeeds but the profile looks wrong

Symptoms:

- Pages, sticky notes, or quick text appear empty or reordered after import.

Actions:

1. Confirm that you actually switched to the imported profile in the profile picker.
2. Export the profile again immediately after import and verify that a schema version is present in the JSON.
3. Compare the imported file with the original to see whether the problem existed before or after import.
4. If the original file is in a legacy format, let StreamDecky import and migrate it, then export it again in the current format.

## Developer troubleshooting: WPF build lock or MC1000

Symptoms:

- The build fails intermittently against generated files such as `App.g.cs`.

Actions:

1. Run `dotnet build src/StreamDecky/StreamDecky.csproj -c Debug --disable-build-servers /nr:false`.
2. Then run `dotnet run --project src/StreamDecky/StreamDecky.csproj --no-build`.
3. If the problem keeps coming back, stop editor design-time hosts and confirm that antivirus is not aggressively indexing `bin` or `obj`.

## When the log is enough and when to use the backup

- Use the log first when the UI shows save failures, overlay issues, or startup registry problems but the data still exists.
- Use the backup when the profile file can no longer be read or when you need to roll back quickly to the last known-good save.
- Export a working profile after recovery if you want an additional manual checkpoint.
