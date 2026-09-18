# Console Layout

A console document is an xterm.js terminal in a WebView, connected to a pty that the host starts as soon as the document opens, usually before the WebView has been laid out. The shell, the pty and xterm must agree on one grid of columns and rows, and that grid can only be measured from the page's own box, which the platform may not have laid out yet. This document explains how the pieces agree on a size, how the heads differ, which failures have come from getting it wrong and the rule that now prevents each one, and how to investigate a new one.

## The rule the design serves

Resizing a pty after the program inside it has drawn its screen has visible costs. ConPTY reflows and repaints its buffer, and a REPL's line editor redraws its prompt, so a late resize shows up as blank rows between the banner and the prompt, a repeated prompt, or rewrapped output. The design therefore launches the pty at the size the view will be read at. Where no such size is available, it corrects the size while the starting veil still covers the screen, before the ready marker. Two corollaries follow: a size measured from placeholder geometry must never reach the pty, and a size that has been superseded must never be applied after the one that replaced it.

The failure signature is always the same. After startup, the pty and xterm disagree about the size, or the pty was resized after the ready marker. Everything under [Investigating a layout problem](#investigating-a-layout-problem) is a way of finding which step produced it.

## Components

| Layer | Component | Role |
|---|---|---|
| Session | `ConsoleSessionService` | Owns one session per open `.console` document. Starts it on `DocumentOpenedMessage` whether or not a view exists, ends it on `DocumentClosedMessage`, and handles attach, reopen and resize requests from views. |
| Session | `ConsoleSession` | Owns the pty: waits for a view size, launches, injects the startup lines, scans for the ready marker, buffers output for replay, and applies resizes. |
| Session | `PendingViewSize` | The size a view reports, held for a launch that has not created its pty yet. Settles over 250 ms, and can be told that no size is coming. |
| Session | `ConsoleSessionChannel` | The `console/*` RPC between a page and its session. |
| Session | `ConPtyTerminal`, `UnixPtyTerminal` | The pty backends, chosen by `PtyBackendFactory`. They resize through `ResizePseudoConsole` and the `TIOCSWINSZ` ioctl, and ignore a size they already have. |
| Session | `ConsoleOutputBuffer` | Up to 256,000 characters of output, replayed to a view that attaches after it was painted. |
| WebView host | `CustomEditorController` | Owns the editor's WebView, and publishes `isSized` and `canSizeUnarranged` in the view state. |
| WebView host | `IWebViewAdapter` | `CanSizeUnarrangedViewport` and `SetViewportSize`, implemented by `WindowsWebViewAdapter` and `SkiaWebViewAdapter`. |
| WebView host | `DocumentSectionView.UpdatePresentedDocumentSizes` | Gives every document in a section the size of the section's content presenter, including documents in tabs that have never been shown. |
| WebView host | `WebViewFactory` | A pool of three WebViews with `CoreWebView2` initialized but not navigated. |
| Page | `client.view` (`celbridge-client/api/view-api.js`) | Whether the page's box can be trusted, and a wait for it to settle. |
| Page | `console.js` | Creates xterm, fits it to its box, and refits on layout changes. |
| Page | `console-session.js` | Attaches to the session, replays its output, and runs the starting veil. |
| Page | `celbridge-client/ui/stack-layout.js` | Moves the console's rail between the side and the top. |

The session classes live in `Source/Workspace/Celbridge.Console`, under `Services/`, `Helpers/` and `Platform/`, with the page under `Web/Console/`. The WebView host classes live in `Source/Workspace/Celbridge.Documents/Views` and `Source/Core/Celbridge.WebHost`.

## Geometry

- `#terminal` is absolutely positioned with an 8 px margin inside `#terminal-view`, so its box follows the content row exactly. The fit addon divides that box by xterm's cell size to get the columns and rows.
- The rail takes `--cel-rail-width` (`calc(40px / var(--cel-page-zoom))`) out of the row. Below `--cel-rail-stack-threshold` (400 px) it moves across the top instead. `attachStackLayout` resolves the arrangement in a `ResizeObserver` and the console refits when it changes.
- Settings replace the terminal rather than sitting beside it, so opening them never resizes the pty. While they are up the terminal has no box, `fitTerminal` declines to measure, and the pty keeps its size.
- On Windows, WebView2 folds the accessibility text size into its rasterization scale, so a CSS pixel can be larger than a device-independent pixel. The client derives `--cel-page-zoom` from the `rasterizationScale` in the app state, and native-mirroring dimensions such as the rail width divide by it. On one development machine it came out at 1.17: a 576x951 DIP control gave its page a 493x813 CSS px viewport, and the rail narrowed from 40 to 34.2 px when the first app state arrived, widening the terminal box by the difference.
- xterm measures its cell size when it opens, so `console.js` waits for the bundled Cascadia Mono faces to load before calling `term.open`.

## When the page may measure

`CustomEditorController` seeds the view state before the page connects, and the page's state store replays the latest snapshot to a subscriber that registers late.

- `isSized` becomes true the first time `ApplyViewportSize` has a size worth reporting: the control has been arranged (`ActualWidth` and `ActualHeight` above zero), or it has not been arranged but the adapter applied the section's presented size to the page's viewport. It runs from `Loaded`, `SizeChanged` and `SetPresentedSize`. `Unloaded`, which a tab switch or a redock raises, sets it false until the next arrange.
- `canSizeUnarranged` mirrors `IWebViewAdapter.CanSizeUnarrangedViewport`.

On the page, `client.view` turns those into answers:

- `sizeUnavailable` is `document.hidden` on a host that cannot size an unarranged surface. No size is coming until the page is shown.
- `canMeasure(element)` requires `isSized`, `sizeUnavailable` to be false, and a non-zero box. A hidden page on such a host can report `isSized` while still holding a placeholder viewport, which is why the middle condition exists.
- `waitForStableSize(element, { timeoutMs })` returns at once when `sizeUnavailable`. Otherwise it samples the box until two samples in a row agree while `canMeasure` holds, or the timeout passes. A hidden page runs no animation frames, so it is sampled on a 100 ms timer. An on-screen page is sampled on animation frames, with the same timer as a backstop.
- `onChanged(handler)` fires on every view state push, which is when these answers can change.

## Starting a console

1. The document opens and `ConsoleSessionService` starts the session. `ConsoleSession.StartAsync` reads the `.console` file, builds the startup invocation, sets the pty size to the 120x30 fallback, gates input, and waits in `PendingViewSize.WaitAsync` for a view to report a size. The wait is 30 s where the host can size unarranged views and 5 s where it cannot.
2. The editor's WebView comes from the pool and navigates. Depending on layout and timing, the page loads either before its tab has been realized, hidden and at a placeholder viewport, or straight into an arranged tab. Cold starts have been seen doing both.
3. `console.js` loads the fonts, opens xterm, and fits once if `canMeasure` already holds. That fit is never reported, because the session's `onResize` listener is registered after it. The attach carries the size instead.
4. After `initializeDocument`, `console-session.js` attaches. It runs `waitForStableSize` with a 4 s budget (`SIZE_SETTLE_TIMEOUT_MS`, deliberately inside the host's 5 s wait), then `fitTerminal` and `term.reset()`, then sends `console/attach` with the terminal's size, or 0x0 when the box could not be trusted.
5. `ConsoleSessionService.AttachAsync` then:
   - reports the size to the session before anything else, because the launch is waiting for it and awaiting the launch first would leave each waiting on the other,
   - calls `ReportNoViewSize` when the view sent no size on a host that cannot size unarranged views, which ends the launch's wait at once,
   - awaits the launch only when the view supplied a size, so an unsized attach returns immediately instead of holding the request open for the whole wait,
   - applies the latest size any view reported (`ApplyReportedViewSize`), which may be newer than the size this attach carried,
   - returns a snapshot of the run state, whether startup is still pending, the buffered output, and the size that output was painted at.
6. The launch's wait returns once reports have stopped changing for 250 ms, or with no size on timeout or when none is coming. The pty is sized and started, then given any size reported while it was being created.
7. The startup lines are written once the shell's output has been quiet for 150 ms, capped at 1.5 s. The injected line clears the screen and, except under cmd, emits the shell family's ready marker before running the command. Under zsh it also replaces the stock macOS prompt, whose user and host names take most of a narrow console's width, with a compact one; the swap is guarded on an exact match of the stock prompt, so a prompt set from an rc file is left alone. Output before the marker is discarded, so the buffer starts on a clean screen. A plain shell that launched without a view size holds its lines until the first real size, so its prompt is not drawn at the fallback width.
8. The page applies the snapshot. It adopts the session's size before writing a replay, so the replay is not rewrapped and that resize is not reported back, and it shows the starting veil while startup is pending. `console/startupComplete` hides the veil when the marker is seen, when 10 s pass without output and without a marker, or when the process exits. A 15 s backstop on the page hides it regardless.

## After startup

- `refitTerminal` runs `fitTerminal` on the next animation frame, coalesced, after a window `resize`, a `visibilitychange`, a view state push, a change in the rail's arrangement, or settings closing.
- A fit that changes the grid fires xterm's `onResize`, which sends `console/resize`. `ConsoleSession.Resize` ignores a non-positive size, which would collapse the pty to one row, skips a size the pty already has, and otherwise resizes the pty.
- A hidden page runs no frames, so its refits wait until it is shown. The pty keeps the size it was left at until then.
- A page that attaches to a session that is already running replays the buffer at the session's size before any refit.
- Reopen (`console/reopen`) shows the terminal first, saves the settings form, disposes the session and launches a new one from the file on disk. It measures without waiting, since the terminal is already on screen.

## How the heads differ

| | Windows packaged (WinUI WebView2) | Windows Skia (WebView2) | macOS Skia (WKWebView) | Linux Skia (WebKitGTK) |
|---|---|---|---|---|
| `CanSizeUnarrangedViewport` | false | false | true | false today |
| `SetViewportSize` | does nothing | does nothing | sets the native frame | does nothing today |
| Launch wait for a view size | 5 s | 5 s | 30 s | 5 s |
| A page before its tab is shown | hidden, with an 832x883 CSS px placeholder | not measured | sized to the section's presented size | unknown |
| pty backend | ConPTY | ConPTY | `UnixPtyTerminal` | `UnixPtyTerminal`, not yet ported |

### Windows packaged head

- The WebView2 viewport follows the XAML control, so the host cannot give an unarranged page a size, and a page in a tab that has not been shown reports none.
- Pooled WebViews are created outside the visual tree. A page that navigates there loads with `document.hidden` true and an 832x883 CSS px viewport.
- XAML can arrange the control, and so set `isSized`, before `Loaded` makes the WebView visible. For that window the page reports `isSized` while it is still hidden and still has the placeholder viewport. It gets its real viewport when it becomes visible.
- Inactive tabs pause the renderer, and the `Unloaded` that comes with a tab switch sets `isSized` false.
- A console whose page is hidden when it attaches reports no size and launches at the 120x30 fallback. When the page is only waiting for its tab to be attached, the real size follows a fraction of a second later, before the ready marker. A console in a background tab keeps the fallback until its tab is first shown.

### Windows Skia head

`SkiaWebViewAdapter` returns true from `CanSizeUnarrangedViewport` only on macOS and sizes viewports only there, so this head takes the same page-side path as the packaged head. Its placeholder geometry has not been measured. It is not a deployment target, but it exercises the Skia code on a Windows machine.

### macOS head

- Uno arranges a WKWebView's native frame only while the control is in the visual tree, so a page loaded outside it would see a zero-sized window. `SkiaWebViewAdapter.ApplyInitialViewportSize` gives each new WebView the window's content size, at least 1024x768.
- `SetViewportSize` sets the native frame directly. That closes the gap before Uno's own arrange pushes the frame, and it is how a page in a background tab gets its section's presented size. A hidden page can therefore have `isSized` and a real size before it has ever been displayed, and once the view state has arrived `sizeUnavailable` is never true.
- WebKit stops a hidden page's event loop after about seven minutes, which stalls host RPC to a background document. The adapter wakes each hosted page every 30 s and pins the native view with background activity preferences.
- Hidden pages run no animation frames, which is why hidden pages are sampled on a timer.

### Linux head

Nothing Linux-specific has been decided. `SkiaWebViewAdapter` currently gives Linux the Windows answers, and `PtyBackendFactory` selects `UnixPtyTerminal`, whose P/Invokes bind `/usr/lib/libSystem.dylib` with Darwin's `TIOCSWINSZ` value. See [Bringing up a new head](#bringing-up-a-new-head).

## Failures and the rules that prevent them

| Symptom | Cause | Rule, and where it lives |
|---|---|---|
| Blank rows between the banner and the prompt, a repeated prompt, or rewrapped output after startup | The pty was resized after the REPL drew its screen, or the pty and xterm ended up at different sizes | Launch at the view's real size or correct it before the ready marker, and never apply a superseded size. Every other rule in this table serves this one. |
| The terminal is sized from a placeholder on Windows | `isSized` follows XAML's arrange, which can come before the WebView is visible, while the page still has the pool's placeholder viewport | `canMeasure` refuses a hidden page while `sizeUnavailable` holds (`view-api.js`) |
| The pty jumps back to an older size just after launch | The attach re-applied the size its request carried after a newer resize had already been applied | After the launch, apply the latest reported size (`ConsoleSession.ApplyReportedViewSize`) |
| A hidden console's launch sits out the whole wait | A view that is not shown, on a host that cannot size it, has no size to report | An unsized attach calls `ReportNoViewSize`, which ends the wait at once (`ConsoleSessionService.StartSessionForViewAsync`) |
| An attach and a launch wait on each other | The launch waits for the size the attach would only report after the launch | The attach reports its size before awaiting the launch (`ConsoleSessionService.AttachAsync`) |
| An attach request times out | An unsized attach awaits a launch that is itself waiting | The attach awaits the launch only when it supplied a size |
| The pty launches at an intermediate size while layout is still settling | A view reports several sizes as it is laid out | The page waits for two equal samples (`waitForStableSize`), and the launch waits for reports to stop for 250 ms (`PendingViewSize`) |
| A size reported while the pty is being created is lost | There was no terminal to apply it to | `StartAsync` applies `PendingViewSize.Current` right after `Start` |
| The pty collapses to one row and loses its screen | A view with no box reported 0x0 as a resize | Non-positive sizes are ignored (`PendingViewSize.Report`, `ConsoleSession.Resize`) |
| The prompt is left behind after a resize that changed nothing | A backend that acts on a resize redraws even at the same size | Unchanged sizes are skipped (`ConsoleSession.SetTerminalSize`, and both backends) |
| Replayed output rewraps when a view attaches | xterm's size differed from the size the buffered output was painted at | The page adopts the session's size before writing the replay, without reporting it back (`adoptSessionSize`) |
| The banner renders twice against an empty screen | Output painted before any view attached replays the prompt's repaint over its own banner | The launch holds the pty until a view reports a size rather than starting at a guess |
| The launch starts at the fallback although the page was about to report | The page's wait outlasted the host's | The page's 4 s budget (`SIZE_SETTLE_TIMEOUT_MS`) sits inside the host's 5 s wait |
| A size wait never ends on a hidden page | A hidden page runs no animation frames | Hidden pages are sampled on a 100 ms timer, and every wait has a timeout |
| Opening settings rewraps the terminal | A settings pane beside the terminal would narrow it | Settings replace the terminal. A hidden terminal has no box, so `fitTerminal` declines and the pty keeps its size, and Reopen shows the terminal before it measures. |
| The cell size is wrong at startup | xterm measured a fallback font | `console.js` loads the Cascadia Mono faces before `term.open` |
| Shell startup noise is visible, or a stray marker appears | The shell's banner and the injected line reach the screen before the command runs | The line clears the screen and emits a marker, and output before the marker is discarded. PowerShell's marker sits in a screen cell and comes back on every ConPTY reflow, so its scan keeps running. Diagnostic OSC sequences are scanned first, because ConPTY forwards an OSC ahead of the text around it. |
| A plain shell's prompt is drawn at the fallback width, or zsh leaves a stray glyph | Startup lines were injected before the terminal had a real size | A plain shell launched without a view size holds its startup lines until the first real size (`ConsoleSession.Resize`) |
| A background document stops answering the host on macOS | WebKit stops a hidden page's event loop after several minutes | `SkiaWebViewAdapter` wakes each hosted page every 30 s |

## Known weak points

These follow from the code as it stands and have not been shown to cause a problem. Check them first when something new appears.

- A command console, such as a Python console, that sits in a background tab at startup on a Windows head launches at the 120x30 fallback and runs its command straight away, so the REPL draws its banner and prompt at 120x30. The pty is resized when the tab is first shown, after that output. Whether that shows the same artifacts has not been checked. Plain shells avoid it by holding their startup lines.
- `waitForStableSize` has a 100 ms timer backstop for an on-screen page, so the wait can complete without the page rendering a frame. Work that only runs on a rendered frame, such as `ResizeObserver` callbacks and media query change events, may not have run by then.
- The launch's 250 ms settle window is what lets a size that arrives just after the attach become the launch size. A real size that arrives later lands as a resize after launch, which is harmless before the ready marker and visible after it.
- On Windows, `isSized` follows XAML's arrange rather than the page's viewport. The page covers the hidden case, but `canMeasure` trusts the box of any visible page once `isSized` is true.

## Investigating a layout problem

### Diagnostics flag

Turn on the `webview-load-diagnostics` feature flag in the project's Features settings. `WebViewLoadDiagnostics` then logs each navigation, each attach and detach of the WebView, and a probe of the loaded page:

```
Navigation starting for project:python.console at <url> (loaded=False size=0x0 xamlRoot=False)
Content probe for project:python.console at <url>: ... hidden=True client=832x883 (loaded=False size=0x0 xamlRoot=False)
WebView attached for project:python.console (loaded=True size=968x397 xamlRoot=True) scrollY=0
```

`size` is the control's arranged size in DIPs, and `client` is the page's document element size in CSS px. `xamlRoot=False` means the WebView is not in a visual tree. The attach and detach lines are written after a script round trip, so their timestamps can trail the event. The flag shows where the page was when it loaded and what viewport it had, but not the sizes the console sent, launched at or ended up with.

### Temporary instrumentation

For those, add temporary logging, tag every line so it can be found, and restore the files from git afterwards. `LogDebug` from the host reaches the log file.

On the host, log:

- in `ConsoleSessionChannel.AttachAsync`, the size requested, and the snapshot's size, `StartupPending` and replay length,
- in `ConsoleSession.StartAsync`, how long `WaitAsync` took and what it returned, and the size the pty starts at,
- in `ConsoleSession.Resize`, the size requested, and whether the pty is running and at what size,
- in `ConsoleSession.SetTerminalSize`, every change actually applied, from and to,
- calls to `ConsoleSession.ReportNoViewSize` and `ConsoleSessionChannel.OnStartupComplete`.

For the page, add `[JsonRpcMethod("console/trace")] void OnTrace(string text)` to `IConsoleSessionRpc` and log the text in `ConsoleSessionChannel`. Have the page queue its lines until the attach starts, after `initializeDocument` has connected the channel, and send them from then on. Worth tracing:

- in `waitForStableSize`, each sample, what released it (a frame or the timer), and how the wait ended (settled, no size coming, or timed out),
- a metrics string with each event: `document.hidden`, `client.view.isSized`, the `#terminal` box, the document element's client size, `devicePixelRatio`, `--cel-page-zoom`, the rail width, xterm's cell size (`term._core._renderService.dimensions.css.cell`), `term.cols` and `term.rows`, and `fitAddon.proposeDimensions()`,
- the events themselves: after the initial fit, the first animation frame, `visibilitychange`, window `resize`, view state pushes, rail layout changes, refits that change the grid, and the attach's start, request and result,
- a dump of `term.buffer.active` a few seconds after `console/startupComplete`, listing each non-empty row with its index, plus `baseY`, `viewportY` and the cursor. The row the prompt is on shows a gap directly. Use this rather than screenshots: screen captures taken by automation mask WebView content on the packaged head.

This is how a cold start regression on the packaged Windows head read, trimmed to the lines that mattered:

```
page: attach-send isSized=true size={"cols":87,"rows":47} hidden=true sized=true box=782x867 viewport=832x883
attach request 87x47
page: visibilitychange hidden=false sized=true box=344x272 viewport=197x288
resize request 37x14, pty not started at 120x30
starting pty at 37x14
resize request 87x47, pty running at 37x14
pty size set from 37x14 to 87x47
page: buffer startup+3s term=37x14 rows 0:"Celbridge v1.0.0 (Debug) - Python v3." ... 38:">>>"
```

The page measured its placeholder while hidden, and the attach later re-applied that size over the real one, leaving a 47-row pty behind a 14-row terminal with the prompt on row 38.

### Reproducing on the packaged Windows head

Visual Studio's deploy registers a loose layout at `Source/Celbridge/bin/Debug/net10.0-windows10.0.22621/win-x64/AppX`, and there is no MSBuild deploy target. To test a change without Visual Studio, build the head for the packaged framework (passing `-p:Platform=x64` would redirect the output away from the registered layout):

```
msbuild Source/Celbridge/Celbridge.Application.csproj -t:Build -p:Configuration=Debug -p:TargetFramework=net10.0-windows10.0.22621 -verbosity:minimal -nologo
```

Copy the changed files into the layout, updating only files it already has and leaving the loose `.xaml` files out. Robocopy exit codes below 8 mean success.

```
$out = 'Source/Celbridge/bin/Debug/net10.0-windows10.0.22621/win-x64'
robocopy $out "$out/AppX" /E /XO /XL /XD "$out/AppX" /XF *.xaml
```

Launch the registered package, and read the log from its local state folder. `Get-AppxPackage org.celbridge.Celbridge` confirms the package family name and that its install location is that layout.

```
explorer.exe shell:AppsFolder\org.celbridge.Celbridge_f09fs4qsgr17a!App
```

Logs are written to `%LOCALAPPDATA%\Packages\org.celbridge.Celbridge_f09fs4qsgr17a\LocalState\Logs`, one file per process start. A file edited directly inside `AppX` is newer than the build output, so `/XO` skips it on the next copy: compare hashes when in doubt. Launching the app by any other route while it is running can start a second instance on the same project, which saves its own window layout when it closes.

### What to test

- A cold start with the console as the selected tab, and a project reload. They take different paths, because whether the page is still hidden when it attaches varies between them.
- A console in a background tab, then selecting it.
- A console in a collapsed area, then expanding it.
- Resizing the window or dragging a splitter while a console starts.
- Opening settings, resizing, and closing settings again.
- Reopen from the settings footer and from the failure overlay.
- A Python console and a plain shell console, since only plain shells hold their startup lines.
- A text size above 100% on Windows.
- The packaged Windows head and the macOS head, with the Windows Skia head as a quick check of the Skia path.

## Bringing up a new head

Answer these with the diagnostics flag and temporary instrumentation before trusting the console on a new head, such as Linux.

1. **pty.** `PtyBackendFactory` already returns `UnixPtyTerminal` on Linux, but its P/Invokes bind `/usr/lib/libSystem.dylib` and use Darwin's `TIOCSWINSZ` value, so it needs Linux bindings before a console can start. Then confirm that a resize reaches the shell, for example with `stty size` after resizing the view.
2. **`CanSizeUnarrangedViewport`.** Linux currently takes the Windows path: a page that has not been shown reports no size and its console launches at the fallback. If WebKitGTK can be given a viewport before it is arranged, implement `SetViewportSize` and return true, which moves Linux onto the macOS path of a 30 s wait and hidden pages sized from their section.
3. **A page before its tab is shown.** Record `hidden` and `client` from the content probe before and after the tab is shown, and whether `isSized` arrives while the page is still hidden with placeholder geometry. On the packaged Windows head it did, with an 832x883 placeholder.
4. **Hidden pages.** Check whether they run animation frames and timers, whether `visibilitychange` fires on tab switches, and whether the engine throttles or suspends them the way WebKit does on macOS, which needs the keepalive.
5. **Pages loaded outside the visual tree.** Check whether such a page sees a zero-sized window, as Uno's frame behaviour causes on macOS. If so, it needs an equivalent of `ApplyInitialViewportSize`.
6. **Scaling.** Compare `devicePixelRatio` with the host's rasterization scale under desktop text scaling, so `--cel-page-zoom` comes out right.
7. **Shells.** Check the reveal on the default shells: the POSIX ready marker is an invisible OSC sequence, and plain shells hold their startup lines until the terminal has a real size.
8. **Run the test matrix** under [What to test](#what-to-test).
