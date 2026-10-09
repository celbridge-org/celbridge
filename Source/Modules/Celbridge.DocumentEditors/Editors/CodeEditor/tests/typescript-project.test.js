import { describe, it, expect } from 'vitest';
import { ProjectTypeLoader, describeJson, resourcePath } from '../js/typescript-project.js';

// Loads from an in-memory project folder and records what the worker would be handed.
function createLoader(files) {
    const libs = new Map();
    const fetched = [];
    const loader = new ProjectTypeLoader({
        fetchText: async (path) => {
            fetched.push(path);
            return path in files ? files[path] : null;
        },
        addLib: (path, content) => libs.set(path, content)
    });
    return { loader, libs, fetched };
}

async function loadFor(loader, path, text) {
    await loader.loadDependencies(path, text);
    await loader.settled();
}

describe('ProjectTypeLoader', () => {
    it('loads relative imports and what they import in turn', async () => {
        const { loader, libs } = createLoader({
            'src/lib/utils.ts': "import { helper } from './helper';\nexport const add = 1;",
            'src/lib/helper.ts': 'export const helper = 2;'
        });

        await loadFor(loader, 'src/app.ts', "import { add } from './lib/utils';");

        expect([...libs.keys()]).toEqual(['src/lib/utils.ts', 'src/lib/helper.ts']);
    });

    it('settles when files import each other', async () => {
        const { loader, libs } = createLoader({
            'a.ts': "import { b } from './b';",
            'b.ts': "import { a } from './a';"
        });
        loader.excludePath('app.ts');

        await loadFor(loader, 'app.ts', "import './a';");

        expect([...libs.keys()].sort()).toEqual(['a.ts', 'b.ts']);
    });

    it('never adds the open document itself', async () => {
        const { loader, libs } = createLoader({
            'app.ts': 'stale copy on disk',
            'other.ts': "import './app';"
        });
        loader.excludePath('app.ts');

        await loadFor(loader, 'app.ts', "import './other';");

        expect([...libs.keys()]).toEqual(['other.ts']);
    });

    it('finds a .ts file named by a .js specifier, and an index file for a folder', async () => {
        const { loader, libs } = createLoader({
            'src/model.ts': '',
            'src/views/index.tsx': ''
        });

        await loadFor(loader, 'src/app.ts', "import './model.js';\nimport './views';");

        expect([...libs.keys()].sort()).toEqual(['src/model.ts', 'src/views/index.tsx']);
    });

    it('describes a JSON import with a .d.json.ts declaration', async () => {
        const { loader, libs } = createLoader({
            'data.json': '{ "name": "x", "items": [1, 2] }'
        });

        await loadFor(loader, 'app.ts', "import data from './data.json';");

        expect(libs.get('data.d.json.ts')).toBe(
            'declare const value: { "name": string; "items": number[]; };\nexport default value;\n');
    });

    it('loads a package\'s package.json and the types entry it names', async () => {
        const { loader, libs } = createLoader({
            'node_modules/mylib/package.json': '{ "types": "dist/index.d.ts" }',
            'node_modules/mylib/dist/index.d.ts': 'export declare const x: number;'
        });

        await loadFor(loader, 'app.ts', "import { x } from 'mylib';");

        expect([...libs.keys()]).toEqual([
            'node_modules/mylib/package.json',
            'node_modules/mylib/dist/index.d.ts'
        ]);
    });

    it('falls back to the @types package for a package without types', async () => {
        const { loader, libs } = createLoader({
            'node_modules/@types/scope__pkg/package.json': '{}',
            'node_modules/@types/scope__pkg/index.d.ts': ''
        });

        await loadFor(loader, 'app.ts', "import '@scope/pkg';");

        expect(libs.has('node_modules/@types/scope__pkg/index.d.ts')).toBe(true);
    });

    it('loads @types/node for a Node built-in module', async () => {
        const { loader, libs } = createLoader({
            'node_modules/@types/node/package.json': '{ "types": "index.d.ts" }',
            'node_modules/@types/node/index.d.ts': '/// <reference path="fs.d.ts" />',
            'node_modules/@types/node/fs.d.ts': ''
        });

        await loadFor(loader, 'app.ts', "import { readFileSync } from 'node:fs';");

        expect(libs.has('node_modules/@types/node/fs.d.ts')).toBe(true);
    });

    it('loads the type packages package.json and tsconfig.json name, and returns tsconfig\'s flags', async () => {
        const { loader, libs } = createLoader({
            'package.json': '{ "devDependencies": { "@types/bun": "1" } }',
            'tsconfig.json': '{\n  // comment\n  "compilerOptions": { "strict": true, "types": ["extra"], "outDir": "a//b", },\n}',
            'node_modules/@types/bun/package.json': '{ "types": "index.d.ts" }',
            'node_modules/@types/bun/index.d.ts': '/// <reference types="bun-types" />',
            'node_modules/bun-types/package.json': '{ "types": "index.d.ts" }',
            'node_modules/bun-types/index.d.ts': 'declare var Bun: unknown;',
            'node_modules/@types/extra/index.d.ts': ''
        });

        const options = await loader.loadProjectTypes();
        await loader.settled();

        expect(options).toEqual({ strict: true });
        expect(libs.has('node_modules/bun-types/index.d.ts')).toBe(true);
        expect(libs.has('node_modules/@types/extra/index.d.ts')).toBe(true);
    });

    it('ignores imports that climb out of the project folder', async () => {
        const { loader, libs, fetched } = createLoader({});

        await loadFor(loader, 'app.ts', "import '../outside';");

        expect(fetched).toEqual([]);
        expect(libs.size).toBe(0);
    });
});

