# TradeGlass

A discipline overlay for traders. You set the time windows where you're
allowed to enter trades. Outside those windows, while your market is open
and your platform is on screen, a semi-transparent click-blocking glass
covers your order entry areas. You can still see prices. You just can't
click.

TradeGlass never touches your positions, orders, or broker account. It's
not connected to anything. It blocks pixels, which is exactly why it works
on platforms and prop accounts where automation and API access aren't
allowed.

You can override the glass, on purpose: it costs a 30 second countdown plus
typing out a full sentence, and every override gets logged with a
timestamp. It's friction, not a cage. The idea is that most impulse trades
don't survive a 30 second wait.

## What it looks like

The glass over a trading platform, override in progress:

![The glass blocking a platform outside the trading window](screenshots/glass.png)

First launch:

![First-run welcome](screenshots/welcome.png)

Customizable settings:

![Settings window](screenshots/settings.png)

## What it does

- Enforces recurring trading windows you set (like 9:45 to 11:35 AM on
  weekdays), in your own timezone
- Turns on a few minutes before each window opens (the classic rush-in-early
  danger zone) and stays active after each close
- Detects trading platforms by window title (Tradovate, NinjaTrader,
  TopstepX, TradingView, and more, or add your own), desktop apps and
  browser tabs alike. Works with any platform whose window you can name,
  which is set in the keywords list in Settings
- Lets you briefly lift the glass to manage a position you already have
  open (adjust stops, cancel orders) without the override friction, on a
  short timer, always logged
- Disappears completely when your platform is closed or your market is
  shut. Weekends: invisible
- Chimes when your window opens, warns you a few minutes before it closes
- Logs every override, manage, pause, and settings change to a local file

## What it doesn't do

- Touch positions or working orders. A trade opened inside your window is
  yours to manage after the window closes; the glass only blocks NEW entries
- Connect to your broker, send data anywhere, or require an account.
  Everything is local
- Enforce P&L rules. Loss limits are your broker's job and most platforms
  already offer them. TradeGlass only handles time
- Stop you if you're truly determined. Task Manager kills it in seconds.
  It's a speed bump, not a wall

## Install

### Option A: prebuilt zip (recommended)

1. Download the latest zip from this repo's Releases page, or use the one
   that was sent to you directly.
2. Install the .NET 8 Desktop Runtime if you don't have it:
   https://dotnet.microsoft.com/download/dotnet/8.0 (the "Desktop Runtime"
   installer, not the SDK). The exe won't start without it.
3. Unzip TradeGlass.exe somewhere permanent, like `C:\Tools\TradeGlass\`.
4. First launch of a downloaded exe: Windows SmartScreen may show
   "Windows protected your PC". Click "More info", then "Run anyway".
   That's the standard treatment for unsigned software; see the antivirus
   section below for why. If you'd rather not take it on faith, Option B
   exists so you can read and build it yourself.

### Option B: build from source

1. Install the .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
2. Clone or download this repo, then from the project folder:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
```

3. The exe lands in `bin\Release\net8.0-windows\win-x64\publish\TradeGlass.exe`.

### About the antivirus flag

Your antivirus will probably flag the exe. That's the normal fate of a
freshly compiled, unsigned executable that draws always-on-top windows: the
behavior looks like screen-hijacking malware to a heuristic, and it can't
tell a discipline tool from a threat. The full source is right here, and if
you built it yourself, the binary is your own compile. Add a folder
exception for the TradeGlass directory in your antivirus and move on. If
that makes you uneasy, read the source first. It's a small codebase.

### Run at startup (recommended)

A discipline tool only works if it's already running when you slip. Press
Win+R, type `shell:startup`, Enter, and drop a shortcut to the exe in that
folder. It'll start with Windows, sit in the system tray, and decide on its
own when the glass is needed.

## First run

On first launch a welcome window walks you through the two setup steps:

1. **Settings** (right click the tray icon): set your trading windows,
   timezone, market calendar, and platform keywords
2. **Draw regions**: the screen dims; drag a rectangle over each order
   entry area (your DOMs). Charts and everything else stay uncovered and
   clickable. Backspace removes the last rectangle, Enter saves

That's the whole setup. From there it runs itself.

Everything is reachable later from the system tray: right click the
TradeGlass shield icon (bottom right of your taskbar, it may be tucked
behind the little arrow) for Settings, region setup, the violation log, a
one hour pause, and exit.

### Does it work with my platform?

Probably. TradeGlass doesn't hook into any specific broker or platform, it
just covers screen regions and detects platforms by window title. So it
works with Tradovate, NinjaTrader, TopstepX, TradingView, MetaTrader, and
anything else, as long as you add a word from that platform's window title
to the keywords list in Settings (some are preset, and there's a custom
field for the rest). Rule of thumb: guard where you actually place orders,
not where you only look at charts. If you execute in one app and chart in
another, only add the one you execute in.

### Managing an open trade after your window closes

If you're already in a position when your window ends, you still need to
manage it: adjust a stop after a partial fill, or cancel leftover working
orders before you're done for the day. The glass covers your DOM, so it
would normally block that too.

That's what the **Manage open position** button on the glass is for. Click
it and the glass lifts for a short time (60 seconds by default) so you can
manage your trade, then it comes back on its own. No countdown, no typed
sentence, because managing a trade you already have open isn't the thing
the glass is trying to stop. It's still logged, so you can see later how
often you used it.

In Settings you can turn this button off entirely, change how long the
glass lifts per click (say 30 seconds instead of 60), and optionally cap
how many times in a row you can use it. By default it's on, 60 seconds, and
uncapped.

## Configuration

Everything lives in the Settings window. If you prefer editing by hand, the
same values are in `%APPDATA%\TradeGlass\config.json`:

- Trading windows: days plus start and end times, in your timezone
- Guard behavior: auto-derived by default (arms N minutes before each
  open); set `AutoDeriveGuards` to false to hand-write `GuardWindows` in
  JSON
- Market calendar: `futures` (CME Globex), `us_equities` (9:30 to 4 ET), or
  `always_open` (crypto). Exchange hours run on the exchange's clock, your
  windows run on yours
- Override sentence, countdown seconds, unlock minutes
- `CustomGlassMessage`: replace the default lock text with your own words
  or your own stats
- `FooterQuotes`: one line shown on the glass, rotating daily

If the config file ever gets corrupted, it's backed up (not overwritten)
and the app tells you.

The violation log is `%APPDATA%\TradeGlass\violations.jsonl`, one JSON
object per line: overrides, pauses, exits, settings changes. Worth
reviewing every so often, since that log is the whole point of the tool.

## Honest limitations

- It's friction, not force. If you really want around it, Task Manager
  kills it in seconds
- Mouse clicks only. If you enter orders with keyboard hotkeys, keystrokes
  can still reach a focused platform window behind the glass
- Regions are static rectangles. If you rearrange your platform windows,
  re-drag the regions (about 30 seconds via the tray menu)
- Window title detection can't tell your platform from a browser tab with a
  similar title; tune the keywords in Settings if false positives get
  annoying
- Exchange holidays aren't modeled; on a holiday the glass may patrol a
  shut market, which is harmless
- Mixed-DPI multi-monitor: saved region pixels are correct, but the preview
  rectangles while dragging may look slightly offset. The saved regions are
  what count
- Windows only

## Privacy

No network calls. No telemetry. No account. Your config and your violation
log are files on your machine and they never leave it.

## License

See LICENSE.