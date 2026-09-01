# Celbridge - Claude Code Instructions

Celbridge is a cross-platform desktop application built with Uno Platform and WinUI. The solution is at `Celbridge.slnx` in the repo root.

## Documentation

The build instructions, coding conventions and architecture rules for this codebase are not
Claude-specific, so they live in `docs/development/` where every contributor can find them. They are
documented once there and referenced here.

Read the document covering an area before working in it:

| Document | Covers |
|---|---|
| [Building and Testing](docs/development/building.md) | Building on Windows and macOS, running the .NET, JavaScript and Python test suites, linting, and CI |
| [Coding Conventions](docs/development/coding_conventions.md) | Conventions for C#, JavaScript and Python, plus the general rules that apply to all three |
| [Architecture](docs/development/architecture.md) | Service lifetimes and dependency injection rules, the `Platform/` folder convention, and the document save model |
| [Design Tokens](docs/development/design_tokens.md) | The generated colour and dimension tokens shared by the XAML and web sides |
| [MCP Tools](docs/development/mcp_tools.md) | Authoring MCP tool classes in `Celbridge.Tools` |

## Git

- Never commit automatically; the user reviews all changes in GitHub Desktop before committing
- Do not add `Co-Authored-By` lines to commit messages
