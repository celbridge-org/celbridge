// Dialog API: Operations that reach the user directly — native dialogs, and the notification centre.

// A dialog is answered by the user, who takes as long as they take, so the requests that open one wait
// without a timeout. The host answers when the dialog closes.
const WAITS_FOR_USER = { timeoutMs: null };

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
     * Opens the resource picker in open mode, with a preview of the selected image.
     * @param {string[]} extensions - Allowed file extensions (e.g., ['.png', '.jpg']).
     * @returns {Promise<string|null>} - The chosen file's path relative to the calling document, or null if
     *   cancelled. Use pickResource for a resource key.
     */
    async pickImage(extensions) {
        const result = await this.#transport.request('dialog/pickImage', { extensions }, WAITS_FOR_USER);
        return result.path;
    }

    /**
     * Opens the resource picker in open mode.
     * @param {string[]} extensions - Allowed file extensions.
     * @returns {Promise<string|null>} - The chosen file's path relative to the calling document, or null if
     *   cancelled. Use pickResource for a resource key.
     */
    async pickFile(extensions) {
        const result = await this.#transport.request('dialog/pickFile', { extensions }, WAITS_FOR_USER);
        return result.path;
    }

    /**
     * Asks the user to choose a file to open, a place to save a new file, or a folder, in the project. The picker
     * writes nothing. In save mode, the caller may write the returned key. The folder exists, the name is valid, the
     * extension is allowed and the file is not read-only. If the file exists, the user has agreed to replace it.
     *
     * The picker starts at `defaultResource`. Without one, the picker starts in the folder of the calling document.
     * A document outside the project starts the picker in the project folder. A `defaultResource` that is
     * malformed, outside the project, or in a folder that does not exist is ignored.
     *
     * @param {Object} options
     * @param {'open'|'save'|'folder'} options.mode - What the user chooses.
     * @param {string[]} [options.extensions] - Allowed extensions, for example ['.png']. Empty or left out for
     *   any. In save mode with one extension, the picker adds the extension to a name typed without it.
     * @param {string} [options.defaultResource] - Where to start. For 'save', a suggested folder and name, for
     *   example 'project:images/hero-export.png'. For 'open', a file to select. For 'folder', a folder to open.
     * @param {string} [options.title] - The dialog's title, already localized. Each mode has its own default.
     * @param {boolean} [options.preview] - Show a preview of image files. 'open' only.
     * @returns {Promise<string|null>} - The chosen resource key, for example 'project:images/hero-export.png', or
     *   null if cancelled.
     */
    async pickResource(options) {
        const result = await this.#transport.request('dialog/pickResource', {
            mode: options.mode,
            extensions: options.extensions,
            defaultResource: options.defaultResource,
            title: options.title,
            preview: options.preview ?? false
        }, WAITS_FOR_USER);
        return result.resource;
    }

    /**
     * Asks the user where to save a new file in the project. Opens the picker in save mode.
     * @param {string} [defaultResource] - A suggested folder and name, for example
     *   'project:images/hero-export.png'.
     * @param {string[]} [extensions] - Allowed extensions, for example ['.png'].
     * @returns {Promise<string|null>} - The resource key to write, or null if cancelled.
     */
    pickSaveLocation(defaultResource, extensions) {
        return this.pickResource({ mode: 'save', defaultResource, extensions });
    }

    /**
     * Asks the user to choose a folder in the project. Opens the picker in folder mode.
     * @param {string} [defaultResource] - A folder to start in, for example 'project:images'.
     * @returns {Promise<string|null>} - The chosen folder's resource key, or null if cancelled. The project folder
     *   is 'project:'.
     */
    pickFolder(defaultResource) {
        return this.pickResource({ mode: 'folder', defaultResource });
    }

    /**
     * Opens the icon picker over the icons the host supports.
     * @param {string} [searchText] - Text the picker opens its search on, usually the icon name the caller holds.
     * @returns {Promise<string|null>} - The chosen icon name, or null if cancelled.
     */
    async pickIcon(searchText) {
        const result = await this.#transport.request('dialog/pickIcon', { searchText }, WAITS_FOR_USER);
        return result.iconName;
    }

    /**
     * Shows an alert dialog.
     * @param {string} title - The alert title.
     * @param {string} message - The alert message.
     * @returns {Promise<void>}
     */
    async showAlert(title, message) {
        await this.#transport.request('dialog/showAlert', { title, message }, WAITS_FOR_USER);
    }

    /**
     * Adds a notification to the notification centre, the list behind the badge beside the Project
     * Switcher. Unlike the other methods here this tells the user something rather than asking them,
     * so it does not wait for a response.
     *
     * Best effort: the promise resolves when the host has taken the notification, not when the user
     * has seen it. Never treat it as an acknowledgement.
     *
     * @param {'info'|'warning'|'error'} severity - How serious it is. The same three
     *   values a report file uses, so a report severity passes straight through.
     * @param {string} message - One line, already localized by the caller. Only the first line is
     *   shown; detail belongs in a report.
     * @param {Object} [action] - Gives the notification a button that opens a document. Omit it and
     *   the notification carries no action.
     * @param {string} action.resource - Resource key of the document to open, such as the key
     *   document.writeReport returned.
     * @param {string} [action.label] - Text on the button, already localized. Defaults to the host's
     *   own wording for opening a report.
     * @param {number} [action.line] - One-based line to land on. Omit to open at the top.
     * @param {number} [action.column] - One-based column to land on.
     * @returns {Promise<void>}
     */
    async showNotification(severity, message, action) {
        await this.#transport.request('dialog/showNotification', {
            severity,
            message,
            resource: action?.resource,
            label: action?.label,
            line: action?.line ?? 0,
            column: action?.column ?? 0
        });
    }
}
