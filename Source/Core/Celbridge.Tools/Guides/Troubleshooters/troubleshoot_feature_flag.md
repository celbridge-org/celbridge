# Troubleshoot: feature flag disabled

The tool you called is gated by a feature flag, and the flag is currently off. The error message names the specific flag (e.g. `webview-dev-tools`).

## Recovering

Each flag has an application default, and a project can override it in the Feature Flags section of Project Settings. Ask the user to switch the named flag on there and reload the project. The tool cannot change a flag itself.

To find which flags are currently on, call `app_get_state` and read the `featureFlags` map. Every public flag declared in `FeatureFlagConstants` appears as a `name -> bool` entry. If the relevant flag is `false` and the user has not consented to enabling it, choose a different approach instead — there is no programmatic bypass.

## Common cases

- **`webview-dev-tools`** gates every `webview_*` tool. Without it, all webview automation is unavailable.
- **`webview-dev-tools-eval`** is a separate, narrower flag that gates only `webview_eval` because arbitrary JavaScript evaluation is the riskiest webview surface.
- **`answer-dialog`** gates the `app_answer_dialog` MCP tool, which lets a script answer a modal dialog without a human present. It is off by default.
