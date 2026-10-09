// Gives Monaco's built-in TypeScript worker the project files a TypeScript or JavaScript document depends on.
// The worker runs in the WebView with no view of the file system, so an import resolves only to a file handed
// to it with addExtraLib. The loader follows the document's imports through the project folder, which the
// loopback server serves under /project/, and adds each file it finds at the file:/// path the worker resolves
// the import to. Relative imports, JSON imports, and packages in node_modules that ship type declarations then
// type check instead of showing as errors. Type packages the project lists in package.json or tsconfig.json
// load up front, because they declare globals (such as Bun or process) that no import names.

import { projectUrl } from '/assets/celbridge-client/api/document-api.js';
import { warn } from './logger.js';

const ScriptLanguages = new Set(['typescript', 'javascript']);

// The worker reads compiler options as TypeScript's numeric enum values.
const ScriptTargetESNext = 99;
const ModuleKindESNext = 99;
const ModuleResolutionBundler = 100;
const JsxEmitReactJSX = 4;

const DefaultCompilerOptions = {
    target: ScriptTargetESNext,
    module: ModuleKindESNext,
    moduleResolution: ModuleResolutionBundler,
    jsx: JsxEmitReactJSX,
    // Monaco's own default, without which a model whose name has no TypeScript extension is not checked.
    allowNonTsExtensions: true,
    allowJs: true,
    allowImportingTsExtensions: true,
    noEmit: true,
    resolveJsonModule: true,
    // Lets a JSON import resolve to the `.d.json.ts` declaration the loader writes for it.
    allowArbitraryExtensions: true,
    esModuleInterop: true,
    allowSyntheticDefaultImports: true,
    skipLibCheck: true,
    isolatedModules: true
};

