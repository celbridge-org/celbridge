# Vendored marked.js — Build Tooling

This directory contains the build tooling used to produce `lib/marked.esm.js`,
the vendored marked bundle used by the Markdown preview.

The entire `build/` directory is excluded from the application build via
the `.csproj` — only `lib/marked.esm.js` ships with the app.

## Updating marked.js

1. `cd` into this directory (`Web/MarkdownPreview/lib/build/`)
2. Edit `package.json` and change the version number
3. Run `npm install` to fetch the new packages
4. Run `npm run vendor` to rebuild the bundle
5. Commit the updated `lib/marked.esm.js`

## Architecture

- `entry.js` re-exports `marked` and related utilities as named exports
- `vendor.js` uses esbuild to bundle `entry.js` into `lib/marked.esm.js`
