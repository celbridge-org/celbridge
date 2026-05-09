# Troubleshoot: feature flag disabled

The tool you called is gated by a feature flag, and the flag is currently off. The error message names the specific flag (e.g. `webview-dev-tools`).

## Recovering

Feature flags live in the user-level `.celbridge` config, not the project file. Ask the user to enable the named flag — the tool cannot toggle it for them, and the project's `.celbridge` does not override the user setting.

To find which flags are currently on, call `app_get_state` and read the `featureFlags` map. Every public flag declared in `FeatureFlagConstants` appears as a `name -> bool` entry. If the relevant flag is `false` and the user has not consented to enabling it, choose a different approach instead — there is no programmatic bypass.

## Common cases

- **`webview-dev-tools`** gates every `webview_*` tool. Without it, all webview automation is unavailable.
- **`webview-dev-tools-eval`** is a separate, narrower flag that gates only `webview_eval` because arbitrary JavaScript evaluation is the riskiest webview surface.
- **`mcp-tools`** gates the broker itself; if it is off, you would not see this error from a tool call (the MCP server would not be running).
- **`console-panel`** gates the console UI feature; tools may reference it for layout reporting.
