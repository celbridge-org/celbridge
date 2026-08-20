// Input API: User input notifications (keyboard shortcuts, link clicks, edit availability).

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
     * Notifies the host that a link was clicked in the document. The host resolves the href against the
     * document's folder: a link that resolves to a project resource opens as a document, and one that does
     * not opens in the default browser.
     * @param {string} href - The href of the clicked link.
     */
    notifyLinkClicked(href) {
        this.#transport.notify('input/linkClicked', { href });
    }

    /**
     * Notifies the host that a global keyboard shortcut was pressed in the editor.
     * Use this to forward Celbridge-level shortcuts (e.g. Ctrl+W) when focus
     * is inside the editor so the host can route them to IKeyboardShortcutService.
     * @param {string} key - The key name (e.g. "W", "F11").
     * @param {Object} [modifiers] - Modifier key state.
     * @param {boolean} [modifiers.ctrl] - Whether Ctrl (or Cmd on macOS) is pressed.
     * @param {boolean} [modifiers.shift] - Whether Shift is pressed.
     * @param {boolean} [modifiers.alt] - Whether Alt (or Option on macOS) is pressed.
     */
    notifyShortcut(key, modifiers = {}) {
        this.#transport.notify('input/keyboardShortcut', {
            key,
            ctrlKey: modifiers.ctrl === true,
            shiftKey: modifiers.shift === true,
            altKey: modifiers.alt === true
        });
    }

    /**
     * Reports which standard edit verbs the editor can currently perform, so the host can drive menu
     * enable state. Send this whenever the editor's selection changes.
     * @param {Object} availability - The current edit availability.
     * @param {boolean} [availability.canCopy]
     * @param {boolean} [availability.canCut]
     * @param {boolean} [availability.canPaste]
     * @param {boolean} [availability.canSelectAll]
     * @param {boolean} [availability.canUndo]
     * @param {boolean} [availability.canRedo]
     * @param {boolean} [availability.canIndent] - Whether the editor indents on Tab, so the host keeps Tab
     *   inside the editor instead of letting it move focus.
     */
    notifyEditAvailability(availability = {}) {
        this.#transport.notify('input/editAvailabilityChanged', {
            canCopy: availability.canCopy === true,
            canCut: availability.canCut === true,
            canPaste: availability.canPaste === true,
            canSelectAll: availability.canSelectAll === true,
            canUndo: availability.canUndo === true,
            canRedo: availability.canRedo === true,
            canIndent: availability.canIndent === true
        });
    }
}
