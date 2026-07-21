# Document editor contributions

A package contributes editors; a project instantiates them. A document editor edits files matching its declared file types in the documents panel. The editor runs in a WebView and talks to the host through the shared client at `/assets/celbridge-client/celbridge.js`, addressed root-relative against the page's own loopback origin. Read `packages_overview` first.

## Manifest

`packages/my-editor/package.toml`:

```toml
[package]
name = "my-editor"
title = "My Editor"

[contributes]
editors = ["my-editor.editor.toml"]

[permissions]
tools = ["document.*", "file.*"]
```

`packages/my-editor/my-editor.editor.toml`:

```toml
[editor]
id = "my-editor"
type = "document"
entry-point = "index.html"
display-name = "MyEditor_Editor_Name"
description = "MyEditor_Editor_Description"

[[file-types]]
extension = ".myext"
display-name = "MyEditor_FileType_MyExt"

[[templates]]
id = "empty"
display-name = "MyEditor_Template_Empty"
template-file = "templates/empty.myext"
default = true
```

`type` is `"document"` (edits matching files, shown in document tabs) or `"utility"` (a workspace fixture; see `utility_documents`). A document editor requires at least one `[[file-types]]` entry and must not declare a `[utility]` section. `display-name` names the editor for what it is (e.g. `Markdown Editor`) while the package `title` names the product — keep them distinct so the two do not read identically in Project Settings. The optional `description` is a short sentence shown as the editor's tooltip. `display-name` and `description` values are localization keys. Templates are optional. All Celbridge-owned manifest keys are kebab-case.

## Activation and configuration

A discovered package is active by default — bundling it, or dropping it into the project's `packages/` folder, is enough for its editors to open matching files. There is no activation list to opt in to. A project only touches the `.celbridge` file to *deviate* from an editor's defaults: a `[[contribution]]` entry sets the editor's config keys, or flips its activation when the manifest marks the contribution `recommended` (add `disabled = true`) or `optional` (add `enabled = true`):

```toml
[[contribution]]
package      = "my-editor"
contribution = "my-editor"
grid-size    = 16              # a config key declared by the editor's [[config]] descriptors
```

To turn a whole package off, list it in `[celbridge].disabled-packages`. Each contribution has exactly one instance, referenced as `package.contribution`; a project cannot declare several instances or override an editor's display name, icon, or description.

Which editor opens a file resolves in order: the per-file sidecar override, the `[celbridge].editor-associations` map (longest matching extension suffix), the first supporting contribution in discovery order, then the built-in editors in host order. The sidecar override records only a deviation from that default: choosing the default in the Open With picker clears it. See `project_structure` for the full `.celbridge` schema.

## Config descriptors (optional)

An editor declares its per-contribution configuration surface as typed `[[config]]` descriptors:

```toml
[[config]]
key          = "grid-size"
type         = "number"
default      = 16
display-name = "MyEditor_Config_GridSize"
```

Types are `bool`, `string`, `number`, `enum` (with `values`), and `string-list`. Instance tables set these keys; the host type-checks them against the descriptors and delivers the merged config to the editor on the `celbridge.options` channel (manifest `[options]`, overlaid with descriptor defaults, overlaid with the instance's keys). Descriptor keys must not collide with the reserved deviation-entry keys (`package`, `contribution`, `disabled`, `enabled`).

## JS handlers

```javascript
import celbridge from '/assets/celbridge-client/celbridge.js';
import { ContentLoadedReason } from '/assets/celbridge-client/api/document-api.js';

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

## Styling

Link the shared stylesheet to inherit the host's fonts and colors, so a WebView editor reads as part of the native app rather than a foreign web page:

```html
<link rel="stylesheet" href="/assets/celbridge-client/celbridge.css">
```

It defines design tokens as CSS custom properties whose color values mirror the native app's theme (`Celbridge.UserInterface/Resources/Colors.xaml`). Theme switching is automatic: the client mirrors the host theme onto `html[data-theme]` on every state snapshot, so tokens re-resolve with no editor JS — do not subscribe to `appState.theme` to swap a stylesheet or toggle a class. Build surfaces from the tokens, or key your own rules on `[data-theme="dark"]` for anything a token does not cover.

An editor that hand-styles its own chrome (the CodeEditor is the precedent) can link `/assets/celbridge-client/celbridge-tokens.css` instead — the same tokens with none of the bare-element control rules — so it gets the host palette without the generic button/input styling leaking into its surface.

Core tokens:

| Token | Purpose |
|---|---|
| `--cel-font-ui` | UI and prose text. A system font stack, matching the host chrome per platform. |
| `--cel-font-mono` | Code and monospace text. The bundled Cascadia Mono, consistent across platforms. |
| `--cel-app-bg`, `--cel-panel-bg`, `--cel-panel-bg-alt` | Window, panel, and inner-content backgrounds. |
| `--cel-text-primary`, `--cel-text-secondary` | Primary and muted foreground text. |
| `--cel-divider` | Separator and control-border color. |
| `--cel-accent` | Accent color (hardcoded per theme; the CSS `AccentColor` keyword renders transparent in WebView2). |
| `--cel-error-text`, `--cel-warning-text`, `--cel-search-highlight` | Semantic status colors. |
| `--cel-radius-control`, `--cel-radius-card` | Corner radii for controls and larger cards. |

The stylesheet also imports the Cascadia Mono face and applies the UI font, base text color, and window background to `body`. It gives common form controls — `<button>`, `<select>`, `<textarea>`, text `<input>`, checkboxes/radios, and range sliders — an approximate native Fluent look with no markup beyond the plain element; add `class="cel-accent"` to a button for the filled accent (primary) variant. Text-level elements are themed too: `<a>` links take the accent color, `<code>`/`<pre>`/`<kbd>` use the mono font, and placeholders, `::selection`, and `<hr>` follow the theme. These are bare-element rules with the lowest specificity, so an editor overrides any of them by id or class. Larger components (tables, dialogs, cards) are intentionally not pre-styled — build them from the tokens. Icons are opt-in: link `/assets/bootstrap-icons/bootstrap-icons.css` and use the `bi` classes (the same icon font the native chrome uses).

The Utility Demo utility is the minimal reference for consuming these tokens — the UI font, host-styled controls, and a bordered input.

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
