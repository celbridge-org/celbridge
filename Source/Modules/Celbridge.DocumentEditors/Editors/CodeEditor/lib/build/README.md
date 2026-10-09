# Vendored Deno Type Declarations — Build Tooling

This directory contains the build tooling used to produce `lib/deno.d.ts`, the
Deno namespace declarations the code editor's TypeScript checker loads for a
project with a `deno.json` or `deno.jsonc`. Deno builds its types into the
`deno` executable rather than installing them into the project, so the editor
ships its own copy.

The entire `build/` directory is excluded from the application build via
the `.csproj` — only `lib/deno.d.ts` ships with the app.

## Updating

1. Install or upgrade Deno (`deno upgrade`)
2. Run `npm run vendor:deno-types` from `Source/`, or `npm run vendor` from this directory
3. Check the script found the libraries: it stops if `deno types` changes shape
4. Update the Deno version in `THIRD-PARTY-LICENSES.txt`
5. Commit the updated `lib/deno.d.ts`
