# Utility documents

A utility document is a custom WebView editor that is not tied to a user-authored file. Instead of claiming a file extension across the project, it owns one fixed backing resource under the hidden `utils:` root and contributes a single icon button to the title bar. It is the right shape for a tool the user reaches for occasionally and of which there is only ever one instance — a colour picker, a sprite renderer, a scratchpad. A utility is an ordinary `type = "custom"` document editor plus utility metadata, so read `document_editor_contributions` first: everything about the manifest, the JS handlers, save, and read-only handling applies unchanged. This guide covers only what is specific to a utility.

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
resource   = "utils:settings._notepad"   # the fixed backing resource (required)
template   = "templates/default._notepad"  # seeds the file when absent (optional)
icon       = "sticky"                       # Bootstrap Icons glyph name (required)
tooltip    = "Notepad_Tooltip"              # localization key (required)
auto_open  = false                          # open automatically on project load (optional)
closable   = true                           # false = user cannot close the tab (optional)
```

| Field | Required | Default | Meaning |
|---|---|---|---|
| `resource` | yes | — | The fixed backing resource under `utils:`. The single source of truth for the utility's identity, and the value the editor extension is derived from. |
| `icon` | yes | — | Bootstrap Icons glyph name for the launcher button and the tab icon (resolved by name, not limited to the curated symbol set). |
| `tooltip` | yes | — | Localization key. Drives the hover tooltip, the accessible name, and the tab title. There is no separate label field. |
| `template` | no | empty file | Package-relative path to a file that seeds the backing resource when it is absent. |
| `auto_open` | no | `false` | Open the utility automatically on project load. |
| `closable` | no | `true` | When `false`, the user cannot close the tab. |

`display_name` in `[document]` is optional for a utility. When omitted it defaults to the `tooltip` key.

## The `utils:` root and naming convention

Utility state lives under the `utils:` root (`.celbridge/utils/`). Like `temp:` and `logs:` it is hidden from the Explorer, text search, and the New File dialog, and is ungoverned by resource policy (no locks, no sidecars). Unlike `temp:`, it is **not wiped on load** — it is the durable home for per-project utility state. `.celbridge/` is gitignored, so the state is local to the machine and never committed. See `resource_keys` for the root itself.

Name the backing resource `utils:settings._<name>`:

- The **stem is always `settings`** — the file's job is "this utility's persisted state". The tab title comes from the manifest `tooltip`, not the filename.
- The **extension is the identity** and must be unique per utility (`._notepad`, `._emoji`, ...). Uniqueness earns the utility a hidden "Reopen with..." item for free, because only one factory ever claims that extension.
- The **leading underscore** marks the file as "not a normal file type" in any tooling that surfaces the path, and makes collision with a real extension unlikely.

One backing file per utility is assumed. If a utility needs more than one, use a per-utility subfolder (`utils:notepad/settings._notepad`, `utils:notepad/cache._notepad`) rather than overloading the stem.

## Launching, never creating

A utility is launched from its icon button in the title bar, immediately to the right of the project button. It is **never created as an ordinary project file** — it does not appear in New File, text search, the Explorer tree, or the "Reopen with..." picker. Clicking the button opens the utility, or activates its existing tab if it is already open (singleton per utility). The button reflects no state, because clicking has the same result either way.

## Create-if-missing

The backing file does not have to exist. When a utility is opened and its file is absent, the host seeds it from the manifest `template` (or an empty file when `template` is omitted) with a direct filesystem write, then proceeds with the open. This seed is on the open path, so it covers every route that opens a utility — the launch button, `auto_open`, and session restore — and doubles as the recovery path when the user deletes the `.celbridge` file. Author the template as the utility's default state (for example a Notepad template of `{"text":""}`).

## Auto-open and non-closable ("process") utilities

- `auto_open = true` opens the utility on project load, after the previous session's layout is restored, so a utility already restored from last session is not opened twice and the restored active tab is not stolen.
- `closable = false` makes the tab non-closable: the tab's close button is hidden and the bulk-close actions (Close Others, Close All, ...) skip it. Project teardown still closes it.

Combining the two is the long-running "process" pattern: a durable per-project surface that starts on load and cannot be accidentally closed. Set `auto_open = true` deliberately — an always-open, non-closable tab is intrusive in every session.

## Persistence

A utility persists through the standard editable-save path: the WebView calls `client.document.save(state)` from `onRequestSave`, and the host auto-saves it to the `utils:` file. There is no new save plumbing and no user-facing Save — recovery is via the same contract every custom editor uses.

## Agent addressability

`utils:` is a registered root, so `file.*` tools can read and write a utility's backing file when the package declares `file.*` under `[permissions] tools`. This is useful for preparing or inspecting a utility's state from an agent or from the package's own `cel.file.*` calls. The editor's own `client.document.save`/`load` contract needs no permission — it is framework-level, distinct from the `cel.*` tool proxies.

## Reference contributions

| Utility | Path | Demonstrates |
|---|---|---|
| Notepad | `Source/Modules/Celbridge.DocumentEditors/Editors/Notepad/` | Ordinary utility: `auto_open = false`, `closable = true`, JSON state blob |
| Process | `Source/Modules/Celbridge.DocumentEditors/Editors/Process/` | Non-closable "process" pattern: `closable = false` |
