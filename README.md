# TradeGlass

A discipline overlay for futures trading. Outside your allowed entry windows,
while the market is open and a trading platform is on screen, semi-transparent
click-blocking glass covers your configured DOM regions. Existing positions
are never touched because the glass only stands between you and new clicks.
Overriding requires typing a full sentence and every override is logged.

Windows agreed as of July 2026:
- 9:45 to 11:35 AM ET, Monday through Friday
- 7:45 to 9:15 PM ET, Sunday through Thursday
Glass only appears during GUARD periods (config-editable): weekdays 9:30 to
9:45 AM and 11:35 AM to 4:45 PM, Sun through Thu 7:30 to 7:45 PM and 9:15 PM
to midnight. Outside guard periods, including pre-market logins before 9:30,
the glass never shows. Within a guard period it still requires: market open,
platform window (Tradovate / SuperDOM) visible, not overridden, not paused.
The override runs a 30 second impulse-check countdown before the typed
sentence is even accepted (OverrideDelaySeconds in config).
A soft chime plus toast fires when a window opens, and a warning toast fires
5 minutes before it closes (ChimeOnOpen / CloseWarningMinutes in config).
Weekends and platform-closed time: invisible, zero footprint.

## Build (one time)

1. Install the .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
2. In PowerShell, from this folder:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
```

3. The exe lands in `bin\Release\net8.0-windows\win-x64\publish\TradeGlass.exe`.
   Move it anywhere permanent, e.g. `C:\Tools\TradeGlass\`.

## Run at startup (the whole point)

Press Win+R, type `shell:startup`, Enter. Put a shortcut to TradeGlass.exe in
that folder. It now launches on every boot and lives in the system tray.

## First run

1. Right click the tray shield icon, then Configure regions.
2. The screen dims. Drag one rectangle over each DOM (or one wide rectangle
   over all three). Backspace removes the last rectangle. Enter saves.
   The footprint chart area stays uncovered, so it stays clickable.
3. Done. The app decides everything else on its own from here.

## Config

`%APPDATA%\TradeGlass\config.json`. Windows, regions, platform title keywords,
override sentence, and unlock minutes all live there. Edit and restart the
app to apply. Adding or retiring a session (e.g. dropping the Asian window)
is editing one JSON entry, not rebuilding.

Violation log: `%APPDATA%\TradeGlass\violations.jsonl`, one JSON object per
line. Overrides, pauses, exits, and config changes all leave a row. This is
the file a future MetriNote sync reads from.

## Honest limitations

- The overlay windows may appear in Alt+Tab. Cosmetic only, and fixable
  later with a tool-window style flag if it bothers you.
- Friction, not force. Task Manager kills it. The bet is that impulse loses
  to a typed sentence, not that you are imprisoned.
- If you rearrange the DOM windows on the monitor, re-drag the regions
  (30 seconds via the tray menu). Static regions do not follow moved windows.
- Mixed-DPI multi-monitor setups: saved region pixels are captured from the
  raw cursor and are correct, but the preview rectangles during dragging may
  render slightly offset. Trust the save, not the preview.
- Mouse only. You confirmed you do not use order hotkeys; if that ever
  changes, tell me and we add focus handling, because keystrokes can pass
  to a focused platform window behind the glass.
- The market-hours logic covers CME equity index and energy hours including
  the daily 5 to 6 PM ET break. Exchange holidays are NOT modeled; on a
  holiday-shortened session the glass may show while the market is shut,
  which is harmless.
- UNTESTED as delivered: written without a Windows machine or compiler
  available. Expect possibly one or two compile errors on first build.
  Paste any error output back and fixes will be immediate.