# Running on macOS

The plans run on the macOS head, the Debug `net10.0-desktop` build run from its development bundle. This
guide records what runs on that head have learned so far. It has no scripts yet, and building, launching and
driving the app still follow [Building and Testing](../building.md) and the project's own MCP tools. Read the
[README](README.md) first for what a run owes the machine and the traps every head shares.

## Before a run

Ask the user to quit any Celbridge they have open, including an installed release. Every build on the machine
shares one `settings.json`, so two running at once overwrite each other's settings.

Build the head with `dotnet build Source/Celbridge/Celbridge.Application.csproj -f net10.0-desktop`. Then run it
from a development bundle: a `Celbridge.app` folder beside the build output, holding an `Info.plist` with a
bundle identifier of its own and a `Contents/MacOS` link to the `osx-arm64` output folder. Computer use grants
an application by its bundle identifier, and the bare executable has none. An installed `Celbridge.app` under
`/Applications` answers to the name Celbridge, so grant the development bundle by its identifier, never by
name. The link means a rebuild needs no new bundle.

The app opens the project it last had open. A project made with an older version stops at its "Project
upgrade required" prompt, and answering it changes that project, so decline it rather than upgrade a
project the run does not own. Clearing `Project.PreviousProject` in the settings, with the user's agreement
and while the app is closed, makes the next launch open Home instead, and the run puts it back afterwards.

Save the clipboard before the run types anything. Computer use can read it once the grant includes the
clipboard, even where the shell cannot. If neither can, say in the report that the run overwrote it.

## Launching and relaunching

- Count the instances before a launch and after it: `pgrep -f 'osx-arm64/Celbridge'` finds one started with
  `open`, which follows the bundle's link. Launch only when the count is 0, and expect 1 afterwards.
- Every launch writes a new log, named with its time and process id, under
  `~/Library/Application Support/Celbridge/Logs`. Wait for a log newer than the last run's before reading
  the port from it. The previous log already says the workspace loaded.
- The MCP port can change from one launch to the next. A Reload Project keeps the port but ends the MCP
  session, so start a new session after either.
- File > Reload Project can be pressed through the background menu tool.

## Driving the app

- `document_activate` takes the document by `fileResource`.
- A click on a tab switches to it. Two clicks on a tab close together make a double-click, which toggles
  Focus, so leave a pause before clicking a tab again.
- The background `app_*` tools hold a lock that stops input to the display. Call `app_release` before the
  next click or key press. The menu tool takes that lock too, and while it is held a clipboard read is
  refused as well.
- A click on the window's own title bar never reaches the application, so it does not dismiss an open
  flyout, and the next click is spent dismissing it. Dismiss a flyout by clicking inside the window content.
- Opening a file with `document_open` straight after writing it to disk can fail with an "Open Document
  Failed" dialog, because the project has not picked the file up yet. The dialog holds the command queue
  until it is answered. Wait about a second after writing a file before opening it.
- The background menu tool presses the item it names whenever that item is enabled. It reads enablement
  only through its refusal of a disabled item, so name an item whose action does no harm. It refuses Cut,
  Copy and Paste outright, so reach those through the menu bar with the pointer.
- The first click into a document that has just been brought to the front can miss: the page takes the
  click, but the caret stays where it was. Read the caret back after that click, and click again if it has
  not moved.
- A document left in a background tab at launch may not answer the `webview_*` tools until it has been
  shown. Activate it first.
- The Dock's menu cannot be driven, since the process that draws it cannot be granted. For the plans' Dock
  quit route, send the quit Apple event it sends instead:
  `osascript -e 'tell application "<path to the development bundle>" to quit'`.

## What to capture

If the Explorer tree draws nothing, capture the screen and note the time before trying anything else, then
save the log. It has happened once after a project reload with nothing in the log, and the toolbar's
Collapse All drew it again.

If a document shows white, look in the log for its "native message bridge present" line before closing it.
`(blank)` with no later line naming the editor's page means its page never loaded, which is a defect to
report. Save the log, then close the document and open it again.
