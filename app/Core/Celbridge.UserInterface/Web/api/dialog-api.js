// Dialog API: Operations for showing native dialogs.

/**
 * Dialog operations API.
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
}
