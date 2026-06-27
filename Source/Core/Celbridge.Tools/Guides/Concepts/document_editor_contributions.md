# Document editor contributions

A package contribution that takes over a file extension in the documents panel. The editor runs in a WebView2 and talks to the host through `https://shared.celbridge/celbridge-client/celbridge.js`. Read `packages_overview` first.

## Manifest

`packages/my-editor/package.toml`:

```toml
[package]
name = "my-editor"
title = "My Editor"

[contributes]
document_editors = ["my-editor.document.toml"]

[permissions]
tools = ["document.*", "file.*"]
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
    onRestoreState: (stateJson) => { /* apply snapshot */ }
});

// Writability is per-view host state, not a document handler — subscribe to it separately (see below).
client.viewState.onChanged((viewState) => {
    if (viewState.writable) { applyWritableState(viewState.writable); }
});
```

- **`onContent(content, metadata)`** — initial load. `content` is string or base64; `metadata.resourceKey` is the resource key. Framework calls `notifyContentLoaded()` for you. Do not save here; suppress framework update events (see trap).
- **`onRequestSave()`** — auto-save, tab close, programmatic flush. `await client.document.save(content)`. May fire while the tab is hidden.
- **`onExternalChange(args)`** — file changed on disk. `client.document.load()`, apply with the spurious-update guard, then `client.document.notifyContentLoaded(ContentLoadedReason.ExternalReload)`. Forward `args.preserveViewState`.
- **`onRequestState()` / `onRestoreState(stateJson)`** — opaque string round-trip for scroll, selection, pending view state. Survives external reloads and session restore. Return `null` if nothing to preserve.

## Edit verbs (optional)

The macOS Edit menu and the in-window menu route the standard verbs (copy, cut, paste, selectAll, undo, redo) to the focused editor. Wire two things to participate; skip both and the menu greys out for your editor and the shortcut falls through to your own key handling unchanged.

```javascript
// Run your editor's OWN command — never reimplement it. The outcome must equal the user
// pressing the shortcut while focused in the editor.
client.onNotification('input/editIntent', ({ intent }) => {
    runMyEditorCommand(intent); // intent: 'copy' | 'cut' | 'paste' | 'selectAll' | 'undo' | 'redo'
});

// Report what you can do whenever the selection changes, so the menu enables Copy/Cut only when
// there is a selection. Paste/selectAll/undo/redo are normally always offered.
function reportCapabilities() {
    client.input.notifyCapabilities({
        canCopy: hasSelection, canCut: hasSelection,
        canPaste: true, canSelectAll: true, canUndo: true, canRedo: true
    });
}
```

Precedent: `Source/Modules/Celbridge.DocumentEditors/Editors/CodeEditor/js/editor-controller.js` (`runEditIntent` + `#notifyEditCapabilities`).

## Writability rides `cel.viewState`

Writability is not a document handler — it is per-view host state on the `cel.viewState` store, alongside any other state the host replicates per view. Subscribe with `client.viewState.onChanged(viewState => ...)` and read `viewState.writable`. The host seeds the value before the view connects, so a handler registered at startup (after your editor surface exists) receives the current value before content is applied, and again whenever it changes mid-session. `viewState.writable` is one of:

- `"Writable"` — accept edits.
- `"Locked"` — `[resources].lock` pattern match.
- `"ReadOnlyAttribute"` — OS read-only bit set.
- `"ReadOnlyRoot"` — non-writable resource root.

Treat **anything other than `"Writable"`** as read-only. Same representation for all three non-writable states.

Read-only-by-design editors simply do not subscribe to `cel.viewState` — there is no writable state to apply, so there is nothing to register. Precedent: `Source/Modules/Celbridge.DocumentEditors/Editors/FileViewer/js/file-viewer.js`.

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
| Presentation-only viewer | Does not subscribe to `cel.viewState` |

## Cross-references

- **Localization** — `t('MyEditor_Editor_Name')` after `await client.initialize()`; strings live in `localization/<locale>.json` next to `index.html`.
- **Secrets** — bundled-package descriptors can inject `client.secrets.<name>`. Non-bundled packages see an empty map.
- **`[permissions] tools`** — every `cel.*` call must be declared under `[permissions].tools` in alias form (`"document.save"`). See `agent_instructions`.

## Reference contributions

| Editor | Path | Demonstrates |
|---|---|---|
| Notes | `Source/Modules/Celbridge.DocumentEditors/Editors/Notes/` | TipTap, spurious-update gating, toolbar dimming |
| Spreadsheet | `Source/Modules/Celbridge.Spreadsheet/Package/` | SpreadJS, command-manager gating, translucent overlay, secret injection |
| FileViewer | `Source/Modules/Celbridge.DocumentEditors/Editors/FileViewer/` | Explicit no-op |
| SceneViewer | `Source/Modules/Celbridge.DocumentEditors/Editors/SceneViewer/` | Explicit no-op |
| CodeEditor | `Source/Modules/Celbridge.DocumentEditors/Editors/CodeEditor/` | Monaco `readOnly`, toolbar gating |