// Matches the module named by `import ... from`, `export ... from`, a bare `import '...'`, `import('...')` and
// `require('...')`.
const ImportPattern = /\b(?:from|import|require)\s*\(?\s*['"]([^'"\n]+)['"]/g;
const ReferencePathPattern = /^\s*\/\/\/\s*<reference\s+path\s*=\s*['"]([^'"]+)['"]/gm;
const ReferenceTypesPattern = /^\s*\/\/\/\s*<reference\s+types\s*=\s*['"]([^'"]+)['"]/gm;

// Node's built-in modules, whose types come from @types/node.
const NodeBuiltins = new Set([
    'assert', 'async_hooks', 'buffer', 'child_process', 'cluster', 'console', 'crypto', 'dgram', 'dns',
    'events', 'fs', 'fs/promises', 'http', 'http2', 'https', 'module', 'net', 'os', 'path', 'perf_hooks',
    'process', 'querystring', 'readline', 'stream', 'string_decoder', 'timers', 'tls', 'tty', 'url', 'util',
    'v8', 'vm', 'worker_threads', 'zlib'
]);

// Stops a large dependency tree from loading without end.
const DefaultMaxFiles = 3000;

// A JSON file larger than this is typed as `any` rather than described field by field.
const MaxDescribedJsonLength = 512 * 1024;

/**
 * Follows a document's imports through the project folder and hands each file found to a sink. Paths are
 * project-relative with no leading slash, such as `src/app.ts`.
 */
export class ProjectTypeLoader {
    #fetchText;
    #addLib;
    #maxFiles;
    // Path to the promise of the file's text, or of null when the file does not exist.
    #files = new Map();
    // Package name to the promise of whether the package was found.
    #packages = new Map();
    #addedCount = 0;
    // Dependency loads still running. A file's own promise settles without waiting for its dependencies,
    // so an import cycle cannot leave two files waiting on each other.
    #inFlight = new Set();

    /**
     * @param {Object} options
     * @param {(path: string) => Promise<string|null>} options.fetchText - Reads a project file, or null.
     * @param {(path: string, content: string) => void} options.addLib - Hands a file to the TypeScript worker.
     * @param {number} [options.maxFiles]
     */
    constructor({ fetchText, addLib, maxFiles = DefaultMaxFiles }) {
        this.#fetchText = fetchText;
        this.#addLib = addLib;
        this.#maxFiles = maxFiles;
    }

    /**
     * Records the open document's own path, so an import cycle back to it does not add a stale copy from disk
     * beside the live buffer.
     */
    excludePath(path) {
        this.#files.set(path, Promise.resolve(null));
    }

    /**
     * Loads the type packages the project names in package.json (its @types dependencies) and in tsconfig.json
     * (compilerOptions.types), and returns tsconfig.json's boolean compiler options, such as `strict`.
     */
    async loadProjectTypes() {
        const [packageJson, tsconfig] = await Promise.all([
            this.#fetchJson('package.json', false),
            this.#fetchJson('tsconfig.json', true)
        ]);

        const compilerOptions = tsconfig?.compilerOptions ?? {};
        const typePackages = new Set();

        for (const field of ['dependencies', 'devDependencies']) {
            for (const name of Object.keys(packageJson?.[field] ?? {})) {
                if (name.startsWith('@types/')) {
                    typePackages.add(name);
                }
            }
        }

        if (Array.isArray(compilerOptions.types)) {
            for (const name of compilerOptions.types) {
                if (typeof name === 'string') {
                    typePackages.add(name);
                }
            }
        }

        await Promise.all([...typePackages].map((name) => this.#loadTypesPackage(name)));

        const booleanOptions = {};
        for (const [key, value] of Object.entries(compilerOptions)) {
            if (typeof value === 'boolean') {
                booleanOptions[key] = value;
            }
        }
        return booleanOptions;
    }

    /**
     * Loads every file the source imports or references, and the files those depend on in turn.
     * @param {string} path - The source's own path, which relative imports resolve against.
     * @param {string} text - The source.
     */
    async loadDependencies(path, text) {
        const directory = parentPath(path);
        const tasks = [];

        for (const specifier of matchAll(ImportPattern, text)) {
            tasks.push(this.#loadModule(directory, specifier));
        }

        for (const reference of matchAll(ReferencePathPattern, text)) {
            const target = joinPath(directory, reference);
            if (target !== null) {
                tasks.push(this.#loadFile(target));
            }
        }

        for (const name of matchAll(ReferenceTypesPattern, text)) {
            tasks.push(this.#loadTypesPackage(name));
        }

        await Promise.all(tasks);
    }

    async #loadModule(directory, specifier) {
        if (specifier.startsWith('./') || specifier.startsWith('../') || specifier === '.' || specifier === '..') {
            const base = joinPath(directory, specifier);
            if (base !== null) {
                await this.#loadFirst(moduleCandidates(base));
            }
            return;
        }

        // Absolute paths and URLs name nothing in the project folder.
        if (specifier.startsWith('/') || /^[a-z][a-z0-9+.-]*:\/\//i.test(specifier)) {
            return;
        }

        if (specifier.startsWith('node:') || NodeBuiltins.has(specifier)) {
            await this.#loadTypesPackage('node');
            return;
        }

        if (specifier.startsWith('bun:') || specifier === 'bun') {
            await this.#loadTypesPackage('bun');
            return;
        }

        const { name, subpath } = splitPackageSpecifier(specifier);
        if (!name) {
            return;
        }

        if (!subpath) {
            if (!await this.#loadPackage(name)) {
                await this.#loadPackage(typesPackageName(name));
            }
            return;
        }

        const found = await this.#loadPackage(name)
            && await this.#loadFirst(declarationCandidates(`node_modules/${name}/${subpath}`));
        if (!found && await this.#loadPackage(typesPackageName(name))) {
            await this.#loadFirst(declarationCandidates(`node_modules/${typesPackageName(name)}/${subpath}`));
        }
    }

    // Loads a package by the name a tsconfig `types` entry or a `reference types` directive gives it, which
    // TypeScript looks for under @types first.
    async #loadTypesPackage(name) {
        if (name.startsWith('@types/')) {
            return this.#loadPackage(name);
        }

        return await this.#loadPackage(typesPackageName(name)) || await this.#loadPackage(name);
    }

    // Loads a package's package.json, which the worker reads to resolve the package's entry point, and the
    // declaration file the entry point names. Resolves to whether the package declares types.
    #loadPackage(name) {
        let pending = this.#packages.get(name);
        if (!pending) {
            pending = this.#readPackage(name);
            this.#packages.set(name, pending);
        }
        return pending;
    }

    async #readPackage(name) {
        const root = `node_modules/${name}`;
        const manifestText = await this.#loadFile(`${root}/package.json`);

        let manifest = null;
        if (manifestText !== null) {
            try {
                manifest = JSON.parse(manifestText);
            } catch {
                manifest = null;
            }
        }

        const entry = manifest?.types
            ?? manifest?.typings
            ?? findTypesCondition(manifest?.exports?.['.'] ?? manifest?.exports);

        if (typeof entry === 'string') {
            const target = joinPath(root, entry);
            if (target !== null && await this.#loadFirst(declarationCandidates(target))) {
                return true;
            }
        }

        return (await this.#loadFile(`${root}/index.d.ts`)) !== null;
    }

    // Loads the first candidate that exists, trying them in order. Resolves to whether any did.
    async #loadFirst(candidates) {
        for (const candidate of candidates) {
            if ((await this.#loadFile(candidate)) !== null) {
                return true;
            }
        }
        return false;
    }

    #loadFile(path) {
        let pending = this.#files.get(path);
        if (!pending) {
            pending = this.#readFile(path);
            this.#files.set(path, pending);
        }
        return pending;
    }

    async #readFile(path) {
        const text = await this.#fetchText(path);
        if (text === null || this.#addedCount >= this.#maxFiles) {
            return text;
        }

        this.#addedCount++;

        if (path.endsWith('.json') && !path.endsWith('/package.json')) {
            // TypeScript looks for `data.d.json.ts` before `data.json`, and the worker would otherwise parse
            // the JSON as a script.
            this.#addLib(`${path.slice(0, -'.json'.length)}.d.json.ts`, describeJson(text));
            return text;
        }

        this.#addLib(path, text);

        if (isScriptPath(path)) {
            this.#track(this.loadDependencies(path, text));
        }

        return text;
    }

    /**
     * Resolves once every file reachable so far has loaded.
     */
    async settled() {
        while (this.#inFlight.size > 0) {
            await Promise.all(this.#inFlight);
        }
    }

    #track(task) {
        const tracked = task
            .catch((error) => warn('typescript: dependency failed to load', error))
            .finally(() => this.#inFlight.delete(tracked));
        this.#inFlight.add(tracked);
    }

    async #fetchJson(path, allowComments) {
        const text = await this.#fetchText(path);
        if (text === null) {
            return null;
        }

        try {
            return JSON.parse(allowComments ? stripJsonComments(text) : text);
        } catch {
            return null;
        }
    }
}

/**
 * Wires a ProjectTypeLoader to Monaco's TypeScript worker and to the document's model. The model's URI must
 * be the document's file:/// path, which is what relative imports resolve against.
 */
export class TypeScriptProjectSupport {
    #loader = null;
    #model = null;
    #path = null;
    #scanTimer = null;

    /**
     * Starts following the imports of the model, when it holds TypeScript or JavaScript.
     * @param {Object} model - The document's Monaco model.
     * @param {string} resourceKey - The document's resource key.
     */
    async attach(model, resourceKey) {
        const path = resourcePath(resourceKey);
        if (!model || !path || this.#model || !ScriptLanguages.has(model.getLanguageId())) {
            return;
        }

        const defaults = typeScriptDefaults();
        if (!defaults) {
            return;
        }

        this.#model = model;
        this.#path = path;

        const addLib = (libPath, content) => {
            const uri = libUri(libPath);
            defaults.typescript.addExtraLib(content, uri);
            defaults.javascript.addExtraLib(content, uri);
        };

        this.#loader = new ProjectTypeLoader({ fetchText: fetchProjectText, addLib });
        this.#loader.excludePath(path);

        const setCompilerOptions = (options) => {
            defaults.typescript.setCompilerOptions(options);
            defaults.javascript.setCompilerOptions(options);
        };

        // Monaco's stock options resolve no imports, so these replace them before the worker first checks the
        // document. tsconfig.json's own flags, such as `strict`, follow once it has loaded.
        setCompilerOptions(DefaultCompilerOptions);

        model.onDidChangeContent(() => this.#scheduleScan());

        const [projectOptions] = await Promise.all([
            this.#loader.loadProjectTypes().catch((error) => {
                warn('typescript: project types failed to load', error);
                return {};
            }),
            this.#scan()
        ]);

        if (Object.keys(projectOptions).length > 0) {
            setCompilerOptions({ ...DefaultCompilerOptions, ...projectOptions });
        }
    }

    #scheduleScan() {
        clearTimeout(this.#scanTimer);
        this.#scanTimer = setTimeout(() => this.#scan(), 750);
    }

    async #scan() {
        if (!this.#model || this.#model.isDisposed?.()) {
            return;
        }

        try {
            await this.#loader.loadDependencies(this.#path, this.#model.getValue());
        } catch (error) {
            warn('typescript: imports failed to load', error);
        }
    }
}

