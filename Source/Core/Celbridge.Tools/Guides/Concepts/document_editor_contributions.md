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

Which editor opens a file resolves in order: the per-file sidecar override, the `[celbridge].editor-associations` map (longest matching extension suffix), the first supporting contribution in discovery order, then the built-in editors in their pinned order. The sidecar override records only a deviation from that default: choosing the default in the Open With picker clears it. Celbridge's own file types (the `.celbridge` project file, `package.toml`, `*.editor.toml`) are reserved: they resolve to their own editor ahead of any contribution claiming the same extension, and neither an association nor a sidecar can point them elsewhere. See `project_structure` for the full `.celbridge` schema.

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

It defines design tokens as CSS custom properties. Both it and the native app's theme dictionaries are generated from one source (`Source/Core/Celbridge.DesignTokens/DesignTokens.json`), so a token holds the same value on either side. Theme switching is automatic: the client mirrors the host theme onto `html[data-theme]` on every state snapshot, so tokens re-resolve with no editor JS — do not subscribe to `appState.theme` to swap a stylesheet or toggle a class. Build surfaces from the tokens, or key your own rules on `[data-theme="dark"]` for anything a token does not cover.

An editor that hand-styles its own chrome (the CodeEditor is the precedent) can link `/assets/celbridge-client/celbridge-tokens.css` instead — the same tokens with none of the bare-element control rules — so it gets the host palette without the generic button/input styling leaking into its surface. It can still take a shared component whole, because each one is also served on its own under `/assets/celbridge-client/ui/`: the CodeEditor links `ui/splitter.css` for the divider between its editor and preview panes and styles the rest by hand.

Core tokens:

| Token | Purpose |
|---|---|
| `--cel-font-ui` | UI and prose text. A system font stack, matching the host chrome per platform. |
| `--cel-font-mono` | Code and monospace text. The bundled Cascadia Mono, consistent across platforms. |
| `--cel-font-size-small`, `--cel-font-size-base`, `--cel-font-size-heading` | The type scale. Small for panel titles, field labels and supporting prose; base for input, button and body text; heading for a section heading. |
| `--cel-font-weight-regular`, `--cel-font-weight-strong` | Weight, which is what separates a label from prose at the same size. |
| `--cel-content-bg`, `--cel-chrome-bg` | The two background roles. Content is a canvas a document is edited or viewed in; chrome is the surface everything else sits on, including the channels between panels. An editor is a document surface, so its page takes `--cel-content-bg`. |
| `--cel-text-primary`, `--cel-text-secondary` | Primary and muted foreground text. |
| `--cel-divider` | Separator and control-border color, for a hairline within a surface. |
| `--cel-panel-edge` | The outline of a region carved out of the chrome surface. Stronger than a divider, so the carve reads as an object. |
| `--cel-accent` | Accent color (hardcoded per theme; the CSS `AccentColor` keyword renders transparent in WebView2). |
| `--cel-error-text`, `--cel-warning-text`, `--cel-search-highlight` | Semantic status colors. |
| `--cel-radius-control`, `--cel-radius-button`, `--cel-radius-card` | Corner radii for controls, flat icon buttons, and larger cards. |
| `--cel-page-zoom` | The scale the web engine applied on top of the host's own, carrying the Windows accessibility text size. See below. |

Most editors can ignore `--cel-page-zoom`. On Windows the web engine folds the accessibility text size into the scale it renders at, so a CSS pixel in a WebView is larger than a device-independent pixel in the native chrome, and a box declared at 40px lands taller than the 40px XAML panel header beside it. Text is unaffected, because both stacks grow it by the same factor, and so is any box sized by its content. Only a fixed pixel dimension diverges. Where one has to line up with the native chrome, divide it: `height: calc(40px / var(--cel-page-zoom, 1))`. The tokens that mirror a native dimension already do this. A plain `40px` elsewhere is fine and stays consistent with everything else on the page.

The stylesheet also imports the Cascadia Mono face and applies the UI font, base text color, and content background to `body`. It gives common form controls — `<button>`, `<select>`, `<textarea>`, text `<input>`, checkboxes/radios, and range sliders — an approximate native Fluent look with no markup beyond the plain element; add `class="cel-accent"` to a button for the filled accent (primary) variant. Text-level elements are themed too: `<a>` links take the accent color, `<code>`/`<pre>`/`<kbd>` use the mono font, and placeholders, `::selection`, and `<hr>` follow the theme. These are bare-element rules with the lowest specificity, so an editor overrides any of them by id or class. Larger components (tables, dialogs, cards) are intentionally not pre-styled — build them from the tokens. Icons are opt-in: link `/assets/bootstrap-icons/bootstrap-icons.css` and use the `bi` classes (the same icon font the native chrome uses).

