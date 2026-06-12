# Packages

Packages extend Celbridge with custom document editors and other contributions. Each package lives in its own kebab-case subfolder under `packages/` (e.g. `packages/my-widget`). Packages run inside a WebView2 control and communicate with the host via JSON-RPC. Web content (HTML, JavaScript, CSS) is typical but not required.

## Creating a package

There is no scaffolding tool — a package is a folder with a manifest. Write `packages/my-widget/package.toml` with the file tools using the manifest shape below, and the package is discovered on the next project load.

## Manifest (`package.toml`)

Every package folder must contain a `package.toml` at its root with at minimum a `[package]` section containing `name`:

```toml
[package]
name = "my-widget"        # identifier; matches the workshop's package name
author = "Acme"           # read by the workshop when a version is published
title = "My Widget"       # display name

[contributes]
document_editors = ["my-editor.document.toml"]
```

**Required:** `name`. **Optional:** `author`, `title`, `feature_flag`. The `[contributes]` section lists document editor manifests provided by the package. If your package contributes a document editor, also read `document_editor_contributions` for the manifest, handler, and read-only contract.

A package name is lowercase ASCII alphanumeric with single interior hyphens as the only separator, 1-64 characters. There is no version field: version numbers are assigned by the workshop when a version is published.

## Workshop workflow

| Tool | What it does |
|---|---|
| `package_publish("packages/name", "name")` | Validate and publish a new version to the workshop |
| `package_install("name")` | Download and extract the latest version from the workshop |
| `package_list()` | List all packages available in the workshop |

To publish, the package must live under `packages/`, the folder name must match the package name, and the manifest must be valid with a `name` equal to the published name.

## Confirmation prompts

`package_publish` and `package_install` are destructive. Both accept `confirmWithUser` (default `true`). Pass `false` only when the user has explicitly asked for unattended operation.

For the JS proxy conventions and `[permissions] tools` declarations packages need at runtime, see `agent_instructions`.
