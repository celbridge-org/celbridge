# Utility documents

A utility is a custom WebView editor that is a workspace fixture rather than an editor of user-authored files. Instead of claiming a file extension across the project, it owns a single state file under the hidden `utils:` root. It is the right shape for a tool the user reaches for occasionally — a colour picker, a sprite renderer, a scratchpad, a long-running process view. A utility is an ordinary editor contribution with `type = "utility"`, so read `document_editor_contributions` first: everything about the manifest, the JS handlers, save, and read-only handling applies unchanged. This guide covers only what is specific to a utility.

## One utility per contribution

A package contributes a utility editor, and a discovered package is active by default — so its utility appears automatically, with its own rail button and backing state file. There is one utility per contribution: the project does not declare it, name it, or spin up several copies. A tool that needs several variants (say Python, Claude, and PowerShell consoles) ships them as separate contributions, not as repeated copies of one.

The `.celbridge` file touches a utility only to *deviate* from its manifest defaults, through the same `[[contribution]]` entry any editor uses — to set config keys, or to disable a `recommended` utility / enable an `optional` one:

```toml
[[contribution]]
package      = "scratchpad"
contribution = "scratchpad"
wrap         = true              # a config key from the utility's [[config]] descriptors
```

A utility's display name, icon, and description (shown as its tooltip) come from its manifest; the project cannot override them per contribution. Rail order follows discovery order (package load order, then manifest order).

## A utility is a permanent fixture

Every utility is **workspace-scoped**: it is created when the project loads and lives until the project closes. It is never destroyed by the user — like Explorer and Search, it is always there. What the user controls is only *where* it is docked. A utility always occupies exactly one **workspace area**:

- **`utility`** — the utility is shown in the Utility Panel (the left sidebar), selected by clicking its rail button, one at a time alongside Explorer and Search.
- **`main`**, **`bottom`**, **`side`** — the utility is a tab in that documents area, sitting among the open documents.

All four are areas inside the app; none is free-floating. The user moves a utility between the areas it allows at runtime and the *same* live WebView is reparented across — no reload, no lost state. This is the VS Code affordance of moving a view between the sidebar and the editor group.

The manifest's `areas` key says which of them a utility may occupy, and `default-area` names the one it falls back to. A manifest declaring neither allows `utility` and `main`, which is what every utility did before the keys existed.

## Moving between areas

- **Dock as a document** ("Open as document"): a control in the utility's Utility Panel header moves it into its document area, in that area's primary section, and makes it the active document. Its rail button stays but dims to show it now lives as a document, and the panel falls back to Explorer. Docking into `bottom` or `side` reveals that area first when it is collapsed. The control is absent for a utility that declares no document area, or several without defaulting to one of them, because there is nowhere for it to send the utility.
- **Dock back into the panel** (close the tab): the close button on a utility's document tab does not destroy it — it reparents the WebView back to the Utility Panel. The utility returns to the panel, reachable from its rail button as before. A utility therefore can never be truly closed; the close control means "send it back to the panel".
- Clicking the rail button of a utility that is docked as a document activates its document tab (with a brief highlight) rather than showing anything in the panel, since its view has moved out of the panel.

The area survives a reload: a utility that was docked as a document when the project closed reopens in the same tab position.

## The `[utility]` manifest section

A utility ships as a normal package with the standard two-file manifest. `type = "utility"` requires a `[utility]` section and forbids `[[file-types]]`.

`packages/scratchpad/package.toml`:

```toml
[package]
name = "scratchpad"
title = "Scratchpad_Package"

[contributes]
editors = ["scratchpad.editor.toml"]
```

`packages/scratchpad/scratchpad.editor.toml`:

```toml
[editor]
id = "scratchpad"
type = "utility"
entry-point = "index.html"
display-name = "Scratchpad_DisplayName"     # required; labels the rail button and docked tab
description = "Scratchpad_Description"      # localization key; the rail-button and docked-tab tooltip

[utility]
resource-extension = "._scratchpad"        # file format of the utility state file (required)
template = "templates/default._scratchpad" # seeds the file when absent (optional)
icon     = "bs-sticky"                     # prefixed icon name (required)
areas    = ["utility", "main"]             # optional; the areas this utility may occupy
default-area = "utility"                   # optional; where it starts
```

