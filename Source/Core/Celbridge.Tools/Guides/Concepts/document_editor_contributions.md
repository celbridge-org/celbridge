# Document editor contributions

A package contribution that takes over a file extension in the documents panel. The editor runs in a WebView2 and talks to the host through `https://shared.celbridge/celbridge-client/celbridge.js`. Read `packages_overview` first.

## Manifest

`packages/my-editor/package.toml`:

```toml
[package]
id = "my-editor"
name = "My Editor"
version = "1.0.0"

[contributes]
document_editors = ["my-editor.document.toml"]

[mod]
requires_tools = ["document.*", "file.*"]
```

`packages/my-editor/my-editor.document.toml`:

```toml
[document]
id = "my-editor-document"
type = "custom"
entry_point = "index.html"
priority = "specialized"
display_name = "MyEditor_Editor_Name"

[[document_file_types]]
extension = ".myext"
display_name = "MyEditor_FileType_MyExt"

[[document_templates]]
id = "empty"
display_name = "MyEditor_Template_Empty"
template_file = "templates/empty.myext"
default = true
```

`priority`: `"specialized"` wins over `"general"` for the same extension. `display_name` values are localization keys. Templates are optional.

## JS handlers

```javascript
import celbridge from 'https://shared.celbridge/celbridge-client/celbridge.js';
import { ContentLoadedReason } from 'https://shared.celbridge/celbridge-client/api/document-api.js';

const client = celbridge;

await client.initializeDocument({
    onContent: async (content, metadata) => { /* load into editor */ },
    onRequestSave: async () => { /* await client.document.save(serialised) */ },
    onExternalChange: async () => { /* reload, then notifyContentLoaded(ExternalReload) */ },
    onRequestState: () => { /* return opaque snapshot string or null */ },
    onRestoreState: (stateJson) => { /* apply snapshot */ },
    onWritableStateChanged: ({ state }) => { /* apply read-only state */ }
});
```

- **`onContent(content, metadata)`** — initial load. `content` is string or base64; `metadata.resourceKey` is the resource key. Framework calls `notifyContentLoaded()` for you. Do not save here; suppress framework update events (see trap).
- **`onRequestSave()`** — auto-save, tab close, programmatic flush. `await client.document.save(content)`. May fire while the tab is hidden.
- **`onExternalChange(args)`** — file changed on disk. `client.document.load()`, apply with the spurious-update guard, then `client.document.notifyContentLoaded(ContentLoadedReason.ExternalReload)`. Forward `args.preserveViewState`.
- **`onRequestState()` / `onRestoreState(stateJson)`** — opaque string round-trip for scroll, selection, pending view state. Survives external reloads and session restore. Return `null` if nothing to preserve.
- **`onWritableStateChanged({ state })`** — see below. Required.

## `onWritableStateChanged` is required

Fires once during `initializeDocument` with the initial state, then again whenever it changes mid-session. `state` is one of:

- `"Writable"` — accept edits.
- `"Locked"` — `[resources].lock` pattern match.
- `"ReadOnlyAttribute"` — OS read-only bit set.
- `"ReadOnlyRoot"` — non-writable resource root.

Treat **anything other than `"Writable"`** as read-only. Same representation for all three non-writable states.

Read-only-by-design editors register an empty handler — a stub, not a TODO:

```javascript
onWritableStateChanged: () => {}
```

A future edit-mode addition must deliberately remove the no-op, surfacing the read-only obligation at review time. Precedent: `Source/Modules/Celbridge.DocumentEditors/Editors/FileViewer/js/file-viewer.js`.

## The spurious-update trap

Many editor frameworks emit "update" events for non-edits — TipTap's `setEditable(false)` fires `onUpdate` with a no-op transaction, SpreadJS's command manager fires through `import`, ProseMirror's `replaceWith` fires the same event as a keystroke. Wired naively, these route through `notifyChanged` → auto-save → and on a locked file, either fail loudly or strip the OS read-only attribute and clobber the user's choice.

**Gate `notifyChanged` on a `frameworkReadOnly` flag.** Single module-level boolean, checked at the top of every save-scheduling path:

```javascript
let frameworkReadOnly = false;

function applyWritableState(state) {
    frameworkReadOnly = state !== 'Writable';
    // ...apply to editor surface...
}

editor.on('update', ({ transaction }) => {
    if (frameworkReadOnly) return;
    if (!transaction?.docChanged) return;
    debounceNotifyChanged();
});
```

The `docChanged` guard is the second line of defence — catches no-op transactions on the writable side too.

**Suppress framework updates around framework-driven writes.** Initial load and external reload are not user edits:

```javascript
editor.commands.setContent(jsonContent, { emitUpdate: false });
```

Apply at every framework-driven `setContent` site.

## Read-only representations

| Editor surface | Signal |
|---|---|
| Code / Monaco | `editor.updateOptions({ readOnly: true })` plus disabled toolbar buttons |
| Rich-text / TipTap, ProseMirror | `editor.setEditable(false, false)` plus disabled toolbar buttons |
| Spreadsheet / SpreadJS | Translucent overlay absorbing pointer events (workbook surface is too multi-tiered to gate option-by-option) |
| Canvas / iframe-wrapped | `pointer-events: none` on the surface, muted filter on the wrapper |
| Presentation-only viewer | Explicit no-op handler |

## Cross-references

- **Localization** — `t('MyEditor_Editor_Name')` after `await client.initialize()`; strings live in `localization/<locale>.json` next to `index.html`.
- **Secrets** — bundled-package descriptors can inject `client.secrets.<name>`. Non-bundled packages see an empty map.
- **`requires_tools`** — every `cel.*` call must be declared under `[mod].requires_tools` in alias form (`"document.save"`). See `agent_instructions`.

## Reference contributions

| Editor | Path | Demonstrates |
|---|---|---|
| Notes | `Source/Modules/Celbridge.DocumentEditors/Editors/Notes/` | TipTap, spurious-update gating, toolbar dimming |
| Spreadsheet | `Source/Modules/Celbridge.Spreadsheet/Package/` | SpreadJS, command-manager gating, translucent overlay, secret injection |
| FileViewer | `Source/Modules/Celbridge.DocumentEditors/Editors/FileViewer/` | Explicit no-op |
| SceneViewer | `Source/Modules/Celbridge.DocumentEditors/Editors/SceneViewer/` | Explicit no-op |
| CodeEditor | `Source/Modules/Celbridge.DocumentEditors/Editors/CodeEditor/` | Monaco `readOnly`, toolbar gating |
