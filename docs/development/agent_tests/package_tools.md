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
| A markdown document open in a new project | list the namespaces on the page's `cel`, then call `cel.app.log` with a message of your own | the namespaces include `app`, `document` and `file`, and the call resolves with the message appearing in the app log | 1 |
| A markdown document open, having made one tool call | reload the project, then call a tool again from the reopened document | the call resolves and its message appears in the app log | 2 |
| A project just reloaded | open a Python script that was not open before the reload, then list its namespaces and call a tool | the namespaces are all present and the call resolves | 2 |
| A python console open, having made one tool call | reload the project, then call a tool from the console once its prompt is back | the call returns rather than raising | 2 |
| A markdown document and a Python script open | reload the project | every reopened page lists all its namespaces, and a call from each resolves | 3 |

Make the project from the Python Project template, which supplies a python console, a markdown document and
a Python script, so nothing needs making with New File. Open the markdown document for the first case, and
leave the script closed until after the first reload, which the third case needs. Make the console's first
call before the first reload, so that the level 2 cases share one reload.

The last case is about the moment a reload reopens several pages, each fetching its tool list at the same
time. Calls made afterwards through `webview_eval` run one at a time, so they cannot stand in for that.

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

A connection lost while the project stays loaded, whether to a long idle period or to a server restart the
host did not notice. A run cannot arrange either, so recovering a lost connection is covered by unit tests.
A reload normally drops the connection cleanly, and a run passes the same way whichever path the host takes.
What the tools themselves do, and the python console beyond making a call, which the Python Environment plan
covers. Agents connected to the application, which hold connections of their own.

## Platform

The page's client and the host's side of the connection are shared by both heads, but a page reaches the
host over a different channel on each. A case that fails on one head only points at that channel.