Shared components sit above the bare-element rules, each mirroring a native control so a WebView surface reads as a peer of the XAML panels beside it: `.cel-expander` (a collapsible card), `.cel-splitter` (a draggable divider, driven by `attachSplitter()` from `ui/splitter.js`), `.field` (a settings form row, covered below), and the section switcher below. `celbridge.css` links all of them, and each is also `ui/<component>.css` on its own, importing whatever its component is built on so that one link is enough. What a sheet on its own cannot carry is the native look of a form control inside the component, because that look is the bare-element rules themselves. The console editor's settings surface is the reference for the rest — a real settings panel with real settings in it, built from these components and nothing else.

### The settings surface

A panel whose content divides into sections uses `.cel-section-switcher`, mirroring the native `SettingsSectionSwitcher`: a nav of named section rows beside the selected section, which is carved out of the surface behind it and headed by its name and description. This is the standard across utility panels and document inspectors — do not build a section hierarchy out of stacked collapsible cards, which hides the panel's shape and makes a deep panel a scroll hunt.

```html
<div class="cel-section-switcher">
  <div class="cel-section-nav" role="tablist">
    <button class="cel-section-nav-item" type="button" role="tab" data-section="session"
            aria-selected="true" title="Session" aria-label="Session">
      <span class="cel-section-nav-icon"><i class="bi bi-terminal"></i></span>
      <span class="cel-section-nav-label">Session</span>
    </button>
    <!-- ... one row per section ... -->
  </div>
  <div class="cel-section-area">
    <div class="cel-section-header" data-section="session"><h1>Session</h1><p>What this section covers.</p></div>
    <!-- ... one header per section, all but the selected one hidden ... -->
    <div class="cel-section-notice" hidden></div>
    <div class="cel-section-content">
      <section data-section="session"><!-- ... --></section>
      <!-- ... one section per row ... -->
    </div>
  </div>
  <div class="cel-section-footer"><!-- an action belonging to the whole surface --></div>
</div>
```

Drive it with `attachSectionSwitcher()` from `ui/section-switcher.js`, which owns the selection, which header and section are showing, `aria-selected`, the roving tabindex, the arrow keys, each section's scroll position, and which layout the surface is in:

```javascript
import { attachSectionSwitcher } from '/assets/celbridge-client/ui/section-switcher.js';

const switcher = attachSectionSwitcher(document.getElementById('switcher'));

switcher.select('environment');   // e.g. when restoring view state
switcher.selected();              // persist this in onRequestState(), with switcher.scrollTop()
switcher.setNotice(message);      // show the notice slot, or hide it with an empty message
switcher.setReadOnly(true);       // disable every control inside the sections
```

The surface around the switcher supplies its height and its margin, the way the native consumers set `Margin="12"` and fill their panel.

**Two layouts.** The switcher picks one from its own width and writes it to `data-layout` on the root. `inline` is the native layout: a labelled nav column beside the carved section. `stacked` puts the nav above the content as an icon strip, over a section that drops its border, and pins the footer below the content instead. The markup is the same in both — a row carries a glyph and a label, and `stacked` drops the label to the tooltip the row needs either way — so a panel written once survives being docked into a document tab and undocked back into a 300px utility panel. Write `data-layout="inline"` or `data-layout="stacked"` on the root to hold one of them.

**The slots.** `.cel-section-notice` is pinned below the header and above the scrolling content, so what it says stays visible from whichever section is showing: a file that would not parse, a setting that cannot be honoured. `.cel-section-footer` carries an action belonging to the whole surface rather than to one section. It sits outside the sections and is deliberately not disabled by `setReadOnly()`, because a footer action operates on the surface, not on the file — which is what keeps the console's Reopen Console available on a read-only document.

Add a `<span class="cel-section-nav-pip">` inside a row's `.cel-section-nav-icon` to flag that its section needs attention. Give a section its own row, header and section element, all carrying the same `data-section` id; a panel whose section set is not known ahead of time generates all three before attaching. The rows are named sections rather than icon tabs, so name them the way a settings category is named.

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

