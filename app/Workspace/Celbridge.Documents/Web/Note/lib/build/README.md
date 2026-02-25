# Vendored TipTap Dependencies — Build Tooling

This directory contains the build tooling used to produce `lib/tiptap.js`,
the vendored TipTap bundle used by the Note editor.

The entire `build/` directory is excluded from the application build via
the `.csproj` — only `lib/tiptap.js` ships with the app.

## Updating TipTap

1. `cd` into this directory (`Web/Note/lib/build/`)
2. Edit `package.json` and change the version numbers
3. Run `npm install` to fetch the new packages
4. Run `npm run vendor` to rebuild the bundle
5. Commit the updated `lib/tiptap.js`

## Architecture

- `entry.js` re-exports `Editor`, `StarterKit`, and all extensions as
  named exports
- `vendor.js` uses esbuild to bundle `entry.js` into `lib/tiptap.js`
- A single bundle guarantees one ProseMirror instance, avoiding duplicate
  plugin key errors that occur when multiple bundles each include their
  own copy of ProseMirror internals
