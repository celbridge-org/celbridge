// Tools API: MCP tool dispatch wrapper and dynamic `cel.*` proxy for contribution editors.
//
// Contribution packages declare the tools they need via `requires_tools` in package.toml.
// The host injects the resolved allowlist as `window.__celbridgeContext.allowedTools`
// before navigation. This module builds a dynamic proxy that exposes only the allowed
// tools as `celbridge.cel.<namespace>.<tool>(...)`.
//
// The host re-enforces the same allowlist on every `tools/call` — the client-side proxy
// is a convenience that keeps undeclared tools off the API surface, but the host gate is
// authoritative.
//
// Calling convention:
//   - Arguments are positional and camelCase, in parameter declaration order.
//   - Extra positional arguments throw CelToolError(InvalidArgs).
//   - Arrays and plain objects passed to `string`-typed parameters are JSON-stringified
//     automatically (for editsJson, resources, files, etc.).

/**
 * Error codes for tool proxy failures. Wire codes are JSON-RPC application error codes
 * (-32000 to -32099); these string codes give editor code something stable to switch on.
 * @readonly
 * @enum {string}
 */
export const CelToolErrorCode = Object.freeze({
    NotFound:    'CEL_TOOL_NOT_FOUND',
    Denied:      'CEL_TOOL_DENIED',
    Failed:      'CEL_TOOL_FAILED',
    InvalidArgs: 'CEL_TOOL_INVALID_ARGS'
});

const JSON_RPC_CODE_TO_CEL = Object.freeze({
    [-32001]: CelToolErrorCode.NotFound,
    [-32002]: CelToolErrorCode.Denied,
    [-32003]: CelToolErrorCode.InvalidArgs,
    [-32004]: CelToolErrorCode.Failed
});

const CEL_TO_JSON_RPC_CODE = Object.freeze({
    [CelToolErrorCode.NotFound]:    -32001,
    [CelToolErrorCode.Denied]:      -32002,
    [CelToolErrorCode.InvalidArgs]: -32003,
    [CelToolErrorCode.Failed]:      -32004
});

/**
 * Error thrown by `celbridge.cel.*` tool invocations.
 */
export class CelToolError extends Error {
    /**
     * @param {string} code - One of the values in CelToolErrorCode.
     * @param {string} tool - The tool identifier (alias) that produced the error.
     * @param {string} message - Human-readable message.
     */
    constructor(code, tool, message) {
        super(message);
        this.name = 'CelToolError';
        this.code = code;
        this.tool = tool;
    }
}

/**
 * Returns true if the given tool alias is allowed by an allowlist entry.
 * Supported patterns: literal alias ("app.get_version"), namespace wildcard ("app.*"),
 * and a lone "*" which allows all tools. Glob syntax only — no regex.
 * @param {string} alias - The tool alias to check (e.g. "document.open").
 * @param {string} pattern - A single allowlist pattern.
 * @returns {boolean}
 */
export function matchesToolPattern(alias, pattern) {
    if (typeof alias !== 'string' || typeof pattern !== 'string') {
        return false;
    }
    if (pattern === '*') {
        return true;
    }
    if (pattern.endsWith('.*')) {
        const prefix = pattern.slice(0, -1); // keep trailing "."
        return alias.startsWith(prefix);
    }
    return alias === pattern;
}

/**
 * Returns true if the tool alias is allowed by any pattern in the list.
 * @param {string} alias
 * @param {ReadonlyArray<string>} allowedPatterns
 * @returns {boolean}
 */
export function isToolAllowed(alias, allowedPatterns) {
    if (!Array.isArray(allowedPatterns) || allowedPatterns.length === 0) {
        return false;
    }
    for (const pattern of allowedPatterns) {
        if (matchesToolPattern(alias, pattern)) {
            return true;
        }
    }
    return false;
}

/**
 * Returns true if the value is a plain object literal (not null, not an array,
 * not a class instance). Used by the positional-call shape to decide whether a
 * trailing argument should be unpacked as keyword arguments.
 * @param {*} value
 * @returns {boolean}
 */
function isPlainObject(value) {
    if (value === null || typeof value !== 'object') {
        return false;
    }
    if (Array.isArray(value)) {
        return false;
    }
    const prototype = Object.getPrototypeOf(value);
    return prototype === Object.prototype || prototype === null;
}

/**
 * Converts a snake_case identifier to camelCase. Used to translate server-side
 * aliases (e.g. "get_version") into their JavaScript method names ("getVersion").
 * @param {string} name
 * @returns {string}
 */
function snakeToCamelCase(name) {
    if (typeof name !== 'string' || name.length === 0) {
        return '';
    }
    return name.replace(/_([a-z0-9])/g, (_, character) => character.toUpperCase());
}

