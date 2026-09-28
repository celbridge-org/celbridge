# Running on Windows

The plans run on the packaged Windows head, the WinAppSDK build that ships. This guide covers building it,
deploying it and driving it outside Visual Studio, which is how an agent runs it. The scripts it names are in
[scripts/windows](scripts/windows). Read the [README](README.md) first for what a run owes the machine and
the traps every head shares.

## One-time setup

Turn on Developer Mode, and install Visual Studio as [Building and Testing](../building.md) describes.

Run the packaged head once from Visual Studio, with F5 in the Debug configuration. That registers a
development package whose layout is `Source\Celbridge\bin\Debug\net10.0-windows10.0.22621\win-x64\AppX`. The
MSIX build tooling has no command-line step that registers a layout, so the scripts rely on this one and do
not register anything themselves. Deploying the Release configuration from Visual Studio moves the
registration to the Release layout, and the deploy script then stops rather than update a layout the app no
longer runs from.

Open any project in the app once, so its settings name a project to open at launch. The scripts change that
setting to open a run's own project.

A signed package of an earlier release may be installed too, under another family name and with its own data
folder. The scripts find the development registration and leave that one alone.

## Build and deploy

```bash
powershell -NoProfile -File docs/development/agent_tests/scripts/windows/Deploy-PackagedHead.ps1
```

A command-line build refreshes `win-x64` but never the registered `AppX` layout, so after a build alone the
app launches and runs the previous build with no warning. The script builds with the MSBuild that vswhere
finds, copies every packaged file the build changed into the layout, and checks that each one matches. A
change that seems to have had no effect is this before it is anything else. Before reading a result, confirm
the change reached the layout by searching the deployed DLL for a name it added.

The development registration keeps the identity version it was registered with, and the app reports that
version. After a version bump the deployed build therefore still reports the old one, and refuses a project
stamped with the new one as made by a newer version, until the packaged head is run once more from Visual
Studio.

Close Celbridge first, since a running instance holds its DLLs open. `-SkipBuild` deploys the existing build.
The script lists files in the layout that the build no longer packages. A loose `.xaml` with no `.xbf` beside
it throws when its type is activated, and `-RemoveOrphans` deletes them.

## A run

Every shell call starts a fresh PowerShell, so each one imports the module and names the run's folder:

```powershell
Import-Module <repo>\docs\development\agent_tests\scripts\windows\CelbridgeAgentTest.psm1 -Force
Use-CelbridgeRun 'C:\Temp\celbridge_agent_tests\<plan>_<date>'
```

The folder keeps the settings backup, the app's port and log, the MCP session and a timeline that
`Write-RunLog` appends to. Keep it and the run's project outside AppData, where a packaged app's writes can be
redirected.

1. Ask the user to close their own Celbridge. `Start-Celbridge` refuses while any instance is running, and a
   run never closes one it did not start.
2. `Backup-CelbridgeSettings` copies the app's `settings.json` and records its hash. A resumed run keeps the
   first backup.
3. Make the run's project. The app's own new-project flow is the plain way. A faster one extracts
   `AppX\Celbridge.Projects\Assets\Templates\<template>.zip`, replaces `<application-version>` in the project
   file with the version the app reports, which `app_get_state` gives, and renames `project.celbridge` to
   `<Name>.celbridge`. The new-project flow also merges a `.gitignore` into the project, which this way does not.
4. `Start-Celbridge <project file>` points the app at the project, launches it by its application ID and waits
   for the workspace to load. It returns the process, the server port and the log file.
5. Drive the plan's cases, recording each result and its evidence.
6. `Stop-Celbridge`, then `Restore-CelbridgeSettings`, which fails if the restored file's hash differs.
   Confirm no Celbridge process is left.

## Driving the app

**The app's MCP tools do most of the work.** They answer at `http://127.0.0.1:<port>/mcp`, and
`Invoke-CelbridgeTool` handles the session. A response opens with a session snapshot and guides, so the tool's
own answer is the last text block. `Invoke-CelbridgeEval` runs JavaScript in a document's WebView, in its
content frame by default, such as an HTML preview, or with `-Frame top` in the editor page around it. A
`document_open` leaves the document inactive unless `activate` is true. A dialog a step raises holds every
later tool until it is answered, so arm `app_answer_dialog` before the step.

**Keys.** `app_simulate_input` sends a key without modifiers through the app's own key routing, Escape
included, and needs the app to have a focused window. It refuses modifiers on Windows. For a chord, call
`Set-CelbridgeForeground` and then `Send-KeyChord` with virtual key codes, such as `0x11, 0x57` for Ctrl+W.
Bringing the window forward takes an Alt press, which moves the keyboard out of WebView2's own find bar, so
for a chord there click the find bar first, which leaves the window in front and the helper with nothing to
do. A key typed in a web page never reaches the app's own key handling on this head, so a keydown logger
added to the page with `Invoke-CelbridgeEval` is the proof a chord arrived.

**Computer use**, for real pointer input and anything read off the screen:

- Launch the app before asking for access. Grant `celbridge.exe` by its full path in the layout, which
  resolves only while it runs. The name Celbridge resolves the package instead, and actions are then refused
  as not allowed. Grant `msedgewebview2.exe` too, by that name. Without it every page is hidden and takes no
  clicks. It resolves only while a page is on screen, so if it fails, open a document and ask again. The app
  can come up minimized, which hides every page too, and `Set-CelbridgeWindowBounds` restores it.
- While computer use runs, the Claude app shrinks to a window that floats on top at the right of the screen.
  Size Celbridge clear of it with `Set-CelbridgeWindowBounds`, or clicks land on the Claude app.
- The Claude app takes the foreground back between actions, so start every batch with a click on Celbridge,
  never with a key.
- Never use `open_application` to reach Celbridge. It starts a second instance. `Set-CelbridgeForeground`
  brings the running one forward. If another window keeps the foreground, ask the user to click Celbridge
  once.
- Never send Escape at the system level, whether from computer use, `SendInput` or `keybd_event`. Computer use
  takes it as its stop key, and later actions come back refused. `Send-KeyChord` refuses it. Use
  `app_simulate_input`.
- Typing a long string pastes it through the clipboard, which overwrites the user's clipboard and tests paste
  rather than keystrokes. A terminal turns typed characters into the wrong ones, so send terminal input as key
  presses.

**UI Automation** reads the app's own controls. `Find-CelbridgeElement` walks the raw tree, since a search
with `FindAll` or `FindFirst` on a busy app can miss whole branches without an error.
`Get-CelbridgeWebViewBounds` gives the WebView on screen. A point in its page is that rectangle's corner plus
the point's CSS position times the page's `devicePixelRatio`, which `Invoke-CelbridgeEval` reads.

## Evidence

Read state back rather than off the screen where the tools allow: page state through `Invoke-CelbridgeEval`,
documents through `document_get_state`, the focused panel through `app_get_state`, and files on disk. The
app's log is in its data folder under `Logs`, and `Get-CelbridgeLogLines -FromLine <n>` reads what came after a
point in the run.

Judge colors from the screen, with `Save-ScreenRegion` or `Get-ScreenPixel`. A PrintWindow capture draws web
page content on white whatever the screen shows. A flash lasts a frame or two, so sample a pixel in a loop
while the step that might cause one runs.
