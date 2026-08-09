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

Each `[[file-types]]` entry names its extension with `extension`, or claims a set from the host's file type catalog with `from-catalog = "languages"` — every extension the catalog assigns a language to. The two keys are mutually exclusive, and `"languages"` is the only supported set; it exists for a general code editor that follows the catalog rather than listing two hundred extensions. The catalog (`file-types.json`, served to editors at `/assets/celbridge-client/file-types.json`) is the host's single source for established file types: the categories an extension is grouped under, the language a code editor highlights it as, and the name the type is known by. A package declares those properties in its manifest only for its own novel extensions.

A `[[file-types]]` entry may also set `icon` (an icon name), `icon-color` (a hex colour such as `#FF8800`), and `icon-scale` (a multiplier such as `1.3` for a glyph its font draws small), which replace the default icon wherever a file of that type is drawn — the resource tree, search results, and the resource picker. Every icon name is `<font>-<name>`, where the prefix selects the icon font: `bs-` is Bootstrap Icons, so `bs-journal-text` is Bootstrap's `journal-text`. The prefix is required — an unprefixed name does not resolve — and it is what keeps names unique as further fonts are bundled. `icon-color` and `icon-scale` each require `icon`; on their own they are a load error, because they would silently do nothing. The host normalises every icon colour into a legible, consistent-prominence band for the active theme, so a declared colour that is very dark or very bright is adjusted for readability rather than shown exactly. An icon naming an unknown font or glyph, or carrying a malformed colour, is dropped with a warning naming the manifest and the offending value, and the file type falls back to the default file icon; it never fails package load. The host catalog accepts the same two keys, and its entry wins for an established type, so a package's icon covers the extensions it introduces rather than repainting standard formats project-wide.

## Activation and configuration

A discovered package is active by default — bundling it, or dropping it into the project's `packages/` folder, is enough for its editors to open matching files. There is no activation list to opt in to. A project only touches the `.celbridge` file to *deviate* from an editor's defaults: a `[[contribution]]` entry sets the editor's config keys, or flips its activation when the manifest marks the contribution `recommended` (add `disabled = true`) or `optional` (add `enabled = true`):

```toml
[[contribution]]
package      = "my-editor"
contribution = "my-editor"
grid-size    = 16              # a config key declared by the editor's [[config]] descriptors
```

To turn a whole package off, list it in `[celbridge].disabled-packages`. A contribution is referenced as `package.contribution`; a project cannot declare several copies of one contribution, nor override an editor's display name, icon, or description.

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

