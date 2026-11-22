# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Windows side-by-side diff viewer (KDiff3 style) on top of difftastic (`difft.exe`) and Avalonia 12. .NET 11, solution file `DiffTastic.Gui.slnx`.

## Commands

```powershell
./tools/get-difft.ps1                     # download pinned difft.exe into tools/difft (copied to Gui output if present)
dotnet build
dotnet test
dotnet test tests/DiffTastic.Core.Tests --filter "FullyQualifiedName~LineAlignerTests"
dotnet run --project src/DiffTastic.Gui -- samples/old.cs samples/new.cs [--path <repo-relative path>]
./tools/screenshot.ps1 -ProcessId <pid> -OutFile x.png   # capture the real running window
```

- `DiffTastic.Core.Tests` uses xunit v2; `DiffTastic.Gui.Tests` uses xunit v3 (`OutputType Exe`) with Avalonia headless + Skia.
- Gui tests save PNGs to `%TEMP%/difftastic-screenshots` (override with `DIFFTASTIC_SCREENSHOTS`). Look at them to verify visual changes.
- `GitTests` create a temporary git repo, so `git` must be on `PATH`.

## Architecture

**DiffTastic.Core** (no UI dependencies)
- `Difft/DifftRunner` runs `difft --display json` with `DFT_UNSTABLE=yes`; looks for `difft.exe` next to the app, then on `PATH`.
- `Diff/LineAligner` turns difft's `aligned_lines` + per-line changes into `AlignedRow`s (left/right `DiffLine` or null = filler row).
- `Diff/DiffService` is the entry point. When difft is missing, fails or exceeds 15 s it falls back to a DiffPlex line diff and sets `DiffDocument.EngineNote` (shown in the status bar).
- `DiffDocument` holds rows plus `Blocks` (runs of non-equal rows) used for next/previous navigation and the overview bar.
- `Git/`: `GitRunner` shells out to `git`; `RevisionLocator` finds which version a (temp) difftool file is: working tree, `HEAD`, a commit holding the same blob (`git log --find-object`), or else raw contents. `BlameService` does blame and line history (`git log -L`).

**DiffTastic.Gui** (Avalonia, CommunityToolkit.Mvvm, compiled bindings by default)
- `StartupArgs`: `<left> <right> [--path <repo path>]`, matching the git difftool setup in README.
- `MainWindowViewModel` loads the diff and starts blame for both sides in the background.
- `MainWindow.axaml.cs` owns two `DiffPane`s and keeps their scroll offsets in sync; scrolling is done via the inner `ScrollViewer`.
- `DiffPane` wraps an AvaloniaEdit `TextEditor`. Each pane's text is one line per aligned row (filler rows are empty lines), so row index == document line index in both panes. Highlighting is drawn by `DiffBackgroundRenderer`; real line numbers by `RowNumberMargin`; syntax by TextMate.
- `BlameHoverController` + `BlamePopup` show the commit for the hovered line, with Older/Newer history navigation.
