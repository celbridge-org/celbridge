// Markdown Client: Markdown-specific API for communicating with the Celbridge Markdown host.
// Uses the shared RPC transport from celbridge.js.

import { RpcTransport } from 'https://shared.celbridge/celbridge-client/core/rpc-transport.js';

/**
 * Markdown Client for editor communication.
 */
class MarkdownClient {
    /** @type {RpcTransport} */
    #transport;

    constructor() {
        this.#transport = new RpcTransport();
    }

    /**
     * Registers a handler for navigation requests from the host.
     * @param {function(string): void} handler - The handler function receiving the heading id or text.
     */
    onNavigateToHeading(handler) {
        this.#transport.addEventListener('markdown/navigateToHeading', (params) => {
            handler(params.heading);
        });
    }

    /**
     * Registers a handler for TOC visibility toggle requests from the host.
     * @param {function(boolean): void} handler - The handler function receiving the visibility state.
     */
    onSetTocVisibility(handler) {
        this.#transport.addEventListener('markdown/setTocVisibility', (params) => {
            handler(params.visible);
        });
    }

    /**
     * Registers a handler for focus requests from the host.
     * @param {function(): void} handler - The handler function.
     */
    onFocus(handler) {
        this.#transport.addEventListener('markdown/focus', () => {
            handler();
        });
    }
}

// Export singleton instance
export const markdownClient = new MarkdownClient();
