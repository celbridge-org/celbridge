# Packages

Packages extend Celbridge with custom document editors and other contributions. Each package lives in its own kebab-case subfolder under `packages/` at the project root (e.g. `packages/my-widget`). Packages run inside a WebView2 control and communicate with the host via JSON-RPC. They can contain any kind of content; web content (HTML, JavaScript, CSS) is typical.

## Creating a package

```python
package.create("my-widget")
```

This creates `packages/my-widget/` with a stub `package.toml` manifest.

## Manifest (`package.toml`)

Every package folder must contain a `package.toml` file at its root with at minimum a `[package]` section containing `id` and `name`:

```toml
[package]
id = "my-widget"
name = "My Widget"
version = "1.0.0"

[contributes]
document_editors = ["my-editor.document.toml"]
```

**Required fields:** `id`, `name`. **Optional:** `version`, `feature_flag`. The `[contributes]` section lists document editor manifests provided by the package.

## Registry workflow

Packages are published to and installed from a remote package registry:

| Tool | What it does |
|---|---|
| `package_create("name")` | Create a new package with a stub manifest |
| `package_publish("packages/name", "name")` | Validate and upload to the registry |
| `package_install("name")` | Download and extract from the registry |
| `package_list()` | List all packages available in the registry |

To publish, the package must be in the `packages/` folder, the folder name must match the package id, and the manifest must be valid.

## Confirmation prompts

`package_publish` and `package_install` are destructive actions. Both accept a `confirmWithUser` parameter (default `true`) that displays a confirmation dialog in the application before proceeding. Pass `true` (or omit the parameter) unless the user has explicitly asked for unattended operation (e.g. inside an install script).

For the JS proxy conventions and `requires_tools` declarations packages need at runtime, see the JavaScript-proxy section in `agent_instructions`.
