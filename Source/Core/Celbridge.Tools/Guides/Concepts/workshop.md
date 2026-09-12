# Workshop

The workshop is the server a Celbridge install publishes packages to and installs them from. It is the only part of the package system that leaves the machine: authoring a package, and the manifest that describes one, involve no server and are covered by `packages_overview`. Pages are published to the same workshop under their own manifest and workflow; see `pages_overview`.

**These tools are behind the `workshop` build-time feature flag, which is off by default.** A build that did not opt in returns a feature-flag error from every tool below, and the flag cannot be turned on from a project's config or the Feature Flags section — it is read from the app's `appsettings.json` at startup. If one of these tools is refused, do not retry it: choose another approach, or say the build does not have the feature. The rest of the `package` namespace is local to the project tree and works in every build.

**The publisher is the Author set once in Workshop settings**, on the Settings page, not a per-package manifest field. `package_publish` fails if no Author is configured.

## Must-knows

- **Publishing and installing are interactive by default.** `package_publish` and `package_install` confirm with the user before mutating the workshop or the project. Pass `confirmWithUser: false` only for unattended flows the user has consented to. See `silent_vs_interactive`.
- **`package_install` requires a loaded project.** Installing without a project loaded fails fast.
- **Install anywhere, but only `project:` loads.** A package installs into a `{packageName}` subfolder of the destination you choose, default `packages/`.
- **The package name comes from the manifest.** `package_publish` reads it from `[package].name`; there is no folder-name rule and no separate name argument, so the source folder can live under any readable root including a `temp:` staging area.
- **The irreversible admin tools always prompt.** `package_delete` (one version) and `package_unpublish` (every version) remove content irreversibly with no `confirmWithUser` opt-out, unlike `package_install` and `package_publish`, which are also destructive but opt-outable for agent workflows.

## Versions and aliases

A package is a container of immutable, server-numbered versions (1, 2, 3, ...). There is no version field in a manifest: the workshop assigns the number when a version is published.

Named **aliases** (`latest`, `stable`, ...) point at versions. `latest` is managed by the workshop; the rest are publisher-defined, and curating them is non-destructive — `package_set_alias` and `package_remove_alias` only repoint or detach a label, never touching version content. `package_info` returns both lists.

A version can be **deleted**, which removes its content bytes permanently. The version number, date, and content hash are retained — the number is never reused, and a vendored copy stays verifiable — but the bytes are gone. Durability rests on consumers vendoring what they depend on, not on the workshop promising eternal availability.

## History

Installing a package writes its workshop history to a generated `HISTORY.md` beside the manifest, newest first, and `package_publish` writes the same file for the version it assigns. This is metadata about the workshop rather than package content: it is excluded from uploads, and the workshop stays authoritative. `package_status` reads the installed version out of it.

Each entry is shaped for grep and fragment reasoning:

```
# my-widget@5

[time: 2026-06-13T15:14:50Z, author: Acme, hash: eb1ddd1ce6a9]

Merge the credits change into the rolled-back content.
```

A `name@version` header makes a quoted entry self-describing, followed by one bracketed metadata line — full UTC `time`, `author`, and a 12-character `hash` fingerprint — then the free-text summary. A deleted version adds `deleted: true` and renders `[package_deleted]` as its body, so a gap in the numbering is explained rather than silent. The full content hash stays authoritative in `package_info`; the short one is for cheap cross-checking of a summary's claims against the actual bytes.

## Tools

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

Reinstalling over an existing package folder replaces its contents, and the replaced files route through the resource trash, so the change is recoverable.