// The TypeScript and JavaScript language defaults, which share one worker configuration here.
function typeScriptDefaults() {
    const namespace = monaco.typescript ?? monaco.languages?.typescript;
    if (!namespace?.typescriptDefaults || !namespace?.javascriptDefaults) {
        return null;
    }

    return {
        typescript: namespace.typescriptDefaults,
        javascript: namespace.javascriptDefaults
    };
}

/**
 * The file:/// URI a project file is the model of, or the name an extra lib is added under.
 */
export function modelUri(path) {
    return monaco.Uri.file(`/${path}`);
}

// The worker names a file by the string TypeScript resolves an import to. TypeScript joins the model's encoded
// URI with the raw specifier, so a lib takes the model's encoding, except for the "@" of a scoped or @types
// package, which only ever arrives raw from a specifier or a node_modules lookup.
function libUri(path) {
    return modelUri(path).toString().replace(/%40/g, '@');
}

async function fetchProjectText(path) {
    try {
        const response = await fetch(projectUrl(path));
        return response.ok ? await response.text() : null;
    } catch {
        return null;
    }
}

/**
 * The project-relative path of a resource key, with any `project:` prefix removed.
 */
export function resourcePath(resourceKey) {
    if (!resourceKey) {
        return null;
    }

    return resourceKey.startsWith('project:')
        ? resourceKey.substring('project:'.length)
        : resourceKey;
}

