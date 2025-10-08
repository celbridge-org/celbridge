# Celbridge Host

Python host process for Celbridge integration.

## Purpose

This package is launched by the .NET Uno Platform application and provides:
- REPL environment for interactive Python
- JSON-RPC server for .NET to Python communication
- MCP (Model Context Protocol) interface
- Programmatic access to Celbridge CLI commands

### How It Works

- Method names are converted from Python style (`snake_case`) to CLI style (`kebab-case`)
- Positional arguments become command arguments
- Keyword arguments become CLI options (`--key value`)
- Boolean kwargs become flags (`--watch` if `watch=True`)
- All commands support `format="json"` or `format="text"`

**Note**: This package requires the `celbridge` CLI to be installed and available on your PATH.
