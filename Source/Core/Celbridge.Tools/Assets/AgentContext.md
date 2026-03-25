# Celbridge Agent Context

This document provides essential context for AI agents working with
Celbridge projects through the MCP tool interface.

## Getting Started

Before calling any workspace tools, call `app_get_status` to check
whether a project is loaded. Most tools require a loaded project and will
fail if no project is open. The response includes the project name.

## Resource Keys

All file and folder references in Celbridge use **resource keys** -- relative
paths from the project root using forward slash separators on all platforms.

- `Project/readme.md` -- a file in the Project folder
- `Project/Scripts/hello.py` -- a nested file
- `Project` -- the project root folder

Resource keys never use backslashes, absolute paths, or leading slashes.

## Project Structure

A Celbridge project is a folder containing a `.celbridge` configuration file
and a `Project` folder that holds all user content. The `Project` folder is
the root for all resource keys.

```
MyProject/
  MyProject.celbridge    # Project configuration
  Project/               # All user content lives here
    readme.md
    Scripts/
      hello.py
    Data/
      report.xlsx
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

The Celbridge console hosts an interactive Python REPL with a pre-configured
`cel` proxy object available as a global variable. Do not import any library
or construct a client -- just use `cel` directly.

```python
cel.document.open("Project/readme.md")
cel.app.log("Processing complete")
```

Tool aliases provide dot-notation method names for the Python proxy:

```
cel.app         - Application info and logging
cel.file        - Read-only file and folder queries
cel.query       - Agent context and knowledge retrieval
cel.explorer    - Explorer panel: structural operations and navigation
cel.document    - Documents panel: content editing and editor management
```
