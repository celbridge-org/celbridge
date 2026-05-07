---
name: command_conventions
description: How tool calls execute (sequentially, awaited to completion) and what state guarantees agents can rely on.
---

# Command conventions

All tools that modify application state execute sequentially and wait for completion before returning. State is always fully applied when the tool call returns.

This means:

- **You don't need to poll.** A `file_write` followed by `file_read` always sees the new contents. A `document_open` followed by `document_get_context` always shows the new tab.
- **You don't need to wait.** No tool returns "queued" or "in progress"; either it returned a result, or it failed.
- **Tool calls don't race against the user.** Each tool serialises through the application's command service, so concurrent agent calls produce a defined order, and no tool observes another tool's half-applied state.

For tools that drive user-facing dialogs (e.g. `package_publish` with `confirmWithUser: true`), the tool waits for the user's response before returning. See `silent_vs_interactive` for which tools do this.
