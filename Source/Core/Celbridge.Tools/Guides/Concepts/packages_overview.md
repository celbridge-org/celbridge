# Packages

Packages extend Celbridge with custom document editors and other contributions. Each package lives in its own kebab-case subfolder under `packages/` (e.g. `packages/my-widget`). Packages run inside a WebView2 control and communicate with the host via JSON-RPC. Web content (HTML, JavaScript, CSS) is typical but not required.

## Creating a package

```python
package.create("my-widget")
```

Creates `packages/my-widget/` with a stub `package.toml` manifest.

## Manifest (`package.toml`)

Every package folder must contain a `package.toml` at its root with at minimum a `[package]` section containing `id` and `name`:

```toml
[package]
id = "my-widget"
name = "My Widget"
version = "1.0.0"

[contributes]
document_editors = ["my-editor.document.toml"]
```

**Required:** `id`, `name`. **Optional:** `version`, `feature_flag`. The `[contributes]` section lists document editor manifests provided by the package.

## Registry workflow

| Tool | What it does |
|---|---|
| `package_create("name")` | Create a new package with a stub manifest |
| `package_publish("packages/name", "name")` | Validate and upload to the registry |
| `package_install("name")` | Download and extract from the registry |
| `package_list()` | List all packages available in the registry |

To publish, the package must live under `packages/`, the folder name must match the package id, and the manifest must be valid.

## Confirmation prompts

`package_publish` and `package_install` are destructive. Both accept `confirmWithUser` (default `true`). Pass `false` only when the user has explicitly asked for unattended operation.

For the JS proxy conventions and `requires_tools` declarations packages need at runtime, see `agent_instructions`.
