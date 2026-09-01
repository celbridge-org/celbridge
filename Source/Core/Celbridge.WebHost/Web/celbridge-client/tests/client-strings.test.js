import { describe, it, expect } from 'vitest';
import { readdirSync, readFileSync, statSync } from 'fs';
import { join, relative } from 'path';
import { fileURLToPath } from 'url';
import clientStrings from '../localization/en.json';

// The strings the client's own controls show are applied at runtime rather than from markup, so nothing in
// an index.html names them and the coverage test over those files cannot see them. A key missing from
// en.json renders as its own name in the editor, with only a console warning to say so. This walks the
// client's modules for the keys they ask for.

const clientFolder = fileURLToPath(new URL('..', import.meta.url));
const skippedFolders = new Set(['tests', 'node_modules', 'localization']);

// A `t('Key')` call with a literal key. The lookbehind keeps the match off the tail of a longer name, so a
// call such as `format('...')` is not read as one.
const localizedKeyPattern = /(?<![\w.$])t\(\s*['"]([A-Za-z][A-Za-z0-9_]*)['"]/g;

function findModules(folder) {
    const modules = [];

    for (const entry of readdirSync(folder)) {
        if (skippedFolders.has(entry)) {
            continue;
        }

        const path = join(folder, entry);
        if (statSync(path).isDirectory()) {
            modules.push(...findModules(path));
        } else if (entry.endsWith('.js')) {
            modules.push(path);
        }
    }

    return modules;
}

function readLocalizedKeys(modulePath) {
    const source = readFileSync(modulePath, 'utf8');

    return [...source.matchAll(localizedKeyPattern)].map((match) => match[1]);
}

describe('the client localization file', () => {
    it('carries every string the client modules ask for by name', () => {
        const modules = findModules(clientFolder);
        expect(modules.length).toBeGreaterThan(0);

        const availableKeys = new Set(Object.keys(clientStrings));
        const missing = [];

        for (const modulePath of modules) {
            for (const key of readLocalizedKeys(modulePath)) {
                if (!availableKeys.has(key)) {
                    missing.push(`${relative(clientFolder, modulePath)}: ${key}`);
                }
            }
        }

        expect(missing).toEqual([]);
    });

    it('reaches the strings the find bar and the icon field ask for', () => {
        // A guard on the scan itself: a pattern that quietly matched nothing would pass the test above.
        const foundKeys = new Set(findModules(clientFolder).flatMap(readLocalizedKeys));

        expect(foundKeys).toContain('FindBar_Placeholder');
        expect(foundKeys).toContain('FindBar_Count');
        expect(foundKeys).toContain('IconPicker_BrowseTooltip');
    });
});
