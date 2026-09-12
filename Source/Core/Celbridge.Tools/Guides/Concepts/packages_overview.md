# Packages

Packages extend Celbridge with editor contributions: packages contribute, projects instantiate. Each package lives in its own kebab-case subfolder (conventionally under `packages/`, e.g. `packages/my-widget`). Packages run inside a WebView2 control and communicate with the host via JSON-RPC. Web content (HTML, JavaScript, CSS) is typical but not required.

## Creating a package

There is no scaffolding tool — a package is a folder with a manifest. Write `packages/my-widget/package.toml` with the file tools using the manifest shape below, and the package is discovered on the next project load. A discovered package is active by default: its editors open matching files with no `.celbridge` entry required. A project touches the `.celbridge` file only to deviate — to disable a package via `[celbridge].disabled-packages`, or to configure a contribution with a `[[contribution]]` entry (see `project_structure`).

## Manifest (`package.toml`)

Every package folder must contain a `package.toml` at its root with at minimum a `[package]` section containing `name`:

```toml
[package]
name = "my-widget"        # identifier
title = "My Widget"       # display name

[contributes]
editors = ["my-editor.editor.toml"]
```

**Required:** `name`. **Optional:** `title` — the package's display name (the product), shown in Project Settings. Name it distinctly from its editors' `display-name` values, which name each editor for what it *is* (e.g. a `Scratchpad` package shipping a `Scratchpad Editor`), so a single-editor package does not read the same name twice. The `[contributes].editors` array lists the editor manifests (`*.editor.toml`) provided by the package. Every editor declares a `type`: `"document"` editors edit matching files (read `document_editor_contributions` for the manifest, handler, and read-only contract); `"utility"` editors are workspace fixtures that own state files under the hidden `utils:` root (read `utility_documents`).

A package name is lowercase ASCII alphanumeric with single interior hyphens as the only separator, 1-64 characters. The manifest carries no author field and no version field.

## Installed packages

`package_status` reports each project package's name, version, and folder, plus any load failures such as a duplicate-name fault. It reads only the project.

`package_archive` and `package_unarchive` are generic zip and unzip against the project tree, useful for staging or vendoring a package folder by hand.

For the JS proxy conventions packages need at runtime, see `agent_instructions`.
