import { describe, it, expect } from 'vitest';
import { readdirSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

// Every module under types/ is one session type's contribution to the settings form. The modules are read
// from the folder rather than listed here, so a type added to the console is checked by the same cases.
const typesFolder = fileURLToPath(new URL('../types/', import.meta.url));
const typeIds = readdirSync(typesFolder)
    .filter((fileName) => fileName.endsWith('.js'))
    .map((fileName) => fileName.replace(/\.js$/, ''));

// How a field's text is read out of its control. console.js reads anything else as plain text, which would
// silently write a list field as one line.
const fieldKinds = ['text', 'lines', 'script'];

const consoleSource = readFileSync(fileURLToPath(new URL('../console.js', import.meta.url)), 'utf8');

async function loadTypeModule(typeId) {
    const typeModule = await import(`../types/${typeId}.js`);

    return typeModule.default;
}

describe('session type modules', () => {
    it('finds the modules to check', () => {
        // A sweep that found nothing would pass every case below having checked nothing.
        expect(typeIds.length).toBeGreaterThan(0);
    });

    it.each(typeIds)('%s declares the type id its file name gives it', async (typeId) => {
        const typeModule = await loadTypeModule(typeId);

        // The host pairs a registered session type with types/<id>.js, so a module naming a different type
        // would satisfy that check and still leave the type unselectable.
        expect(typeModule.typeId).toBe(typeId);
    });

    it.each(typeIds)('%s is imported by the settings form', (typeId) => {
        // The form builds its type map from a hand-written import list rather than from this folder, so a
        // module the list omits leaves its type unselectable with nothing to report it. An import the map
        // then leaves out is an unused binding, which the lint step fails on.
        expect(consoleSource).toContain(`from './types/${typeId}.js'`);
    });

    it.each(typeIds)('%s has a control for every field it declares', async (typeId) => {
        const typeModule = await loadTypeModule(typeId);

        expect(typeModule.icon).toBeTruthy();
        expect(typeModule.fields.length).toBeGreaterThan(0);

        for (const field of typeModule.fields) {
            expect(typeModule.markup).toContain(`id="${field.id}"`);
            expect(field.key).toBeTruthy();
            expect(fieldKinds).toContain(field.kind);
        }
    });
});
