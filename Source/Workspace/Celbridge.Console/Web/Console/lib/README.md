# Vendored xterm.js

xterm.js terminal emulator and addons (`@xterm/xterm` plus the fit, clipboard,
unicode11, and web-links addons) for the `.console` document editor.

- Upstream: https://github.com/xtermjs/xterm.js
- Pinned versions: see `build/package.json` (the source of truth).
- Licences:
  - `LICENSE` — xterm.js and its addons (MIT).
  - `LICENSE-js-base64` — js-base64 (BSD-3-Clause), which is bundled into `addon-clipboard.js`.

To refresh: `cd build && npm install && npm run vendor`
