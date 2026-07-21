# Packages

Packages extend Celbridge with editor contributions: packages contribute, projects instantiate. Each package lives in its own kebab-case subfolder (conventionally under `packages/`, e.g. `packages/my-widget`). Packages run inside a WebView2 control and communicate with the host via JSON-RPC. Web content (HTML, JavaScript, CSS) is typical but not required.

## Creating a package

There is no scaffolding tool — a package is a folder with a manifest. Write `packages/my-widget/package.toml` with the file tools using the manifest shape below, and the package is discovered on the next project load. A discovered package is active by default: its editors open matching files with no `.celbridge` entry required. A project touches the `.celbridge` file only to deviate — to disable a package via `[celbridge].disabled-packages`, or to configure a contribution with a `[[contribution]]` entry (see `project_structure`).

## Manifest (`package.toml`)

Every package folder must contain a `package.toml` at its root with at minimum a `[package]` section containing `name`:

```toml
[package]
name = "my-widget"        # identifier; matches the workshop's package name
title = "My Widget"       # display name

[contributes]
editors = ["my-editor.editor.toml"]
```

**Required:** `name`. **Optional:** `title` — the package's display name (the product), shown in Project Settings. Name it distinctly from its editors' `display-name` values, which name each editor for what it *is* (e.g. a `Notepad` package shipping a `Notepad Editor`), so a single-editor package does not read the same name twice. The `[contributes].editors` array lists the editor manifests (`*.editor.toml`) provided by the package. Every editor declares a `type`: `"document"` editors edit matching files (read `document_editor_contributions` for the manifest, handler, and read-only contract); `"utility"` editors are workspace fixtures whose instances own state files under the hidden `utils:` root (read `utility_documents`).

The manifest carries no author: the publisher recorded on each version is the **Author** set once in Workshop settings (Settings page), not a per-package field. `package_publish` fails if no Author is configured.

A package name is lowercase ASCII alphanumeric with single interior hyphens as the only separator, 1-64 characters. There is no version field in the manifest: version numbers are assigned by the workshop when a version is published.

## Versions, aliases, and history

The workshop models a package as a container of immutable, server-numbered versions (1, 2, 3, ...). Named **aliases** (`latest`, `stable`, ...) point at versions; `latest` is managed by the workshop, others are publisher-defined. `package_info` returns both lists.

A version can be **deleted** (`package_delete`), which removes its content bytes permanently. The version number, date, and content hash are retained — the number is never reused, and a vendored copy stays verifiable — but the bytes are gone. Celbridge does not model the server's hidden tombstone state. A deleted version still appears in `HISTORY.md` with its heading and metadata, rendering `[package_deleted]` in place of its summary, so the gap in the numbering is explained rather than silent. Durability rests on consumers vendoring what they depend on, not on the workshop promising eternal availability.

When you install a package, its workshop history is written to a generated `HISTORY.md` beside the manifest (newest first); `package_publish` writes the same file for the version it assigns. This is metadata about the workshop, not package content — it is excluded from uploads, and the workshop stays authoritative for publish history. The installed (or last-published) version is recorded in `HISTORY.md`, which is where `package_status` reads it from.

Each entry is shaped for grep/fragment reasoning:

```
# my-widget@5

[time: 2026-06-13T15:14:50Z, author: Acme, hash: eb1ddd1ce6a9]

Merge the credits change into the rolled-back content.
```

The header is a `name@version` token (so a quoted entry is self-describing), followed by one compact bracketed metadata line — `time` (full UTC timestamp), `author`, and a 12-character `hash` fingerprint — then the free-text summary. A deleted version adds `deleted: true` to the line and renders `[package_deleted]` as its body. The full content hash stays authoritative in `package_info`; the short `hash` is for cheap reasoning and cross-checking a summary's claims against the actual bytes.

## Workshop workflow

| Tool | What it does |
|---|---|
| `package_list()` | List all packages available in the workshop |
| `package_info("name")` | Inspect a package's versions and aliases |
| `package_install("name", version, destination)` | Download and extract a version (or alias) into a destination folder |
| `package_publish("packages/name/package.toml", summary)` | Validate and publish a new version; name read from the manifest |
| `package_set_alias("name", "stable", 3)` | Point an alias at a version |
| `package_remove_alias("name", "stable")` | Remove an alias |
| `package_delete("name", "3")` | Delete one version permanently (always confirms) |
| `package_unpublish("name")` | Remove a whole package and every version (always confirms) |

`package_publish` reads the published name from the manifest's `[package].name`, so the source folder can have any name and live under any readable root, including a `temp:` staging area.

## Confirmation prompts

`package_publish` and `package_install` are destructive and confirm by default. Both accept `confirmWithUser` (default `true`); pass `false` only when the user has explicitly asked for unattended operation. Reinstalling over an existing package folder replaces its contents — the replaced files route through the resource trash, so the change is recoverable. Alias curation is non-destructive and is not gated.

`package_delete` and `package_unpublish` remove workshop content permanently and **always confirm** — they have no `confirmWithUser` opt-out, because the bytes cannot be recovered through the workshop. They are held to a firmer bar than `package_install`/`package_publish` (which are reversible via trash or re-install) for that reason.

For the JS proxy conventions and `[permissions] tools` declarations packages need at runtime, see `agent_instructions`.
