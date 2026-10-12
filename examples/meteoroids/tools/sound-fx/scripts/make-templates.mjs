// Regenerates templates/: one complete Sound FX .wav per preset, which New File offers by name. A maintainer
// tool, not needed to use the editor. Run it with Node 22 or later, from any folder:
//
//     node tools/sound-fx/scripts/make-templates.mjs
//
// It uses the vendored jsfxr and the editor's own modules, so a template is exactly what the editor would
// make. Math.random is swapped for a seeded generator while each sound is rolled and rendered, so the
// preset's choices and the noise both come out the same on every run and the files don't churn in git.

import { GENERATOR, PRESETS, render, rollPreset } from '../js/params.js';
import { writeWav } from '../js/wav-file.js';

// The seed each template's preset roll uses. Change one to pick a different variation of that preset.
export const TEMPLATE_SEEDS = {
    laser: 5,
    explosion: 4,
    coin: 1,
    'power-up': 2,
    hit: 2,
    jump: 2,
    blip: 9,
};

// mulberry32: a small, well-mixed 32-bit generator with the same [0, 1) contract as Math.random.
function seededRandom(seed) {
    let state = seed >>> 0;
    return () => {
        state = (state + 0x6d2b79f5) >>> 0;
        let value = state;
        value = Math.imul(value ^ (value >>> 15), value | 1);
        value ^= value + Math.imul(value ^ (value >>> 7), value | 61);
        return ((value ^ (value >>> 14)) >>> 0) / 4294967296;
    };
}

// Returns [{ id, bytes }], one per preset. Needs globalThis.jsfxr, as the editor page has it.
export function buildTemplates() {
    const random = Math.random;
    try {
        return PRESETS.map((preset) => {
            Math.random = seededRandom(TEMPLATE_SEEDS[preset.id]);
            const params = rollPreset(preset.method);
            const wave = render(params);
            return { id: preset.id, bytes: writeWav(wave.wav, params, GENERATOR) };
        });
    } finally {
        Math.random = random;
    }
}

async function main() {
    const { readFile, writeFile, mkdir } = await import('node:fs/promises');
    const { runInThisContext } = await import('node:vm');
    const { fileURLToPath } = await import('node:url');

    // Loaded as classic scripts, as the editor page's <script> tags load them, so they define
    // globalThis.RIFFWAVE and globalThis.jsfxr.
    for (const name of ['riffwave.js', 'sfxr.js']) {
        const url = new URL(`../lib/${name}`, import.meta.url);
        runInThisContext(await readFile(url, 'utf8'), { filename: fileURLToPath(url) });
    }

    const folder = new URL('../templates/', import.meta.url);
    await mkdir(folder, { recursive: true });
    for (const { id, bytes } of buildTemplates()) {
        const url = new URL(`${id}.wav`, folder);
        await writeFile(url, bytes);
        console.log(`Wrote ${fileURLToPath(url)}`);
    }
}

// Runs only when started by Node, so the editor page can import buildTemplates too.
if (typeof process !== 'undefined' && process.versions?.node) {
    const { pathToFileURL } = await import('node:url');
    if (process.argv[1] && pathToFileURL(process.argv[1]).href === import.meta.url) {
        await main();
    }
}