/**
 * Builds a nested object proxy from a list of tool descriptors.
 *
 * Each descriptor's alias becomes a method path: "file.apply_edits" exposes
 * `proxy.file.applyEdits(...)`. Arguments are mapped to the descriptor's
 * parameter list in declaration order. Arrays and plain objects passed to
 * string-typed parameters are JSON-stringified automatically.
 *
 * @param {ReadonlyArray<ToolDescriptor>} descriptors - The tool descriptors to expose.
 * @param {(alias: string, args: Object) => Promise<any>} invoke - Dispatch function.
 * @returns {Object} The root of the proxy tree.
 */
export function buildCelProxy(descriptors, invoke) {
    const root = {};
    if (!Array.isArray(descriptors)) {
        return root;
    }

    for (const descriptor of descriptors) {
        if (!descriptor || typeof descriptor.alias !== 'string' || descriptor.alias.length === 0) {
            continue;
        }
        const alias = descriptor.alias;
        const segments = alias.split('.');
        const leafSegment = segments.pop();
        const leafName = snakeToCamelCase(leafSegment);

        let node = root;
        for (const segment of segments) {
            if (!Object.prototype.hasOwnProperty.call(node, segment) || typeof node[segment] !== 'object') {
                node[segment] = {};
            }
            node = node[segment];
        }
        node[leafName] = buildLeafFunction(descriptor, invoke);
    }

    return root;
}

function buildLeafFunction(descriptor, invoke) {
    const alias = descriptor.alias;
    const rawParameters = Array.isArray(descriptor.parameters) ? descriptor.parameters : [];
    const parameterNames = [];
    const parameterTypes = {};

    for (const parameter of rawParameters) {
        if (parameter && typeof parameter.name === 'string') {
            parameterNames.push(parameter.name);
            parameterTypes[parameter.name] = typeof parameter.type === 'string' ? parameter.type : '';
        }
    }

    const expectedCount = parameterNames.length;
    const signature = `cel.${alias}(${parameterNames.join(', ')})`;

    return (...args) => {
        if (args.length > expectedCount) {
            throw new CelToolError(
                CelToolErrorCode.InvalidArgs,
                alias,
                `${signature} was called with ${args.length} positional arguments`
            );
        }

        const argumentsObject = {};
        for (let index = 0; index < args.length; index++) {
            const parameterName = parameterNames[index];
            if (typeof parameterName === 'string') {
                argumentsObject[parameterName] = args[index];
            }
        }

        // Auto-serialize arrays and plain objects when the tool parameter expects a string.
        for (const [parameterName, value] of Object.entries(argumentsObject)) {
            if (parameterTypes[parameterName] === 'string' && (Array.isArray(value) || isPlainObject(value))) {
                argumentsObject[parameterName] = JSON.stringify(value);
            }
        }

        return invoke(alias, argumentsObject);
    };
}

/**
 * Client-side tools API. Loads tool descriptors from the host during
 * `celbridge.initialize()`, builds a positional `cel.*` proxy, and dispatches
 * calls through JSON-RPC. The host re-enforces the allowlist on every call —
 * this proxy does not bypass that gate.
 */
export class ToolsAPI {
    /** @type {import('../core/rpc-transport.js').RpcTransport} */
    #transport;

    /** @type {ReadonlyArray<string>} */
    #allowedPatterns;

    /** @type {ReadonlyArray<ToolDescriptor>|null} */
    #descriptors = null;

    /** @type {Object|null} */
    #celProxy = null;

    /**
     * @param {import('../core/rpc-transport.js').RpcTransport} transport
     * @param {ReadonlyArray<string>} [allowedPatterns] - Glob patterns for allowed tools.
     * @param {ReadonlyArray<ToolDescriptor>} [initialDescriptors] - Pre-supplied descriptors
     *   (tests can pass these to skip the tools/list fetch).
     */
    constructor(transport, allowedPatterns = [], initialDescriptors = null) {
        this.#transport = transport;
        this.#allowedPatterns = Array.isArray(allowedPatterns) ? [...allowedPatterns] : [];

        if (Array.isArray(initialDescriptors)) {
            this.setDescriptors(initialDescriptors);
        }
    }

    /**
     * The resolved allowlist patterns declared by the package manifest.
     * @returns {ReadonlyArray<string>}
     */
    get allowedPatterns() {
        return this.#allowedPatterns;
    }

    /**
     * Returns true once descriptors have been loaded (via loadDescriptors,
     * setDescriptors, or the constructor's initialDescriptors argument).
     * The `cel` proxy throws until this is true.
     * @returns {boolean}
     */
    get isReady() {
        return this.#descriptors !== null;
    }

