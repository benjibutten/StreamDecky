# Remaining Improvements

This document lists the items that still remain open after the current pass and notes which earlier risk areas are now addressed.

## Still open

- Any target-framework change should still be treated as a separate release decision, with the current documented recommendation to stay on `net10.0-windows` until a concrete compatibility requirement appears.
- UI automation or broader end-to-end manual testing around fullscreen applications, focus restoration, multi-monitor DPI behavior, and anti-cheat environments is still needed before hardening the app for a wider range of real-world target environments.
- Release packaging is still zip-based. An installer or managed update story would further reduce update friction for non-technical users.
- Release code signing is still optional in CI. A fully production-gated release flow should eventually make signing mandatory when official public releases are cut.

## Addressed in this pass

- Best-effort diagnostics to a local log file.
- Logging for previously silent failures in profile handling, autosave, and background actions.
- Safer atomic file writes for profile and backup files.
- Protected registry sync for `Start with Windows`.
- Explicit schema versioning and versioned profile/store migration.
- Save protection for profile data created by newer schema versions, to avoid silent downgrade writes.
- User-visible save status for unsaved changes, in-progress saves, and save failures.
- Splitting `MainViewModel` into smaller partials for profiles, layouts, sticky notes, quick text, and save lifecycle.
- Automated tests for overlay lifecycle, hotkey registration, startup registry sync, import/export roundtrip, and action execution.
- Test execution in both pull-request and release workflows.
- English user-facing copy in the app, clearer destructive confirmations, and English end-user documentation.
- Project-level high-DPI configuration for the hybrid WPF and WinForms app setup.