# Package Tools

A package editor's page calls the application's tools through the `cel` object its client builds from the
list of tools the host offers it, and a python console calls the same tools through its own `cel`. Both go
through one connection the host holds to its own tool server, and that server restarts on every project
load. The cases here are about tool calls that keep working across that restart. Read the
[README](README.md) for the invariants, evidence rules and levels.

## Surfaces

The `cel` object inside a package editor's page, which lists the tools the page was offered and makes calls
through them, and the `cel` object in a python console.

## Cases

| Situation | Action | Expected | Level |
|---|---|---|---|
| A note open in a new project | list the namespaces on the page's `cel`, then call `cel.app.log` with a message of your own | the namespaces include `app`, `document` and `file`, and the call resolves with the message appearing in the app log | 1 |
| A note open, having made one tool call | reload the project, then call a tool again from the reopened note | the call resolves and its message appears in the app log | 2 |
| A project just reloaded | open a note that was not open before the reload, then list its namespaces and call a tool | the namespaces are all present and the call resolves | 2 |
| A python console open, having made one tool call | reload the project, then call a tool from the console once its prompt is back | the call returns rather than raising | 2 |
| A note and a markdown document open | reload the project, then call a tool from both pages at once | both calls resolve | 3 |

Make each call from inside the page with `webview_eval`. It returns what the expression evaluates to
without waiting for a promise, so start the call with its outcome stored on `window`, then read that back
with a second evaluation. A call that fails rejects with a `CelToolError` whose message gives the host's
reason. Record that message in a failing case, since it separates a lost connection from a withheld tool.

An editor that could not load its tool list writes a line to the app log saying so, naming the page, and
every call it makes then rejects with the same reason. Search the log for it whenever a namespace comes back
missing, because the page looks healthy until it makes a call.

A reload reopens the python console with the project's other documents, so wait for its prompt before
calling.

## Not covered

A connection lost to a long idle period. The host's tool server keeps a connection for as long as the
project stays loaded, and recovering one it has dropped is covered by unit tests, since a run cannot wait
long enough to see it happen. What the tools themselves do, and the python console beyond making a call,
which the Python Environment plan covers. Agents connected to the application, which hold connections of
their own.

## Platform

The page's client and the host's side of the connection are shared by both heads, but a page reaches the
host over a different channel on each. A case that fails on one head only points at that channel.