Rail buttons are icon-only with a tooltip, so a button whose action has no natural glyph still needs a fallback icon rather than a text label. Add `selected` to the button whose surface is showing; the accent fills that button for as long as the surface is open. This deliberately differs from the workspace utility rail, whose selected button drops to a neutral fill while its panel is unfocused — with several rails and panels on screen at once, "this panel is open" is the more useful signal, and an editor's own content usually holds focus anyway. `.cel-rail-right` moves the rail's border to the window edge; omit it for a rail on the left.

The console editor is the reference: its shortcut buttons render into the rail below the settings toggle, and the toggle opens a `.cel-section-switcher` surface. A rail action switches away from that surface: pressing a console shortcut closes the settings and types into the terminal, because the action belongs to the content the settings configure.

### Editing a list of entries

A setting that is a list of records — the console's script runners, triggers and shortcuts — is edited as one `.cel-expander` card per entry, matching how the native Packages and Pages panels render their lists. The card header carries just the entry's name; its fields are one expand away, so the header stays scannable. Add belongs on the list, below it. Delete belongs in the card header but only shows while that card is open, which keeps a collapsed list free of destructive controls and means an entry cannot be deleted without its contents having been on screen first — worth having while there is no undo behind it. A newly added card opens on add, so deleting one added by mistake still costs a single click.

Lists reorder by dragging a `.cel-card-grip` handle in the header, with Alt+Up / Alt+Down on the focused header as the keyboard equivalent. This is on by default, and worth leaving on even where the order looks cosmetic: a card list is a list the user curates, its file order is what gets persisted, and one list that reorders next to another that does not reads as a bug. Order is also load-bearing more often than it first appears — the console resolves a file's runner by taking the first one whose extensions match. Do not use move-up / move-down buttons: chevrons read as a second expander control, and clicking one while a card is expanded jumps the layout out from under the user. A drag runs only against a collapsed list. Pressing the handle while any card is open therefore collapses the list and starts no drag; the user presses again to actually drag, and the cards stay collapsed afterwards. The extra press buys an exact grab: collapsing as part of the grab would shorten everything above the grabbed row and slide it out from under the pointer, by as much as a few hundred pixels, and no amount of scroll compensation reliably gets it back — the panel is often already at the top of its scroll range. Splitting the two makes the collapse a visible, understandable step rather than a mysterious offset.

The list then reorders live under the pointer, and Escape (or the browser cancelling the pointer) restores the order it started in.

Collapsing is what makes the live reorder tractable: uniform rows give the list a single row pitch, so the target slot can come from how far the pointer has travelled in whole pitches rather than from hit-testing the rows themselves. That one-way dependency is the important part — a reorder driven by the rows' live positions feeds its own output back into its next input, and the list oscillates as the row you just moved lands back under the cursor. Report the change once on release, not per slot, or every pixel of drag marks the document dirty and re-renders whatever the order drives.

Displaced rows slide to their new positions with a FLIP transition (measure, reorder, invert with a transform, let the transition carry it away), gated on `prefers-reduced-motion`. The before-positions are measured live, so a row interrupted mid-slide continues from where it visually is. The dragged row is excluded from both the slide and its transition, and instead carries a transform of the pointer delta minus the pitches its slot has already absorbed — so it stays under the pointer while the rows snap around it, and is stacked above them since it spends most of a drag straddling two. Because the slot rounds to the nearest pitch, that leftover never exceeds half a row, which is all the card has to travel when it settles on release.

All of this is decoration layered on a gesture that never reads the rows, so no transform in flight can influence where the next slot lands.

`ui/card-list.js` in the shared client implements this as `createCardList()`, and `ui/card-list.css` styles it. The list element takes `.cel-card-list`; the card template supplies `.cel-card-grip`, `.cel-card-icon`, `.cel-card-title`, `.cel-card-actions` and `.cel-card-delete`, of which the grip and the delete button are the two the module drives.

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

## Telling the user something

`client.dialog.toast(severity, message)` shows the workspace toast — the same single-line notification the host uses for a project load or a failed batch operation.

```javascript
await client.dialog.toast('warning', t('MyEditor_ConvertedWithWarnings', failed.length));
```

