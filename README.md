# DiffTastic

Windows diff viewer in KDiff3 style, built on [difftastic](https://difftastic.wilfred.me.uk/) and Avalonia.

- Side-by-side diff with aligned rows, filler rows and per-character changes from difftastic.
- Overview bar, previous/next difference (Ctrl+Up / Ctrl+Down, Ctrl+Home / Ctrl+End).
- Syntax highlighting (TextMate).
- Hover a line to see the commit that last changed it. Use **Older / Newer** to walk through the line's history (`git log -L`).
- Falls back to a plain line diff when difft is missing, fails or takes longer than 15 s.

## Download

Every push to `master` creates a [release](https://github.com/rgroenewoudt/DiffTastic.Gui/releases) with self-contained builds for Windows (`win-x64`) and Linux (`linux-x64`). difft is included; no .NET install needed.

## Build

```powershell
./tools/get-difft.ps1          # downloads difft.exe (pinned version) into tools/difft
dotnet build
dotnet test
```

`difft.exe` is copied next to `DiffTastic.Gui.exe`. Without it, the app looks on `PATH`.

## Usage

```
DiffTastic.Gui.exe <left> <right> [--path <repo-relative path>]
```

### As git difftool

```ini
[difftool "difftastic-gui"]
    cmd = "C:/path/to/DiffTastic.Gui.exe" "$LOCAL" "$REMOTE" --path "$MERGED"
[diff]
    tool = difftastic-gui
```

Then `git difftool HEAD~3 -- some/file.cs`.

For blame, the app finds which commit each side belongs to:
the working tree file, `HEAD`, a commit that holds the same blob (`git log --find-object`), or else the content is blamed on top of `HEAD`.

### AI (optional)

When [Claude Code](https://claude.com/claude-code) (`claude`) or [Codex](https://github.com/openai/codex) (`codex`) is on `PATH`, the toolbar shows an **AI** toggle. It is off by default. Turned on, every diff is sent to that tool, which uses its cheapest model and your existing login, and you get:

- a summary of the change,
- per difference: importance, a short reason and review hints (orange strip in the overview bar, orange dot in the gutter),
- **Skip noise**: previous/next skip differences that only change formatting, names, comments or imports,
- **Explain (AI)** in the blame popup: what the commit did to that line.

The changed code is sent to Anthropic or OpenAI, so only turn it on when that is allowed for the code. Settings are in `%APPDATA%/DiffTastic/settings.json`:

```json
{
  "AiEnabled": false,
  "AiProvider": "codex",
  "ClaudeModel": "haiku",
  "CodexModel": "gpt-6-luna"
}
```

`AiProvider` is `claude` or `codex`; empty picks the first one found.

## Development

- `tests/DiffTastic.Gui.Tests` renders the window headless and saves PNGs to `%TEMP%/difftastic-screenshots` (override with `DIFFTASTIC_SCREENSHOTS`).
- `tools/screenshot.ps1 -ProcessId <pid> -OutFile x.png` captures the real running window.

## License

MIT. difftastic (downloaded separately) is also MIT licensed.
