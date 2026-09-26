// Input API: User input notifications (keyboard shortcuts, link clicks, edit availability), and the shortcut
// keys a hosted page keeps from its WebView.

/**
 * Input events API.
 */
export class InputAPI {
    /** @type {import('../core/rpc-transport.js').RpcTransport} */
    #transport;

    /** @type {Function|null} */
    #reloadKeyHandler = null;

    /** @type {WeakSet<EventTarget>} */
    #shortcutKeyTargets = new WeakSet();

    /**
     * @param {import('../core/rpc-transport.js').RpcTransport} transport
     */
    constructor(transport) {
        this.#transport = transport;
    }

    /**
     * Registers the handler for F5, which the WebView would otherwise take as a reload of the whole page.
     * The client keeps the reload keys, F5, Ctrl+R and Ctrl+Shift+R, from reloading a hosted page with or
     * without a handler, because a reloaded page loses its state and its session with the host. A later
     * handler replaces an earlier one.
     * @param {Function} handler - Called with no arguments when F5 is pressed and nothing on the page took it.
     */
    onReloadKey(handler) {
        this.#reloadKeyHandler = typeof handler === 'function' ? handler : null;
    }

    /**
     * Keeps the shortcut keys working while they are pressed inside a document or window. The reload
     * keys never reload the page. On Windows the close shortcuts, Ctrl+W and Ctrl+Shift+W, go to the host,
     * since the WebView never passes a key typed in the page to the application. The client watches its own
     * page. A key pressed inside a same-origin frame never reaches the page around it, so call this for each
     * document the frame loads. Does nothing in a page opened outside the host, where the keys keep their
     * browser meaning.
     * @param {EventTarget} target - The document or window to watch.
     */
    watchShortcutKeys(target) {
        if (!this.#transport.isHosted ||
            typeof target?.addEventListener !== 'function' ||
            this.#shortcutKeyTargets.has(target)) {
            return;
        }

        this.#shortcutKeyTargets.add(target);

        // The key reaches this listener after the page's own handlers, so a control that takes one of these
        // keys for itself keeps it, as a terminal does with F5 and Ctrl+W.
        target.addEventListener('keydown', (event) => this.#onShortcutKeyDown(event));
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
     * Notifies the host that a global keyboard shortcut was pressed in the editor, so the host can route it
     * to IKeyboardShortcutService. The client already forwards the close shortcuts from every watched page.
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
     * @param {boolean} [availability.canHandleTab] - Whether Tab is the editor's to act on, so the host
     *   sends it over the bridge. Leave this false and Tab goes to the page natively, moving between the
     *   surface's own form fields.
     * @param {boolean} [availability.canFind] - Whether the editor runs a find of its own, so the host's
     *   Find menu item is offered and opens it. Leave this false and the host offers no find for this
     *   document.
     * @param {boolean} [availability.hostMediatedClipboard] - Whether the host performs this editor's cut,
     *   copy, and paste, exchanging plain text over `editor/getSelectedText` and `editor/insertText`. Leave
     *   this false to keep the platform's own clipboard, which a rich text editor needs to preserve its
     *   formatting; the host then stands aside for those three verbs.
     */
    notifyEditAvailability(availability = {}) {
        this.#transport.notify('input/editAvailabilityChanged', {
            canCopy: availability.canCopy === true,
            canCut: availability.canCut === true,
            canPaste: availability.canPaste === true,
            canSelectAll: availability.canSelectAll === true,
            canUndo: availability.canUndo === true,
            canRedo: availability.canRedo === true,
            canIndent: availability.canIndent === true,
            hostMediatedClipboard: availability.hostMediatedClipboard === true,
            canHandleTab: availability.canHandleTab === true,
            canFind: availability.canFind === true
        });
    }

    /**
     * Asks the host to perform an edit verb on this editor. A WebView cannot reach the clipboard on every
     * head, so an editor that draws its own menu calls this for the verbs it cannot run itself instead of
     * going to the clipboard directly. The host performs the same verb the menu bar and the keyboard
     * shortcut do.
     * @param {string} command - The editor command name: cut, copy, paste, selectAll, undo, or redo.
     * @returns {Promise<void>} Resolves once the verb has been applied.
     */
    requestEdit(command) {
        return this.#transport.request('input/requestEdit', { command });
    }

    #onShortcutKeyDown(event) {
        if (event.defaultPrevented) {
            return;
        }

        if (isReloadKey(event)) {
            event.preventDefault();

            if (event.key === 'F5' && !event.repeat) {
                this.#reloadKeyHandler?.();
            }
            return;
        }

        if (isCloseShortcut(event)) {
            event.preventDefault();

            if (!event.repeat) {
                this.notifyShortcut('W', { ctrl: true, shift: event.shiftKey });
            }
        }
    }
}

// The keys a browser takes as a reload of the page: F5 with or without a modifier, and Ctrl+R with or
// without Shift.
function isReloadKey(event) {
    if (event.key === 'F5') {
        return true;
    }

    return event.ctrlKey &&
        !event.altKey &&
        !event.metaKey &&
        typeof event.key === 'string' &&
        event.key.toLowerCase() === 'r';
}

// Ctrl+W, and Ctrl+Shift+W, which close the active document and every document in its section. macOS
// closes them on Command+W, which its web view never passes to a page, so there Control+W is the page's own.
function isCloseShortcut(event) {
    return event.ctrlKey &&
        !event.altKey &&
        !event.metaKey &&
        typeof event.key === 'string' &&
        event.key.toLowerCase() === 'w' &&
        !isMacOS();
}

function isMacOS() {
    const navigatorInfo = globalThis.navigator;
    const platform = navigatorInfo?.userAgentData?.platform ||
        navigatorInfo?.platform ||
        navigatorInfo?.userAgent ||
        '';
    return /mac/i.test(platform);
}