Types are `bool`, `string`, `number`, `enum` (with `values`), and `string-list`. Contribution tables set these keys; the host type-checks them against the descriptors and delivers the merged config to the editor on the `celbridge.options` channel (manifest `[options]`, overlaid with descriptor defaults, overlaid with the contribution's keys). Descriptor keys must not collide with the reserved deviation-entry keys (`package`, `contribution`, `disabled`, `enabled`).

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
| `--cel-font-size-small`, `--cel-font-size-base`, `--cel-font-size-heading` | The type scale. Small for panel titles, field labels and supporting prose; base for input, button and body text; heading for a section heading. |
| `--cel-font-weight-regular`, `--cel-font-weight-strong` | Weight, which is what separates a label from prose at the same size. |
| `--cel-app-bg`, `--cel-panel-bg`, `--cel-panel-bg-alt` | Window, panel, and inner-content backgrounds. |
| `--cel-text-primary`, `--cel-text-secondary` | Primary and muted foreground text. |
| `--cel-divider` | Separator and control-border color. |
| `--cel-accent` | Accent color (hardcoded per theme; the CSS `AccentColor` keyword renders transparent in WebView2). |
| `--cel-error-text`, `--cel-warning-text`, `--cel-search-highlight` | Semantic status colors. |
| `--cel-radius-control`, `--cel-radius-card` | Corner radii for controls and larger cards. |

The stylesheet also imports the Cascadia Mono face and applies the UI font, base text color, and window background to `body`. It gives common form controls — `<button>`, `<select>`, `<textarea>`, text `<input>`, checkboxes/radios, and range sliders — an approximate native Fluent look with no markup beyond the plain element; add `class="cel-accent"` to a button for the filled accent (primary) variant. Text-level elements are themed too: `<a>` links take the accent color, `<code>`/`<pre>`/`<kbd>` use the mono font, and placeholders, `::selection`, and `<hr>` follow the theme. These are bare-element rules with the lowest specificity, so an editor overrides any of them by id or class. Larger components (tables, dialogs, cards) are intentionally not pre-styled — build them from the tokens. Icons are opt-in: link `/assets/bootstrap-icons/bootstrap-icons.css` and use the `bi` classes (the same icon font the native chrome uses).

Shared components sit above the bare-element rules, each mirroring a native control so a WebView surface reads as a peer of the XAML panels beside it: `.cel-expander` (a collapsible card), `.cel-splitter` (a draggable divider, driven by `attachSplitter()` from `ui/splitter.js`), `.cel-panel-footer` (a pinned caption-and-action row), `.field` (a settings form row, covered below), and the two navigation components below. The Utility Demo utility is the reference for all of them — the UI font, host-styled controls, a bordered input, and each shared component in use.

### Panel navigation

A panel whose content divides into sections uses `.cel-nav-tabs`: a strip of icon tabs over an accent underline, above the active section's name and description. This is the standard across utility panels and document inspectors — do not build a section hierarchy out of stacked collapsible cards, which hides the panel's shape and makes a deep panel a scroll hunt.

```html
<div class="cel-nav-tabs">
  <div id="tabs" class="cel-nav-tab-strip" role="tablist">
    <button class="cel-nav-tab" type="button" role="tab" data-section="session" aria-selected="true" title="Session">
      <span class="cel-nav-tab-icon"><i class="bi bi-terminal"></i></span>
    </button>
    <!-- ... one button per section ... -->
  </div>
  <div class="cel-section-header" data-section="session"><h1>Session</h1><p>What this section covers.</p></div>
  <!-- ... one header per section, all but the active one hidden ... -->
</div>
```

Drive it with `attachNavTabs()` from `ui/nav-tabs.js`, which owns `aria-selected`, the roving tabindex, and arrow-key navigation, and reports the selected section id:

```javascript
import { attachNavTabs } from '/assets/celbridge-client/ui/nav-tabs.js';

const tabs = attachNavTabs(document.getElementById('tabs'), {
    onChange(sectionId) { /* show that section */ },
});
tabs.select('environment');   // e.g. when restoring view state
tabs.selected();              // persist this in onRequestState()
```

Each tab is an icon with a tooltip, not a text label, so the strip stays a fixed width however many sections a panel has; the section name below it is what names the selection. Add a `<span class="cel-nav-tab-pip">` inside a tab's icon to flag that its section needs attention.

### Settings form fields

A settings row is a `.field`: the label naming the control, the control itself, and an optional hint or warning below it.

```html
<label class="field">
  <span class="field-label">Working directory</span>
  <input type="text" placeholder="Relative to the project folder">
  <span class="field-hint">A relative path resolves against the project folder.</span>
</label>
```

The label carries the strong weight and the hint the secondary colour, so a form scans by its labels. Use `.field-warning` for a setting the panel will not be able to honour.

### Field help

Three things can explain a settings field, and each has its own job:

- **Label** — what the setting is.
- **Placeholder** — the shape of the value: "Comma-separated file extensions", "Shown as the button tooltip".
- **Inline hint** (`.field-hint`) — syntax, a format constraint, or an example the user has to reproduce, and nothing else. A hint that restates the label or the placeholder is the common failure; delete it.

A field whose label and placeholder already say everything gets no hint. That is why the console's shortcut Label and Command carry none while its Icon does: `bs-play-fill` is a naming scheme nobody guesses.

The native XAML settings panels use tooltips for this instead, and an editor panel should not copy them. Their fields hold values you read once, where a tooltip is fine. An editor's settings tend to hold syntax you type, and a tooltip disappears the moment the pointer moves to the field it describes. Keep tooltips for background nobody needs in hand while typing.

### Inspector rail

An editor that needs a side panel puts it behind `.cel-rail`, a vertical icon rail down the right edge of its content, mirroring the workspace utility rail. The settings button sits at the top, the editor's own action buttons below it, separated by a `.cel-rail-separator`; the settings button uses `bi-sliders`, the same glyph as the workspace Project Settings button, and toggles the panel.

```html
<div class="cel-rail cel-rail-right">
  <button class="cel-rail-button selected" type="button" title="Settings">
    <i class="bi bi-sliders"></i><span class="cel-rail-pip"></span>
  </button>
  <div class="cel-rail-separator"></div>
  <button class="cel-rail-button" type="button" title="Run tests"><i class="bi bi-play-fill"></i></button>
</div>
```

Settings goes above the action buttons, inverting the workspace rail's ordering, because an editor's action buttons are usually configured from the panel that settings button opens — below them, its position would shift every time the user adds or removes one. Above them it is fixed, and a growing action group overflows downward into empty space instead. The separator is what carries the family resemblance to the workspace rail, not the ordering.

Rail buttons are icon-only with a tooltip, so a button whose action has no natural glyph still needs a fallback icon rather than a text label. Add `selected` to the button whose surface is showing; its accent edge capsule stays lit for as long as that surface is open. This deliberately differs from the workspace utility rail, whose capsule dims while its panel is unfocused — with several rails and panels on screen at once, "this panel is open" is the more useful signal, and an editor's own content usually holds focus anyway. `.cel-rail-right` moves the rail's border and its capsules to the window edge; omit it for a rail on the left.

The console editor is the reference: its shortcut buttons render into the rail below the settings toggle, and its settings panel is a `.cel-nav-tabs` hierarchy.

### Editing a list of entries

A setting that is a list of records — the console's script runners, triggers and shortcuts — is edited as one `.cel-expander` card per entry, matching how the native Packages and Pages panels render their lists. The card header carries just the entry's name; its fields are one expand away, so the header stays scannable. Add belongs on the list, below it. Delete belongs in the card header but only shows while that card is open, which keeps a collapsed list free of destructive controls and means an entry cannot be deleted without its contents having been on screen first — worth having while there is no undo behind it. A newly added card opens on add, so deleting one added by mistake still costs a single click.

Lists reorder by dragging a `.cel-card-grip` handle in the header, with Alt+Up / Alt+Down on the focused header as the keyboard equivalent. This is on by default, and worth leaving on even where the order looks cosmetic: a card list is a list the user curates, its file order is what gets persisted, and one list that reorders next to another that does not reads as a bug. Order is also load-bearing more often than it first appears — the console resolves a file's runner by taking the first one whose extensions match. Do not use move-up / move-down buttons: chevrons read as a second expander control, and clicking one while a card is expanded jumps the layout out from under the user. A drag runs only against a collapsed list. Pressing the handle while any card is open therefore collapses the list and starts no drag; the user presses again to actually drag, and the cards stay collapsed afterwards. The extra press buys an exact grab: collapsing as part of the grab would shorten everything above the grabbed row and slide it out from under the pointer, by as much as a few hundred pixels, and no amount of scroll compensation reliably gets it back — the panel is often already at the top of its scroll range. Splitting the two makes the collapse a visible, understandable step rather than a mysterious offset.

The list then reorders live under the pointer, and Escape (or the browser cancelling the pointer) restores the order it started in.

Collapsing is what makes the live reorder tractable: uniform rows give the list a single row pitch, so the target slot can come from how far the pointer has travelled in whole pitches rather than from hit-testing the rows themselves. That one-way dependency is the important part — a reorder driven by the rows' live positions feeds its own output back into its next input, and the list oscillates as the row you just moved lands back under the cursor. Report the change once on release, not per slot, or every pixel of drag marks the document dirty and re-renders whatever the order drives.

Displaced rows slide to their new positions with a FLIP transition (measure, reorder, invert with a transform, let the transition carry it away), gated on `prefers-reduced-motion`. The before-positions are measured live, so a row interrupted mid-slide continues from where it visually is. The dragged row is excluded from both the slide and its transition, and instead carries a transform of the pointer delta minus the pitches its slot has already absorbed — so it stays under the pointer while the rows snap around it, and is stacked above them since it spends most of a drag straddling two. Because the slot rounds to the nearest pitch, that leftover never exceeds half a row, which is all the card has to travel when it settles on release.

All of this is decoration layered on a gesture that never reads the rows, so no transform in flight can influence where the next slot lands.

`ui/card-list.js` in the shared client implements this as `createCardList()`, and `celbridge.css` styles it. The list element takes `.cel-card-list`; the card template supplies `.cel-card-grip`, `.cel-card-icon`, `.cel-card-title`, `.cel-card-actions` and `.cel-card-delete`, of which the grip and the delete button are the two the module drives.

```javascript
import { createCardList } from '/assets/celbridge-client/ui/card-list.js';

const list = createCardList({
    listElement, emptyElement, addButton, template,
    blankItem: () => ({ label: '', command: '' }),
    focusSelector: '.entry-label',
    reorderable: false,             // opt out of the grip handle and Alt+Up / Alt+Down
    fillCard(card, item) { },       // entry -> inputs
    readCard(card) { },             // inputs -> entry, or null to drop the card
    updateHeader(card) { },         // refresh the collapsed summary
    localize, onChanged, isWritable,
});
```

Prefer this over a delimited text field (`a | b | c` per line) for anything Celbridge-specific: it removes a syntax the user has to learn and a parser you have to maintain. The exception is a setting whose data has a canonical text form elsewhere — command-line arguments, `requirements.txt` entries, `KEY=value` environment pairs — where a plain textarea is the better control, because it lets the user paste from the file the data already lives in.

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
| CodeEditor | `Source/Modules/Celbridge.DocumentEditors/Editors/CodeEditor/` | Monaco `readOnly`, toolbar gating |
