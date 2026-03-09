// Localization API: Localization update events.

/**
 * Localization events API.
 */
export class LocalizationAPI {
    /** @type {import('../core/rpc-transport.js').RpcTransport} */
    #transport;

    /**
     * @param {import('../core/rpc-transport.js').RpcTransport} transport
     */
    constructor(transport) {
        this.#transport = transport;
    }

    /**
     * Registers a handler for localization update notifications.
     * @param {(strings: Object<string, string>) => void} handler - Called when localization is updated.
     */
    onUpdated(handler) {
        this.#transport.addEventListener('localization/updated', handler);
    }
}
