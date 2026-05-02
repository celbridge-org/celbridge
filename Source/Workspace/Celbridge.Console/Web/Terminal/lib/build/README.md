# Vendored xterm.js — Build Tooling

This directory contains the build tooling used to refresh the vendored xterm.js
dist files in the parent `lib/` folder.

The entire `build/` directory is excluded from the application build via the
`.csproj` — only the copied dist files in `lib/` ship with the app.

## Updating xterm.js

1. `cd` into this directory (`Web/Terminal/lib/build/`)
2. Edit `package.json` and change the version numbers
3. Run `npm install` to fetch the new packages
4. Run `npm run vendor` to copy the dist files into `lib/`
5. Commit the updated files in `lib/`

## Architecture

`vendor.mjs` is a thin copy script. xterm.js ships pre-built dist files on
npm so no bundling is required — the script just copies `xterm.js`,
`xterm.css`, and the addon JS files from `node_modules/@xterm/*/` into the
parent `lib/`. Source maps are copied alongside so DevTools can resolve them.