/**
 * A declaration for a JSON file, typing its default export as the value it holds.
 */
export function describeJson(text) {
    let type = 'any';
    if (text.length <= MaxDescribedJsonLength) {
        try {
            type = describeValue(JSON.parse(text), 0);
        } catch {
            type = 'any';
        }
    }

    return `declare const value: ${type};\nexport default value;\n`;
}

function describeValue(value, depth) {
    if (depth > 16) {
        return 'any';
    }

    if (value === null) {
        return 'null';
    }

    if (Array.isArray(value)) {
        const elementTypes = [...new Set(value.map((element) => describeValue(element, depth + 1)))];
        if (elementTypes.length === 0) {
            return 'unknown[]';
        }
        return elementTypes.length === 1 ? `${elementTypes[0]}[]` : `(${elementTypes.join(' | ')})[]`;
    }

    switch (typeof value) {
        case 'string':
            return 'string';
        case 'number':
            return 'number';
        case 'boolean':
            return 'boolean';
        case 'object': {
            const members = Object.entries(value)
                .map(([key, member]) => `${JSON.stringify(key)}: ${describeValue(member, depth + 1)};`);
            return `{ ${members.join(' ')} }`;
        }
        default:
            return 'any';
    }
}

// The files an import of `base` (a path with the relative specifier applied) can name, in the order TypeScript
// tries them.
function moduleCandidates(base) {
    if (/\.(d\.)?[cm]?tsx?$/.test(base) || base.endsWith('.json')) {
        return [base];
    }

    const scriptExtension = base.match(/\.([cm]?)jsx?$/);
    if (scriptExtension) {
        const stem = base.slice(0, -scriptExtension[0].length);
        const flavour = scriptExtension[1];
        return [`${stem}.${flavour}ts`, `${stem}.tsx`, `${stem}.d.${flavour}ts`, base];
    }

    return [
        `${base}.ts`, `${base}.tsx`, `${base}.d.ts`, `${base}.js`, `${base}.jsx`,
        `${base}/index.ts`, `${base}/index.tsx`, `${base}/index.d.ts`, `${base}/index.js`
    ];
}

