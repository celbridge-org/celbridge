# Running on macOS

The plans run on the macOS head, the Debug `net10.0-desktop` build run from its development bundle. This
guide records what runs on that head have learned so far. It has no scripts yet, and building, launching and
driving the app still follow [Building and Testing](../building.md) and the project's own MCP tools. Read the
[README](README.md) first for what a run owes the machine and the traps every head shares.

## Before a run

The app opens the project it last had open. A project made with an older version stops at its "Project
upgrade required" prompt, and answering it changes that project, so decline it rather than upgrade a
project the run does not own. Clearing `Project.PreviousProject` in the settings, with the user's agreement
and while the app is closed, makes the next launch open Home instead, and the run puts it back afterwards.

Reading the clipboard from the shell can be blocked. The clipboard then cannot be saved before the run
types anything, so say in the report that the run overwrote it.

## Driving the app

- `document_activate` takes the document by `fileResource`.
- A click on a tab switches to it. Two clicks on a tab close together make a double-click, which toggles
  Focus, so leave a pause before clicking a tab again.
- An `app_*` tool call running in the background holds a lock, and input sent to the display waits for it,
  so let such a call return before pressing keys or clicking.
- The Dock's menu cannot be driven, since the process that draws it cannot be granted. For the plans' Dock
  quit route, send the quit Apple event it sends instead:
  `osascript -e 'tell application "<path to the development bundle>" to quit'`.

## What to capture

If the Explorer tree draws nothing, capture the screen and note the time before trying anything else, then
save the log. It has happened once after a project reload with nothing in the log, and the toolbar's
Collapse All drew it again.
