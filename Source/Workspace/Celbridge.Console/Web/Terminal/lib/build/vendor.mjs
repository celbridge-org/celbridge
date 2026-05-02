// vendor.mjs — Copy xterm.js dist files from node_modules into the parent lib/.
// Run from the build/ directory: npm install && npm run vendor

import { copyFile } from 'fs/promises';
import { resolve, dirname } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const libDir = resolve(__dirname, '..');
const nodeModules = resolve(__dirname, 'node_modules');

const files = [
    ['@xterm/xterm/lib/xterm.js', 'xterm.js'],
    ['@xterm/xterm/lib/xterm.js.map', 'xterm.js.map'],
    ['@xterm/xterm/css/xterm.css', 'xterm.css'],
    ['@xterm/xterm/LICENSE', 'LICENSE'],
    ['@xterm/addon-fit/lib/addon-fit.js', 'addon-fit.js'],
    ['@xterm/addon-fit/lib/addon-fit.js.map', 'addon-fit.js.map'],
    ['@xterm/addon-clipboard/lib/addon-clipboard.js', 'addon-clipboard.js'],
    ['@xterm/addon-clipboard/lib/addon-clipboard.js.map', 'addon-clipboard.js.map'],
    ['@xterm/addon-web-links/lib/addon-web-links.js', 'addon-web-links.js'],
    ['@xterm/addon-web-links/lib/addon-web-links.js.map', 'addon-web-links.js.map'],
];

for (const [src, dest] of files) {
    const from = resolve(nodeModules, src);
    const to = resolve(libDir, dest);
    await copyFile(from, to);
    console.log(`copied ${src} -> ${dest}`);
}

console.log('done');
