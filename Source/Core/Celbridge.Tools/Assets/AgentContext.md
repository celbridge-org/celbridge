# Celbridge Agent Context

This document provides essential context for AI agents working with
Celbridge projects through the MCP tool interface.

## Getting Started

Before calling any workspace tools, call `app_get_status` to check
whether a project is loaded. Most tools require a loaded project and will
fail if no project is open. The response includes the project name.

## Resource Keys

All file and folder references in Celbridge use **resource keys** -- paths
relative to the `Project` folder, using forward slash separators on all
platforms. The `Project/` prefix itself is **not** part of the resource key.

- `readme.md` -- a file in the project root
- `Scripts/hello.py` -- a nested file
- `Data` -- a subfolder
- (empty string) -- the project root folder itself

Resource keys never use backslashes, absolute paths, or leading slashes.
When in doubt, call `file_get_tree` with an empty resource key to see the
actual resource keys in the project.

## Project Structure

A Celbridge project is a folder containing a `.celbridge` configuration file
and a `Project` folder that holds all user content. Resource keys are relative
to the `Project` folder.

```
MyProject/
  MyProject.celbridge    # Project configuration
  Project/               # Resource key root -- all keys are relative to here
    readme.md            # Resource key: readme.md
    Scripts/
      hello.py           # Resource key: Scripts/hello.py
    Data/
      report.xlsx        # Resource key: Data/report.xlsx
```

## Tool Namespaces

Tools are organized into namespaces that match the workspace UI panels:

- `app` -- Application info, logging, and alerts
- `file` -- Read-only file and folder queries
- `query` -- Agent context and knowledge retrieval
- `explorer` -- Explorer panel: structural operations and navigation
- `document` -- Documents panel: content editing and editor management

## Extensions

Celbridge supports extensions as collections of HTML, JavaScript, and CSS
files that run inside a WebView2 control. Extensions communicate with the
host application through a JSON-RPC message channel.

## Documents

Documents are files opened in the editor area. The editor type is determined
by the file extension. Multiple documents can be open simultaneously across
up to three editor sections.

## Explorer Panel

The explorer panel shows the project's file tree. Use `explorer_select` to
highlight a resource in the tree.

## Commands

All tools that modify application state enqueue commands on a sequential
command queue. Commands execute in order on the UI thread. Tools return
immediately after enqueuing -- they do not wait for the command to complete
unless the tool method is explicitly async.

## Python Scripting

When writing Python scripts for Celbridge, import the modules you need from
the `celbridge` package. Each module corresponds to a tool namespace.

```python
from celbridge import app, document

document.open("readme.md")
app.log("Processing complete")
```

Available modules:

```
celbridge.app         - Application info and logging
celbridge.file        - Read-only file and folder queries
celbridge.query       - Agent context and knowledge retrieval
celbridge.explorer    - Explorer panel: structural operations and navigation
celbridge.document    - Documents panel: content editing and editor management
```
