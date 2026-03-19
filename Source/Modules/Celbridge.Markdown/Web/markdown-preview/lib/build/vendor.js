// Build script for vendoring marked.js
// Run with: npm run vendor

import { build } from 'esbuild';
import { fileURLToPath } from 'url';
import { dirname, resolve } from 'path';

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);

await build({
    entryPoints: [resolve(__dirname, 'entry.js')],
    bundle: true,
    format: 'esm',
    outfile: resolve(__dirname, '..', 'marked.esm.js'),
    minify: true,
    sourcemap: false,
    target: ['es2020'],
    platform: 'browser'
});

console.log('Vendored marked.js bundle created at lib/marked.esm.js');