    /**
     * Fetches tools/list from the host, filters by the allowlist, and stores
     * the descriptors so `cel.*` becomes callable. Invoked by Celbridge.initialize().
     * Safe to call multiple times; subsequent calls refresh the descriptor list.
     *
     * When the allowlist is empty the fetch is skipped — no tool can pass the
     * gate, so there is nothing to discover. Packages that do not declare
     * `requires_tools` therefore pay no startup round-trip.
     *
     * @returns {Promise<void>}
     */
    async loadDescriptors() {
        if (this.#allowedPatterns.length === 0) {
            this.setDescriptors([]);
            return;
        }

        let response;
        try {
            response = await this.#transport.request('tools/list', {});
        } catch {
            // If the host does not expose tools (older host or test environment),
            // mark ready with an empty descriptor list rather than rejecting.
            this.setDescriptors([]);
            return;
        }

        const tools = Array.isArray(response) ? response : response?.tools;
        const filtered = Array.isArray(tools)
            ? tools.filter(tool => typeof tool?.alias === 'string' && isToolAllowed(tool.alias, this.#allowedPatterns))
            : [];

        this.setDescriptors(filtered);
    }

    /**
     * Replaces the descriptor list and rebuilds the `cel.*` proxy. Primarily
     * for tests that want to skip the `tools/list` round-trip.
     * @param {ReadonlyArray<ToolDescriptor>} descriptors
     */
    setDescriptors(descriptors) {
        const copy = Array.isArray(descriptors) ? [...descriptors] : [];
        this.#descriptors = Object.freeze(copy);
        this.#celProxy = buildCelProxy(copy, (alias, args) => this.call(alias, args));
    }

    /**
     * Returns the cached descriptor list for tools reachable through this proxy.
     * Throws if called before `loadDescriptors` / `setDescriptors` has run.
     * @returns {ReadonlyArray<ToolDescriptor>}
     */
    list() {
        if (this.#descriptors === null) {
            throw new CelToolError(
                CelToolErrorCode.Failed,
                '',
                'Tool descriptors not loaded. Call celbridge.initialize() first.'
            );
        }
        return this.#descriptors;
    }

    /**
     * Invokes a tool by its alias (e.g. "app.get_version"). This is the primary
     * entry point and is what the `cel.*` proxy calls internally.
     * @param {string} alias - The tool alias.
     * @param {Object} [args] - Keyword arguments for the tool.
     * @returns {Promise<any>} The tool's result value.
     */
    async call(alias, args) {
        if (!isToolAllowed(alias, this.#allowedPatterns)) {
            throw new CelToolError(
                CelToolErrorCode.Denied,
                alias,
                `Tool '${alias}' is not in this package's requires_tools allowlist`
            );
        }

        let response;
        try {
            response = await this.#transport.request('tools/call', {
                name: alias,
                arguments: args ?? {}
            });
        } catch (error) {
            const code = mapJsonRpcCodeToCel(error?.code) ?? CelToolErrorCode.Failed;
            throw new CelToolError(code, alias, error?.message ?? 'Tool call failed');
        }

        if (response && response.isSuccess === false) {
            throw new CelToolError(
                CelToolErrorCode.Failed,
                alias,
                response.errorMessage || `Tool '${alias}' failed`
            );
        }

        return response?.value ?? null;
    }

    /**
     * Returns the `cel.*` proxy. Throws `CelToolError` synchronously if accessed
     * before `celbridge.initialize()` has loaded the tool descriptors.
     * The proxy tree mirrors the Python `cel` object layout: a tool alias like
     * "file.apply_edits" is exposed as `cel.file.applyEdits(...)`.
     * @returns {Object}
     */
    get cel() {
        if (this.#celProxy === null) {
            throw new CelToolError(
                CelToolErrorCode.Failed,
                '',
                'cel proxy not initialized. Call celbridge.initialize() first.'
            );
        }
        return this.#celProxy;
    }
}

function mapJsonRpcCodeToCel(code) {
    if (typeof code !== 'number') {
        return null;
    }
    return JSON_RPC_CODE_TO_CEL[code] ?? null;
}

/**
 * Maps a CelToolErrorCode to its JSON-RPC wire code. Exposed for tests and for
 * hosts implementing the `tools/call` gate.
 * @param {string} celCode
 * @returns {number|null}
 */
export function jsonRpcCodeForCelCode(celCode) {
    return CEL_TO_JSON_RPC_CODE[celCode] ?? null;
}

/**
 * @typedef {Object} ToolParameter
 * @property {string} name - Parameter name in camelCase.
 * @property {string} type - JSON Schema type name (e.g. "string", "number", "boolean", "array").
 * @property {boolean} [hasDefaultValue] - True if the parameter has a default.
 * @property {*} [defaultValue] - The default value, if any.
 */

/**
 * @typedef {Object} ToolDescriptor
 * @property {string} name - The MCP tool name (e.g. "app_get_version").
 * @property {string} alias - The cel-proxy alias (e.g. "app.get_version").
 * @property {string} description - Human-readable description.
 * @property {string} [returnType] - JSON Schema type name for the return value.
 * @property {Array<ToolParameter>} [parameters] - Parameter metadata.
 */