`severity` is `'info'`, `'warning'` or `'error'`. Anything else is rejected rather than downgraded, so a typo surfaces as an error instead of quietly showing your failure as information. `message` is one line you have already localized; only its first line is shown.

A third argument gives the toast a button that opens a document:

```javascript
await client.dialog.toast('error', t('MyEditor_ConfigSyntaxError'), {
    resource: 'project:config.json',
    label: t('MyEditor_OpenConfig'),
    line: 42
});
```

`resource` is any document, not only a report — `line` and `column` are one-based and land the reader on a spot in it, so a single problem with a known position does not need a report written just to be navigable. `label` is your own localized text; omit it and the button uses the host's wording for opening a report. Omit the whole argument and the toast carries no button.

**It resolves when the host has taken the toast, not when the user has seen it.** One notification is on screen at a time, a newer one replaces the current one, and anything below an error is dropped while an error is still showing. Nothing auto-dismisses. Treat the call as best effort and never as an acknowledgement.

This sits under `dialog` alongside `alert`, but it is the opposite kind of call: `alert` blocks until the user answers, `toast` tells them and returns. Note that it is unrelated to `notifyChanged`, `notifyContentLoaded` and the other `notify*` calls, which are protocol messages to the host rather than anything the user sees. Reach for `alert` only when the user genuinely cannot continue without responding.

Use it for an outcome the user should know about but did not ask a question about — a conversion that finished with failures, a long operation that completed. **One operation raises one toast**, whatever it found: a loop that toasts per item will have every line but the last replaced before anyone reads it. When there is per-item detail worth reading, say it once here and write the detail as a report.

## Reporting per-item detail

`client.document.writeReport(report)` writes a `.report` document into the project and returns the resource key it opens by. Hand that key to `dialog.toast` as its action resource and the toast gains a button that opens it.

```javascript
const resource = await client.document.writeReport({
    id: 'acme-tiles-convert',
    title: 'Convert Tilesets',
    severity: 'warning',
    summary: '9 of 40 tilesets could not be converted.',
    sections: [{
        title: 'Tilesets',
        kind: 'findings',
        severity: 'warning',
        items: failed.map(entry => ({
            severity: 'warning',
            message: 'Could not be converted.',
            resource: entry.resource,
            detail: entry.reason,
            actions: [{ kind: 'openResource', label: 'Open', resource: entry.resource }]
        }))
    }]
});

await client.dialog.toast('warning', '9 of 40 tilesets could not be converted', { resource });
```

**Write one when there is per-item detail worth reading beyond the notification line** — more than one item, or one item whose reason will not fit a line. A single failure fully described by its notification does not need a report, and a reports folder churning with one-row documents devalues the ones that matter.

### The id names the kind, not the run

The current report of an id sits at `{id}.report` and writing a new one moves the previous into `history/`, where it is kept for a week. So `id` is stable across runs — `acme-tiles-convert`, not `convert-2026-08-18`. That keeps re-running from opening a new tab each time, and lets the reader compare against the last few.

It must be lowercase letters, digits, hyphens and dots. **Nothing stops you colliding with another package or with the host, so qualify it with your package name.** The host's own ids are `project-load`, `check-references`, `copy-resources`, `move-resources`, `delete-resources`; taking one of those replaces the report the user's project health button opens.

Set `generatedAt` yourself (an ISO 8601 UTC stamp) if one operation writes its report more than once as it progresses — the same stamp means one report being revised rather than several superseding each other. Omit it and the host stamps the write.

### Sections and items

A section declares its `kind`. `facts` sections are labelled readings — `message` is the label and `value` the reading — and their items carry no code. `findings` sections are things needing attention, and the editor groups their items by message into a table whose columns come from what the rows actually carry.

`detail` is **per-occurrence only**: the parse error, the rejected value, the reason this item failed. Anything true of every item belongs in the `message`, which is stated once as the group heading. A constant `detail` repeated on every row is the common mistake — it says nothing the message did not, and becomes the same paragraph printed a hundred times.

`openResource` is the only action kind, and a report can be shared or committed, so there is deliberately no way to name a URL or a command to run. Give an item an action naming its own `resource` and the editor turns the resource itself into the link; add `location: { line, column }` when the finding has a position.

Report text is resolved by you and written into the file, exactly as notification text is. A report is a JSON file that can be read outside Celbridge, so it holds words rather than localization keys.

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
