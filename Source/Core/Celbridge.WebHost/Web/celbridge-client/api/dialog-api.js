// Dialog API: Operations that reach the user directly — native dialogs, and the workspace toast.

/**
 * Operations that reach the user directly.
 */
export class DialogAPI {
    /** @type {import('../core/rpc-transport.js').RpcTransport} */
    #transport;

    /**
     * @param {import('../core/rpc-transport.js').RpcTransport} transport
     */
    constructor(transport) {
        this.#transport = transport;
    }

    /**
     * Opens an image picker dialog.
     * @param {string[]} extensions - Allowed file extensions (e.g., ['.png', '.jpg']).
     * @returns {Promise<string|null>} - The selected path, or null if cancelled.
     */
    async pickImage(extensions) {
        const result = await this.#transport.request('dialog/pickImage', { extensions });
        return result.path;
    }

    /**
     * Opens a file picker dialog.
     * @param {string[]} extensions - Allowed file extensions.
     * @returns {Promise<string|null>} - The selected path, or null if cancelled.
     */
    async pickFile(extensions) {
        const result = await this.#transport.request('dialog/pickFile', { extensions });
        return result.path;
    }

    /**
     * Shows an alert dialog.
     * @param {string} title - The alert title.
     * @param {string} message - The alert message.
     * @returns {Promise<void>}
     */
    async alert(title, message) {
        await this.#transport.request('dialog/alert', { title, message });
    }

    /**
     * Raises a workspace notification. Unlike the other methods here this tells the user something
     * rather than asking them, so it does not wait for a response.
     *
     * Best effort: the promise resolves when the host has taken the notification, not when the user
     * has seen it. Never treat it as an acknowledgement.
     *
     * @param {'info'|'warning'|'error'} severity - How serious the notification is. The same three
     *   values a report file uses, so a report severity passes straight through.
     * @param {string} message - One line, already localized by the caller. Only the first line is
     *   shown; detail belongs in a report.
     * @returns {Promise<void>}
     */
    async notify(severity, message) {
        await this.#transport.request('dialog/notify', { severity, message });
    }
}
