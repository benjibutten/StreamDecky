# Runtime Target Assessment

This document summarizes why the project currently stays on `net10.0-windows` instead of moving to another target.

## Current state

- The app is a Windows-specific WPF client and currently targets `net10.0-windows`.
- According to Microsoft's support guidance, .NET 10 is an LTS release supported until November 2028.
- .NET 8 is also an LTS release, but only until November 2026.

## Recommendation

Keep `net10.0-windows` unless a separate distribution or compatibility requirement makes another target necessary.

The reasoning is straightforward:

- StreamDecky is a distributed desktop client, and LTS is the right default track when stability and a longer support window matter more than frequent runtime jumps.
- The project is already on an LTS target. Moving away from `net10.0-windows` would not be an upgrade to LTS; it would be a downgrade to an older LTS line.
- The longer .NET 10 support window reduces pressure to plan another framework migration in the near term.
- Self-contained publishing also reduces the end user's dependence on a separately installed runtime.

## When a downgrade to `net8.0-windows` could still be reasonable

Consider it only if at least one of the following is true:

- The build environment or release pipeline must run on a toolchain locked to .NET 8.
- Distribution has to align with other internal software that is not yet validated for .NET 10.
- A concrete support requirement exists in target environments where .NET 10 SDK tooling cannot be adopted.

If none of those conditions apply, there is no clear technical advantage in leaving .NET 10 right now.

## Practical release guidance

- Keep `net10.0-windows` in the codebase.
- Stay current with the latest .NET 10 servicing update.
- Treat any target-framework change as a separate release decision with its own validation for publish, signing, tray behavior, overlay behavior, input simulation, and startup behavior.

## Source basis

This assessment is based on Microsoft's current .NET release-track and support-window guidance, where .NET 10 is documented as LTS through November 2028 and desktop client distribution scenarios are aligned with the LTS track when long-term stability is the priority.