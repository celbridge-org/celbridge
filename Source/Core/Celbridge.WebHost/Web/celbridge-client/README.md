# Celbridge Client

JavaScript client for communicating with the Celbridge .NET host via JSON-RPC.

## Running Tests

To run the unit tests for the celbridge-client:

```bash
cd Core/Celbridge.WebHost/Web/celbridge-client
npm install
npm test
```

To run tests in watch mode during development:

```bash
npm run test:watch
```

## Project Structure

```
celbridge-client/
├── celbridge.js          # Main client entry point
├── localization.js       # Localization utilities for WebView editors
├── types.js              # JSDoc type definitions
├── celbridge.css         # Shared editor styles
├── celbridge-tokens.css  # Generated design tokens (see docs/development/design_tokens.md)
├── api/                  # API modules
│   ├── dialog-api.js     # Dialog operations
│   ├── document-api.js   # Document operations
│   ├── input-api.js      # Input events (keyboard, link clicks, scroll)
│   ├── localization-api.js
│   ├── log-api.js        # Host application log
│   ├── tools-api.js      # Host capability proxy (cel.*)
│   └── view-api.js       # Viewport trust: when a page may measure its own box
├── core/
│   ├── rpc-transport.js  # JSON-RPC 2.0 transport layer
│   ├── state-store.js    # Read-only mirrors of host app and per-view state
│   └── webview-tools-shim.js  # Classic script injected for the webview_* tools
├── ui/                   # Shared controls editors import by path, with their CSS
│   ├── card-list.js      # Editable list of expandable cards
│   ├── find-bar.js       # Find bar for backends with none of their own
│   ├── icon-field.js     # Icon name field with a picker
│   ├── section-switcher.js  # Settings surface: nav rail, sections, notice, footer
│   ├── splitter.js       # Draggable split between two panes
│   └── stack-layout.js   # Resolves an element between inline and stacked
├── localization/         # Client string resources, by locale
└── tests/                # Vitest suites
```

The `api/` classes are reached through the client instance (`client.view.canMeasure(...)`); the `ui/`
modules are imported by URL (`/assets/celbridge-client/ui/card-list.js`). Both sets are pinned by
`Source/Tests/Architecture/ClientApiSurfaceTests.cs`, because packages that call them live outside this
repository.
