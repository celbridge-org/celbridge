# Celbridge Agent Context

This document provides essential context for AI agents working with
Celbridge projects through the MCP tool interface.

## Resource Keys

All file and folder references in Celbridge use **resource keys** — relative
paths from the project root using forward slash separators on all platforms.

- `Project/readme.md` — a file in the Project folder
- `Project/Scripts/hello.py` — a nested file
- `Project` — the project root folder

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

## Extensions

Celbridge supports extensions as collections of HTML, JavaScript, and CSS
files that run inside a WebView2 control. Extensions communicate with the
host application through a JSON-RPC message channel.

## Documents

Documents are files opened in the editor area. The editor type is determined
by the file extension. Multiple documents can be open simultaneously across
up to three editor sections.

## Console

The console panel displays log messages and hosts an interactive Python
terminal. Use the `log_info`, `log_warning`, and `log_error` tools to write
messages to the console log.

## Explorer Panel

The explorer panel shows the project's file tree. Use `explorer_select` to
highlight a resource in the tree.

## Commands

All tools that modify application state enqueue commands on a sequential
command queue. Commands execute in order on the UI thread. Tools return
immediately after enqueuing — they do not wait for the command to complete
unless the tool method is explicitly async.