describe('ProjectTypeLoader in a Deno project', () => {
    function createDenoLoader(files) {
        const libs = new Map();
        const loader = new ProjectTypeLoader({
            fetchText: async (path) => (path in files ? files[path] : null),
            fetchDenoTypes: async () => 'declare namespace Deno {}',
            addLib: (path, content) => libs.set(path, content)
        });
        return { loader, libs };
    }

    it('adds the Deno declarations and makes strict the default', async () => {
        const { loader, libs } = createDenoLoader({
            'deno.jsonc': '{\n  // comment\n  "compilerOptions": { "noImplicitOverride": true },\n}'
        });

        const options = await loader.loadProjectTypes();

        expect(options).toEqual({ strict: true, noImplicitOverride: true });
        expect(libs.get('__celbridge/deno.d.ts')).toBe('declare namespace Deno {}');
    });

    it('lets deno.json turn strict off', async () => {
        const { loader } = createDenoLoader({ 'deno.json': '{ "compilerOptions": { "strict": false } }' });

        expect(await loader.loadProjectTypes()).toEqual({ strict: false });
    });

    it('maps import map entries for project files and installed npm packages to paths', async () => {
        const { loader } = createDenoLoader({
            'deno.json': JSON.stringify({
                imports: { '@/': './src/', 'helpers': './src/helpers.ts', 'chalk': 'npm:chalk@5' }
            }),
            'node_modules/chalk/package.json': '{ "types": "index.d.ts" }',
            'node_modules/chalk/index.d.ts': ''
        });

        const options = await loader.loadProjectTypes();

        expect(options.baseUrl).toBe('file:///');
        expect(options.paths).toEqual({
            '@/*': ['src/*'],
            'helpers': ['src/helpers.ts'],
            'chalk': ['node_modules/chalk']
        });
    });

    it('declares remote import map entries and remote specifiers as modules', async () => {
        const { loader, libs } = createDenoLoader({
            'deno.json': JSON.stringify({ imports: { '@std/assert': 'jsr:@std/assert@^1', 'lodash': 'npm:lodash@4' } })
        });

        await loader.loadProjectTypes();
        const modules = libs.get('__celbridge/deno-modules.d.ts');

        for (const name of ['@std/assert', '@std/assert/*', 'lodash', 'jsr:*', 'npm:*', 'https://*', 'node:*']) {
            expect(modules).toContain(`declare module ${JSON.stringify(name)};`);
        }
    });

    it('reads the imports of a separate import map file', async () => {
        const { loader } = createDenoLoader({
            'deno.json': '{ "importMap": "./import_map.json" }',
            'import_map.json': '{ "imports": { "~/": "./lib/" } }'
        });

        expect((await loader.loadProjectTypes()).paths).toEqual({ '~/*': ['lib/*'] });
    });

    it('loads the project files an import map entry leads to', async () => {
        const { loader, libs } = createDenoLoader({
            'deno.json': JSON.stringify({ imports: { '@/': './src/', 'chalk': 'npm:chalk@5' } }),
            'src/util/deep.ts': 'export const deep = 1;',
            'node_modules/chalk/package.json': '{ "types": "index.d.ts" }',
            'node_modules/chalk/index.d.ts': ''
        });

        await loader.loadProjectTypes();
        await loadFor(loader, 'main.ts', "import { deep } from '@/util/deep.ts';\nimport chalk from 'chalk';");

        expect(libs.has('src/util/deep.ts')).toBe(true);
        expect(libs.has('node_modules/chalk/index.d.ts')).toBe(true);
    });

    it('adds nothing for Deno to a project without deno.json', async () => {
        const { loader, libs } = createDenoLoader({});

        expect(await loader.loadProjectTypes()).toEqual({});
        expect(libs.size).toBe(0);
    });
});

describe('describeJson', () => {
    it('types a value it cannot parse as any', () => {
        expect(describeJson('{ not json')).toBe('declare const value: any;\nexport default value;\n');
    });

    it('describes mixed arrays as a union', () => {
        expect(describeJson('[1, "a", null]')).toContain('(number | string | null)[]');
    });
});

describe('resourcePath', () => {
    it('strips the project prefix', () => {
        expect(resourcePath('project:src/app.ts')).toBe('src/app.ts');
        expect(resourcePath('src/app.ts')).toBe('src/app.ts');
        expect(resourcePath('')).toBeNull();
    });
});
