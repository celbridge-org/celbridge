// vendor.js — Bundle all TipTap packages into a single ESM file (lib/tiptap.js)
// Run from the build/ directory: npm install && npm run vendor
//
// A single bundle guarantees one ProseMirror instance, avoiding the
// "Adding different instances of a keyed plugin" error that occurs when
// multiple bundles each include their own copy of ProseMirror internals.

import { build } from 'esbuild';
import { resolve, dirname } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const libDir = resolve(__dirname, '..');

async function run() {
    console.log('Bundling TipTap into lib/tiptap.js ...');
    await build({
        entryPoints: [resolve(__dirname, 'entry.js')],
        bundle: true,
        format: 'esm',
        outfile: resolve(libDir, 'tiptap.js'),
        minify: true,
        sourcemap: false,
        target: 'es2020',
        logLevel: 'warning',
    });
    console.log('Done — tiptap.js written to lib/');
}

run().catch(err => { console.error(err); process.exit(1); });
