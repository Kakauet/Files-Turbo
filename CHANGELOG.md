# Files Turbo changelog

## 4.3.0.12

- Fixed upside-down folder previews and other shell thumbnails, preserving transparency.
- Fixed sorting after a file or folder's modification date, creation date or size changes. Items now move to their
  correct position even when they stay in the same date group. Single and multiple selections are preserved.
- Confirmed the 4.3.0.10 fix for file operations hanging after long sessions during daily use. Removed the
  unresolved-issue notice from the README.

Validated with a Release x64 ReadyToRun build, 124 passing Turbo unit tests, native bitmap orientation and alpha
checks, and manual app checks for previews, live file and folder sorting, date grouping and selection.

The README also explains how grouping by creation date can place recently modified folders in older groups.
