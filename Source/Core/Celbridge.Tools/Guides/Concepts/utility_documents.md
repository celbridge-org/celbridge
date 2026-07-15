# Utility documents

A utility is a custom WebView editor that is not tied to a user-authored file. Instead of claiming a file extension across the project, it owns one fixed backing resource under the hidden `utils:` root. It is the right shape for a tool the user reaches for occasionally and of which there is only ever one instance — a colour picker, a sprite renderer, a scratchpad, a long-running process view. A utility is an ordinary `type = "custom"` document editor plus utility metadata, so read `document_editor_contributions` first: everything about the manifest, the JS handlers, save, and read-only handling applies unchanged. This guide covers only what is specific to a utility.

## A utility is a permanent fixture

Every utility is **workspace-scoped**: it is created when the project loads and lives until the project closes. It is never created on demand and never destroyed by the user — like Explorer and Search, it is always there. What the user controls is only *where* it is docked. A utility always occupies exactly one **dock location**:

- **Utility Panel** — the utility is a rail surface in the Utility Panel (the left sidebar), selected by clicking its rail button, shown one at a time alongside Explorer and Search.
- **Document** — the utility is a tab in the documents area, sitting among the open documents.

Both are docked locations inside the app; neither is free-floating. The user moves a utility between them at runtime and the *same* live WebView is reparented across — no reload, no lost state. This is the VS Code affordance of moving a view between the sidebar and the editor group. Every utility has both a permanent rail button and a document tab it can occupy; the manifest does not pre-decide the location.

## Moving between dock locations

- **Dock as a document** ("Open as document"): a control in the utility's Utility Panel header moves it into the documents area, in the section of the active document, and makes it the active document. Its rail button stays but dims to show it now lives as a document, and the panel falls back to Explorer.
- **Dock back into the panel** (close the tab): the close button on a utility's document tab does not destroy it — it reparents the WebView back to the Utility Panel. The utility returns to the panel, reachable from its rail button as before. A utility therefore can never be truly closed; the close control means "send it back to the panel".
- Clicking the rail button of a utility that is docked as a document activates its document tab (with a brief highlight) rather than showing a panel surface, since its surface has moved out of the panel.

The dock location survives a reload: a utility that was docked as a document when the project closed reopens in the same tab position.

## The `[utility]` manifest section

A utility ships as a normal package with the standard two-file manifest. What marks it as a utility is a `[utility]` section in the document manifest. The section is mutually exclusive with `[[document_file_types]]` — a manifest with both is rejected. A utility has no file-type entry because its editor extension is derived from the `resource` field.

`packages/notepad/package.toml`:

```toml
[package]
name = "notepad"
title = "Notepad_Package"

[contributes]
document_editors = ["notepad.document.toml"]
```

`packages/notepad/notepad.document.toml`:

```toml
[document]
id = "notepad"
type = "custom"
entry_point = "index.html"

[utility]
resource = "utils:settings._notepad"      # the fixed backing resource (required)
template = "templates/default._notepad"   # seeds the file when absent (optional)
icon     = "sticky"                        # Bootstrap Icons glyph name (required)
tooltip  = "Notepad_Tooltip"              # localization key (required)
```

| Field | Required | Default | Meaning |
|---|---|---|---|
| `resource` | yes | — | The fixed backing resource under `utils:`. The single source of truth for the utility's identity, and the value the editor extension is derived from. |
| `icon` | yes | — | Bootstrap Icons glyph name for the rail button and the docked tab icon (resolved by name, not limited to the curated symbol set). |
| `tooltip` | yes | — | Localization key. Drives the rail button tooltip, the accessible name, and the docked tab title. There is no separate label field. |
| `template` | no | empty file | Package-relative path to a file that seeds the backing resource when it is absent. |

`display_name` in `[document]` is optional. When omitted it defaults to the `tooltip` key; it labels the Utility Panel header.

The manifest declares no dock location: it is a runtime, user-controlled property (the user moves the utility between the Utility Panel and a document tab), never a manifest choice.

## Never an ordinary project file

A utility is never created as a normal project file — it does not appear in New File, text search, the Explorer tree, or the "Reopen with..." picker. Its `utils:` resource is reachable only through the utility itself: its rail button, or docking it into a document tab.

## The `utils:` root and naming convention

Utility state lives under the `utils:` root (`.celbridge/utils/`). Like `temp:` and `logs:` it is hidden from the Explorer, text search, and the New File dialog, and is ungoverned by resource policy (no locks, no sidecars). Unlike `temp:`, it is **not wiped on load** — it is the durable home for per-project utility state. `.celbridge/` is gitignored, so the state is local to the machine and never committed. See `resource_keys` for the root itself.

Name the backing resource `utils:settings._<name>`:

- The **stem is always `settings`** — the file's job is "this utility's persisted state". The rail and tab title come from the manifest, not the filename.
- The **extension is the identity** and must be unique per utility (`._notepad`, `._process`, ...). Uniqueness earns the utility a hidden "Reopen with..." item for free, because only one factory ever claims that extension.
- The **leading underscore** marks the file as "not a normal file type" in any tooling that surfaces the path, and makes collision with a real extension unlikely.

One backing file per utility is assumed. If a utility needs more than one, use a per-utility subfolder (`utils:notepad/settings._notepad`, `utils:notepad/cache._notepad`) rather than overloading the stem.

## Create-if-missing

The backing file does not have to exist. When a utility is created on project load and its file is absent, the host seeds it from the manifest `template` (or an empty file when `template` is omitted) with a direct filesystem write, then proceeds. This also covers session restore and doubles as the recovery path when the user deletes the `.celbridge` file. Author the template as the utility's default state (for example a Notepad template of `{"text":""}`).

## Persistence

A utility persists through the standard editable-save path: the WebView calls `client.document.save(state)` from `onRequestSave`, and the host auto-saves it to the `utils:` file. There is no new save plumbing and no user-facing Save — recovery is via the same contract every custom editor uses. Saving is unaffected by dock state; the utility saves the same way whether it is showing in the panel or docked as a document.

## Agent interaction

- `app_list_utilities` lists every available utility — the built-in Explorer and Search plus contributed utilities — with each one's id, display name, `location` (`"panel"` or `"document"`, its current dock location), and whether it is currently shown.
- `app_show_utility` reveals a utility by id wherever it currently lives: it selects a utility's rail tab when it is in the panel, or activates its document tab when it is docked as a document. Pass an optional `location` (`"panel"` or `"document"`) to move it there first. A contributed utility's id is `{packageName}.{documentId}`.
- `app_get_state` reports `activeUtility`, the id of the surface currently shown in the Utility Panel rail.
- `app_spotlight` can point at a utility's button: `{utilityId}-utility-button` for its rail item in the Utility Panel.
- `utils:` is a registered root, so `file.*` tools can read and write a utility's backing file when the package declares `file.*` under `[permissions] tools`. This is useful for preparing or inspecting a utility's state. The editor's own `client.document.save`/`load` contract needs no permission — it is framework-level, distinct from the `cel.*` tool proxies.

## Reference contributions

| Utility | Path | Demonstrates |
|---|---|---|
| Notepad | `Source/Modules/Celbridge.DocumentEditors/Editors/Notepad/` | A utility backed by a JSON state blob, with a template seeding its default state |
| Process | `Source/Modules/Celbridge.DocumentEditors/Editors/Process/` | A utility that hosts a long-running per-project process view |
