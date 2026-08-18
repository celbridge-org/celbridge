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
     * Shows a workspace toast. Unlike the other methods here this tells the user something rather
     * than asking them, so it does not wait for a response.
     *
     * Best effort: the promise resolves when the host has taken the toast, not when the user has
     * seen it. Never treat it as an acknowledgement.
     *
     * @param {'info'|'warning'|'error'} severity - How serious it is. The same three
     *   values a report file uses, so a report severity passes straight through.
     * @param {string} message - One line, already localized by the caller. Only the first line is
     *   shown; detail belongs in a report.
     * @param {Object} [action] - Gives the toast a button that opens a document. Omit it and the
     *   toast carries no action.
     * @param {string} action.resource - Resource key of the document to open, such as the key
     *   document.writeReport returned.
     * @param {string} [action.label] - Text on the button, already localized. Defaults to the host's
     *   own wording for opening a report.
     * @param {number} [action.line] - One-based line to land on. Omit to open at the top.
     * @param {number} [action.column] - One-based column to land on.
     * @returns {Promise<void>}
     */
    async toast(severity, message, action) {
        await this.#transport.request('dialog/toast', {
            severity,
            message,
            resource: action?.resource,
            label: action?.label,
            line: action?.line ?? 0,
            column: action?.column ?? 0
        });
    }
}