| Field | Required | Default | Meaning |
|---|---|---|---|
| `resource-extension` | yes | — | File extension of the utility's backing state file. The host derives the full path from the utility's id, as `utils:{package}.{contribution}{resource-extension}`. |
| `icon` | yes | — | Prefixed icon name (`<font>-<name>`, e.g. `bs-sticky`) for the rail button and the docked tab icon. Resolved by name, not limited to the curated symbol set. |
| `template` | no | empty file | Package-relative path to a file that seeds a utility's backing resource when it is absent. |
| `areas` | no | `["utility", "main"]` | The areas this utility may occupy. A non-empty set drawn from `utility`, `main`, `bottom` and `side`, with no duplicates. It must include `utility`: a utility always occupies the Utility Panel, which is where its live view parks when its tab closes. |
| `default-area` | no | `utility` | The area the utility falls back to when no other one is named: what the "Open as document" control and the `"document"` tool alias resolve to, and where a utility is restored when its stored area is no longer allowed. Must be one of the areas `areas` declares. |

`display-name` in `[editor]` is required (as for any editor) and labels the rail button and the docked tab. The tooltip comes from `[editor].description` — the same field a document editor uses — so a utility's rail-button and docked-tab tooltip are authored once there, not in `[utility]`.

### The manifest declares the constraint, not the position

Where a utility actually is comes from workspace state, which the user writes by moving it: a utility opens each workspace in the Utility Panel and is restored wherever it was left. The manifest declares what is allowed, not where the utility sits.

The constraint is enforced on restore as well as on a move. A utility whose stored area is no longer in `areas` — which is what a package update narrowing its own declaration produces — is restored at `default-area` rather than in an area it has stopped claiming it can live in.

## Never an ordinary project file

A utility is never created as a normal project file — it does not appear in New File, text search, the Explorer tree, or the "Reopen with..." picker. Its `utils:` resource is reachable only through the utility itself: its rail button, or docking it into a document tab.

## The `utils:` root and state file naming

Utility state lives under the `utils:` root (`.celbridge/utils/`). Like `temp:` and `logs:` it is hidden from the Explorer, text search, and the New File dialog, and is ungoverned by resource policy (no locks, no sidecars). Unlike `temp:`, it is **not wiped on load** — it is the durable home for per-project utility state. `.celbridge/` is gitignored, so the state is local to the machine and never committed. See `resource_keys` for the root itself.

A utility's backing file is `utils:{package}.{contribution}{resource-extension}` — for a scratchpad utility (package `scratchpad`, contribution `scratchpad`, extension `._scratchpad`), `utils:scratchpad.scratchpad._scratchpad`. The path is derived from the contribution identity, so no extension-uniqueness rules are needed.

## Create-if-missing

The backing file does not have to exist. When a utility is created on project load and its file is absent, the host seeds it from the manifest `template` (or an empty file when `template` is omitted) with a direct filesystem write, then proceeds. This also covers session restore and doubles as the recovery path when the user deletes the `.celbridge` folder. Author the template as the utility's default state (for example a scratchpad template of `{"text":""}`).

## Persistence

A utility persists through the standard editable-save path: the WebView calls `client.document.save(state)` from `onRequestSave`, and the host auto-saves it to the `utils:` file. There is no new save plumbing and no user-facing Save — recovery is via the same contract every custom editor uses. Saving is unaffected by dock state; the utility saves the same way whether it is showing in the panel or docked as a document.

## Agent interaction

- `app_list_utilities` lists every available utility — the built-in Explorer and Search plus the active utility contributions — with each one's id, display name, `area` (`"utility"` or a document area token, where it currently is), `allowedAreas` (the areas it may be moved to), and whether it is currently shown.
- `app_show_utility` reveals a utility by id wherever it currently lives: it selects a utility's rail tab when it is in the panel, or activates its document tab when it is docked as a document. Pass an optional `area` (an area token, or `"document"` for whichever document area the utility declares) to move it there first, which fails when the utility does not allow that area. A utility's id is `package.contribution` (for example `scratchpad.scratchpad`).
- `app_get_state` reports `activeUtility`, the id of the item the Utility Panel rail is currently showing.
- `app_spotlight` can point at a utility's button: `{utilityId}-utility-button` for its rail item in the Utility Panel.
- `utils:` is a registered root, so `file.*` tools can read and write a utility's backing file when the package declares `file.*` under `[permissions] tools`. This is useful for preparing or inspecting a utility's state. The editor's own `client.document.save`/`load` contract needs no permission — it is framework-level, distinct from the `cel.*` tool proxies.

## Reference contributions

| Utility | Path | Demonstrates |
|---|---|---|
| Utility Demo | `Source/Modules/Celbridge.DocumentEditors/Editors/UtilityDemo/` | A utility backed by a JSON state blob, with a template seeding its default state, and the reference for the shared styling tokens |
