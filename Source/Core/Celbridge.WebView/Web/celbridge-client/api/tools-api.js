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
 * Builds a nested object proxy from a flat list of tool aliases.
 * For an alias like "document.open", the proxy exposes `proxy.document.open(args)`,
 * which dispatches via `invoke(alias, args)`.
 *
 * @param {ReadonlyArray<string>} aliases - The allowed tool aliases.
 * @param {(alias: string, args?: Object) => Promise<any>} invoke - Dispatch function.
 * @returns {Object} The root of the proxy tree.
 */
export function buildCelProxy(aliases, invoke) {
    const root = {};
    for (const alias of aliases) {
        if (typeof alias !== 'string' || alias.length === 0) {
            continue;
        }
        const segments = alias.split('.');
        const leafName = segments.pop();
        let node = root;
        for (const segment of segments) {
            if (!Object.prototype.hasOwnProperty.call(node, segment) || typeof node[segment] !== 'object') {
                node[segment] = {};
            }
            node = node[segment];
        }
        node[leafName] = (args) => invoke(alias, args);
    }
    return root;
}

/**
 * Client-side tools API. Builds a dynamic `cel.*` proxy from the allowlist and
 * dispatches tool calls through JSON-RPC to the host. The host re-enforces the
 * allowlist on every call — this proxy does not bypass that gate.
 */
export class ToolsAPI {
    /** @type {import('../core/rpc-transport.js').RpcTransport} */
    #transport;

    /** @type {ReadonlyArray<string>} */
    #allowedPatterns;

    /** @type {Object|null} */
    #celProxy = null;

    /** @type {Promise<ReadonlyArray<ToolDescriptor>>|null} */
    #toolListPromise = null;

    /**
     * @param {import('../core/rpc-transport.js').RpcTransport} transport
     * @param {ReadonlyArray<string>} [allowedPatterns] - Glob patterns for allowed tools.
     */
    constructor(transport, allowedPatterns = []) {
        this.#transport = transport;
        this.#allowedPatterns = Array.isArray(allowedPatterns) ? [...allowedPatterns] : [];
    }

    /**
     * The resolved allowlist patterns declared by the package manifest.
     * @returns {ReadonlyArray<string>}
     */
    get allowedPatterns() {
        return this.#allowedPatterns;
    }

    /**
     * Lists all tools reachable through this proxy (pre-filtered by the allowlist).
     * Result is cached; subsequent calls return the same list without re-querying the host.
     * @returns {Promise<ReadonlyArray<ToolDescriptor>>}
     */
    async list() {
        if (this.#toolListPromise === null) {
            this.#toolListPromise = this.#fetchToolList();
        }
        return this.#toolListPromise;
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
     * Returns the `cel.*` proxy. Lazily constructed from the allowlist on first access.
     * The proxy tree mirrors the Python `cel` object layout: a tool alias like
     * "document.open" is exposed as `cel.document.open(args)`.
     * @returns {Object}
     */
    get cel() {
        if (this.#celProxy === null) {
            const reachableAliases = this.#enumerateLiteralAliases();
            this.#celProxy = buildCelProxy(reachableAliases, (alias, args) => this.call(alias, args));
        }
        return this.#celProxy;
    }

    /**
     * Rebuilds the proxy after the tool list has been fetched from the host,
     * merging in any aliases matched by wildcard patterns.
     * Call this after `await list()` if the package uses wildcard allowlist entries
     * and you want them surfaced on `celbridge.cel`.
     */
    async refreshProxy() {
        const tools = await this.list();
        const aliases = tools
            .map(tool => tool?.alias)
            .filter(alias => typeof alias === 'string' && alias.length > 0);
        this.#celProxy = buildCelProxy(aliases, (alias, args) => this.call(alias, args));
    }

    #enumerateLiteralAliases() {
        const literals = [];
        for (const pattern of this.#allowedPatterns) {
            if (typeof pattern !== 'string') {
                continue;
            }
            if (pattern === '*' || pattern.endsWith('.*')) {
                // Wildcards expand only after list() has been called; skip here.
                continue;
            }
            literals.push(pattern);
        }
        return literals;
    }

    async #fetchToolList() {
        let response;
        try {
            response = await this.#transport.request('tools/list', {});
        } catch (error) {
            // If the host does not expose tools (older host or test environment),
            // return an empty list rather than rejecting.
            return [];
        }

        const tools = Array.isArray(response) ? response : response?.tools;
        if (!Array.isArray(tools)) {
            return [];
        }

        return tools.filter(tool => {
            const alias = tool?.alias;
            return typeof alias === 'string' && isToolAllowed(alias, this.#allowedPatterns);
        });
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
 * @typedef {Object} ToolDescriptor
 * @property {string} name - The MCP tool name (e.g. "app_get_version").
 * @property {string} alias - The cel-proxy alias (e.g. "app.get_version").
 * @property {string} description - Human-readable description.
 * @property {string} [returnType] - JSON Schema type name for the return value.
 * @property {Array<Object>} [parameters] - Parameter metadata.
 */
