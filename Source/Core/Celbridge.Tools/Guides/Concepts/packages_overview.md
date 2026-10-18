# Packages

Packages extend Celbridge with editor contributions: packages contribute, projects instantiate. Packages run inside a WebView2 control and communicate with the host via JSON-RPC. Web content (HTML, JavaScript, CSS) is typical but not required.

## Package folders

A package is a direct subfolder of a package folder, with a `package.toml` at its root, e.g. `packages/my-widget/package.toml`. Every project has the package folder `packages/`. The project file can list more in `folders` in its `[celbridge.packages]` table (see `project_structure`). Name each package's folder in kebab-case.

Discovery runs when the project loads. It lists each package folder and reads the manifest in each subfolder. A `package.toml` anywhere else is an ordinary file. That includes a manifest at the project root, one in a folder that is not a package folder, and one nested inside a package. A listed folder that is the project root or lies inside a package is refused, and the load report names it. A missing listed folder adds no packages. A change to the list applies when the project reloads.

## Creating a package

There is no scaffolding tool — a package is a folder with a manifest. Write `packages/my-widget/package.toml` with the file tools using the manifest shape below, and the package is discovered on the next project load. A discovered package is active by default: its editors open matching files with no `.celbridge` entry required. A project touches the `.celbridge` file only to deviate from the defaults. A project disables a package by listing the package in `disabled` in the `[celbridge.packages]` table, and configures a contribution with a `[[contribution]]` entry (see `project_structure`).

## Manifest (`package.toml`)

Every package folder must contain a `package.toml` at its root with at minimum a `[package]` section containing `name`:

```toml
[package]
name = "my-widget"        # identifier
title = "My Widget"       # display name
package-version = "1.2.0" # the package's own version

[contributes]
editors = ["my-editor.editor.toml"]
```

**Required:** `name`. **Optional:** `title` and `package-version`. The `title` is the package's display name (the product), shown in Project Settings. Name it distinctly from its editors' `display-name` values, which name each editor for what it *is* (e.g. a `Scratchpad` package shipping a `Scratchpad Editor`), so a single-editor package does not read the same name twice. The `[contributes].editors` array lists the editor manifests (`*.editor.toml`) provided by the package. Every editor declares a `type`: `"document"` editors edit matching files (read `document_editor_contributions` for the manifest, handler, and read-only contract); `"utility"` editors are workspace fixtures that own state files under the hidden `utils:` root (read `utility_documents`).

A package name is lowercase ASCII alphanumeric with single interior hyphens as the only separator, 1-64 characters. The manifest carries no author field.

The `package-version` is three dot-separated numbers, such as `1.2.0`, with no `v` prefix and no suffix. A manifest without one is version `1.0.0`. A malformed version stops the package loading, and `app_list_packages` lists it among the load failures. The package version belongs to the package, and is separate from the version numbers the workshop assigns when a package is published.

## Installed packages

`app_list_packages` reports each project package's name, package version, and folder, plus any load failures such as a duplicate-name fault, and `app_get_state` carries the same packages as a summary. Both report the packages as the project loaded. A package added, removed or edited on disk during the session shows after the project reloads, so read the `package.toml` and `.celbridge` files to see what a project declares now.

`explorer_archive` and `explorer_unarchive` zip and unzip against the project tree, useful for staging or vendoring a package folder by hand.

For the JS proxy conventions packages need at runtime, see `agent_instructions`.
