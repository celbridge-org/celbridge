// Resources API: notifications of files created, changed, deleted or renamed in the project.

/**
 * @typedef {Object} ResourceChange
 * @property {'created'|'changed'|'deleted'|'renamed'} kind - What happened to the resource.
 * @property {string} resource - The canonical resource key (e.g. "project:src/main.py"), ready to pass to the
 *   file tools. For a rename, the key the resource has now.
 * @property {string|null} [oldResource] - For a rename, the key the resource had before. Absent otherwise.
 */

/**
 * Project resource change notifications. The host reports every change to a file in the project tree,
 * whichever way it was made: an editor's save, an agent's edit, a git checkout, another application. Changes
 * are collapsed per file until it has been quiet for a moment, so one save is one notification. Nothing is
 * sent until the page subscribes.
 *
 * ```js
 * client.resources.onChanged(change => console.log(change.kind, change.resource));
 * await client.resources.subscribe(['*.py', '*.js']);
 * ```
 */
export class ResourcesAPI {
    /** @type {import('../core/rpc-transport.js').RpcTransport} */
    #transport;

    /**
     * @param {import('../core/rpc-transport.js').RpcTransport} transport
     */
    constructor(transport) {
        this.#transport = transport;
    }

    /**
     * Starts receiving changes, or replaces the patterns of a subscription already running. Patterns use the
     * project's resource glob syntax: a pattern with no slash ("*.py") matches a file name at any depth, one
     * with a slash ("src/**\/*.ts") matches from the project root. Omit the patterns, or pass an empty list,
     * to receive changes to every file. Register onChanged first so no change arrives unhandled.
     * @param {string[]} [patterns]
     * @returns {Promise<void>}
     */
    async subscribe(patterns = []) {
        await this.#transport.request('resources/subscribe', { patterns: [...patterns] });
    }

    /**
     * Stops receiving changes. Changes the host has not yet reported are dropped.
     * @returns {Promise<void>}
     */
    async unsubscribe() {
        await this.#transport.request('resources/unsubscribe', {});
    }

    /**
     * Registers a handler for resource changes. A reloaded page starts unsubscribed, so call subscribe
     * again after registering.
     * @param {(change: ResourceChange) => void} handler
     * @returns {() => void} - Removes the handler.
     */
    onChanged(handler) {
        const listener = (params) => handler({
            kind: params?.kind,
            resource: params?.resource,
            oldResource: params?.oldResource ?? null
        });
        this.#transport.addEventListener('resources/changed', listener);

        return () => this.#transport.removeEventListener('resources/changed', listener);
    }
}
