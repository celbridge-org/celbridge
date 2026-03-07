// Input API: User input notifications (keyboard shortcuts, link clicks, scroll events).

/**
 * Input events API.
 */
export class InputAPI {
    /** @type {import('../core/rpc-transport.js').RpcTransport} */
    #transport;

    /**
     * @param {import('../core/rpc-transport.js').RpcTransport} transport
     */
    constructor(transport) {
        this.#transport = transport;
    }

    /**
     * Notifies the host that a link was clicked in the document.
     * Used for opening local resources in the editor.
     * @param {string} href - The href of the clicked link.
     */
    notifyLinkClicked(href) {
        this.#transport.notify('link/clicked', { href });
    }

    /**
     * Notifies the host that the scroll position has changed.
     * Used for synchronizing scroll position with other views.
     * @param {number} percentage - The scroll position as a percentage (0.0 to 1.0).
     */
    notifyScrollChanged(percentage) {
        this.#transport.notify('editor/scrollChanged', { scrollPercentage: percentage });
    }
}