// The declaration files a package's types entry or subpath can name.
function declarationCandidates(base) {
    if (/\.d\.[cm]?ts$/.test(base) || /\.[cm]?tsx?$/.test(base)) {
        return [base];
    }

    const stem = base.replace(/\.[cm]?jsx?$/, '');
    return [`${stem}.d.ts`, `${stem}/index.d.ts`, `${stem}.ts`];
}

function isScriptPath(path) {
    return /\.[cm]?[jt]sx?$/.test(path);
}

// Finds the `types` target in a package.json `exports` entry, which may nest it under other conditions.
function findTypesCondition(entry) {
    if (!entry || typeof entry !== 'object') {
        return null;
    }

    if (typeof entry.types === 'string') {
        return entry.types;
    }

    for (const condition of ['import', 'default', 'require', 'node']) {
        const found = findTypesCondition(entry[condition]);
        if (found) {
            return found;
        }
    }

    return null;
}

function splitPackageSpecifier(specifier) {
    const segments = specifier.split('/');
    const nameLength = specifier.startsWith('@') ? 2 : 1;
    if (segments.length < nameLength || segments.slice(0, nameLength).some((segment) => !segment)) {
        return { name: null, subpath: null };
    }

    return {
        name: segments.slice(0, nameLength).join('/'),
        subpath: segments.slice(nameLength).join('/')
    };
}

// The DefinitelyTyped package for a package name, which writes `@scope/name` as `@types/scope__name`.
function typesPackageName(name) {
    const mangled = name.startsWith('@') ? name.slice(1).replace('/', '__') : name;
    return `@types/${mangled}`;
}

function parentPath(path) {
    const index = path.lastIndexOf('/');
    return index < 0 ? '' : path.slice(0, index);
}

// Applies a relative path to a directory. Returns null for a path that climbs out of the project folder.
function joinPath(directory, relative) {
    const segments = directory ? directory.split('/') : [];
    for (const segment of relative.split('/')) {
        if (segment === '' || segment === '.') {
            continue;
        }
        if (segment === '..') {
            if (segments.length === 0) {
                return null;
            }
            segments.pop();
            continue;
        }
        segments.push(segment);
    }
    return segments.join('/');
}

function matchAll(pattern, text) {
    return Array.from(text.matchAll(pattern), (match) => match[1]);
}

// Removes the comments and trailing commas tsconfig.json allows, leaving string contents alone.
function stripJsonComments(text) {
    return text
        .replace(/("(?:\\.|[^"\\])*")|\/\/[^\n]*|\/\*[\s\S]*?\*\//g, (match, string) => string ?? '')
        .replace(/("(?:\\.|[^"\\])*")|,(\s*[}\]])/g, (match, string, closing) => string ?? closing);
}
