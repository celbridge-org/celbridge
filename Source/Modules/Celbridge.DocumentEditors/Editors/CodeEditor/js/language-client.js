// The page's connection to the language server of its document. The host is the server's one Language Server
// Protocol client, and the page reaches the server through the host, over the page's bridge. Each message has the
// protocol's own shape. The host fills in the document's URI and language.

// The bridge methods of the language channel. Mirrored by LanguageChannelRpcMethods in the host's language servers
// module.
const openMethod = 'language/open';
const sendMethod = 'language/send';
const closeMethod = 'language/close';

// The host's own messages, beside the protocol's. Mirrored by PageMessages in the host's
// language servers module.
const serverReadyMethod = 'celbridge/serverReady';
const serverUnavailableMethod = 'celbridge/serverUnavailable';
const publishDiagnosticsMethod = 'textDocument/publishDiagnostics';

// How the server wants a document's changes, in a server's textDocumentSync capability.
const fullSync = 1;
const incrementalSync = 2;

const defaultReconnectDelayMs = 1000;

/**
 * The page's language channel on the bridge. One connection is open at a time. The channel passes the host's
 * messages to the receiver that opened the connection. A receiver has two methods: `handleMessage(message)` and
 * `handleClosed()`.
 */
export class LanguageChannel {
    #bridge;
    #receiver = null;

    /**
     * @param {Object} bridge - The page's bridge client to the host.
     */
    constructor(bridge) {
        this.#bridge = bridge;

        bridge.onNotification('language/message', (params) => {
            this.#receiver?.handleMessage(JSON.parse(params.message));
        });

        bridge.onNotification('language/closed', () => {
            this.#receiver?.handleClosed();
        });
    }

    /**
     * Opens a connection to the document's language server, in place of any connection already open.
     * @param {Object} receiver - Gets the host's messages on the connection.
     */
    open(receiver) {
        this.#receiver = receiver;
        this.#bridge.sendNotification(openMethod, {});
    }

    /**
     * Sends one message on the open connection.
     * @param {Object} message - A message in the protocol's shape.
     */
    send(message) {
        this.#bridge.sendNotification(sendMethod, { message: JSON.stringify(message) });
    }

    /**
     * Closes the receiver's connection. Only the receiver of the open connection can close it.
     * @param {Object} receiver - The receiver that opened the connection.
     */
    close(receiver) {
        if (this.#receiver !== receiver) {
            return;
        }

        this.#receiver = null;
        this.#bridge.sendNotification(closeMethod, {});
    }
}

export class LanguageClient {
    #model;
    #channel;
    #onReady;
    #onDiagnostics;
    #onUnavailable;
    #onDisconnected;
    #reconnectDelayMs;

    #isOpen = false;
    #capabilities = null;
    #isSynced = false;
    #isDisposed = false;
    #isUnavailable = false;
    #nextRequestId = 1;
    #pendingRequests = new Map();
    #reconnectTimer = null;
    #changeListener = null;

    /**
     * @param {Object} options
     * @param {Object} options.model - The Monaco model of the page's document.
     * @param {Object} options.channel - The page's language channel, with `open`, `send` and `close`.
     * @param {Function} options.onReady - Called with the server's capabilities each time a connection is ready.
     * @param {Function} options.onDiagnostics - Called with the document's diagnostics, in the protocol's shape.
     * @param {Function} options.onUnavailable - Called once when the host reports that no server will run.
     * @param {Function} options.onDisconnected - Called each time the host closes a connection.
     * @param {number} [options.reconnectDelayMs] - The wait before a closed connection is opened again.
     */
    constructor({ model, channel, onReady, onDiagnostics, onUnavailable, onDisconnected, reconnectDelayMs }) {
        this.#model = model;
        this.#channel = channel;
        this.#onReady = onReady ?? (() => {});
        this.#onDiagnostics = onDiagnostics ?? (() => {});
        this.#onUnavailable = onUnavailable ?? (() => {});
        this.#onDisconnected = onDisconnected ?? (() => {});
        this.#reconnectDelayMs = reconnectDelayMs ?? defaultReconnectDelayMs;
    }

    /**
     * Opens the connection, and starts sending the document's changes.
     */
    connect() {
        if (this.#isDisposed) {
            return;
        }

        this.#changeListener ??= this.#model.onDidChangeContent((event) => this.#sendChange(event));

        this.#isOpen = true;
        this.#isSynced = false;
        this.#channel.open(this);
    }

    /**
     * Sends a request about the document, and returns the server's result. Returns null in three cases: the
     * server fails the request, Monaco cancels the request, or the connection closes first.
     * @param {string} method - The protocol's method name.
     * @param {Object} params - The request's parameters, without the document's URI.
     * @param {Object} [cancellationToken] - Monaco's cancellation token for the request.
     * @returns {Promise<any>}
     */
    sendRequest(method, params, cancellationToken) {
        if (!this.#isSynced) {
            return Promise.resolve(null);
        }

        const id = this.#nextRequestId++;

        return new Promise((resolve) => {
            const cancellation = cancellationToken?.onCancellationRequested?.(() => {
                if (this.#pendingRequests.delete(id)) {
                    this.#send({ method: '$/cancelRequest', params: { id } });
                    resolve(null);
                }
            });

            this.#pendingRequests.set(id, { resolve, cancellation });
            this.#send({ id, method, params });
        });
    }

    /**
     * Closes the connection for good. Every waiting request returns null.
     */
    dispose() {
        this.#isDisposed = true;
        this.#changeListener?.dispose();
        this.#changeListener = null;

        if (this.#reconnectTimer !== null) {
            clearTimeout(this.#reconnectTimer);
            this.#reconnectTimer = null;
        }

        this.#isOpen = false;
        this.#isSynced = false;
        this.#channel.close(this);

        this.#failPendingRequests();
    }

    /**
     * Handles one message from the host on the open connection. The channel calls this method.
     * @param {Object} message - A message in the protocol's shape.
     */
    handleMessage(message) {
        if (!this.#isOpen) {
            return;
        }

        if (message.method === serverReadyMethod) {
            this.#capabilities = message.params?.capabilities ?? {};

            // The server gets the whole buffer on every connection, so it always has the current text.
            this.#send({
                method: 'textDocument/didOpen',
                params: { text: this.#model.getValue(), version: this.#model.getVersionId() }
            });
            this.#isSynced = true;

            this.#onReady(this.#capabilities);
            return;
        }

        if (message.method === serverUnavailableMethod) {
            this.#isUnavailable = true;
            this.#onUnavailable();
            return;
        }

        if (message.method === publishDiagnosticsMethod) {
            this.#onDiagnostics(message.params?.diagnostics ?? []);
            return;
        }

        if (message.id !== undefined && message.method === undefined) {
            const pending = this.#pendingRequests.get(message.id);
            if (!pending) {
                return;
            }

            this.#pendingRequests.delete(message.id);
            pending.cancellation?.dispose?.();
            pending.resolve(message.error ? null : (message.result ?? null));
        }
    }

    /**
     * Handles the host closing the connection, which happens when the server stops. The channel calls this
     * method. The client opens a new connection after a wait.
     */
    handleClosed() {
        if (!this.#isOpen) {
            return;
        }

        this.#isOpen = false;
        this.#isSynced = false;
        this.#failPendingRequests();
        this.#onDisconnected();

        if (!this.#isDisposed && !this.#isUnavailable) {
            this.#reconnectTimer = setTimeout(() => {
                this.#reconnectTimer = null;
                this.connect();
            }, this.#reconnectDelayMs);
        }
    }

    #sendChange(event) {
        if (!this.#isSynced) {
            return;
        }

        const syncKind = getSyncKind(this.#capabilities);
        if (syncKind !== fullSync && syncKind !== incrementalSync) {
            return;
        }

        // A change that replaces the whole buffer is sent as the whole text.
        let contentChanges;
        if (syncKind === fullSync || event.isFlush) {
            contentChanges = [{ text: this.#model.getValue() }];
        } else {
            contentChanges = event.changes.map((change) => ({
                range: {
                    start: { line: change.range.startLineNumber - 1, character: change.range.startColumn - 1 },
                    end: { line: change.range.endLineNumber - 1, character: change.range.endColumn - 1 }
                },
                rangeLength: change.rangeLength,
                text: change.text
            }));
        }

        this.#send({
            method: 'textDocument/didChange',
            params: { version: event.versionId, contentChanges }
        });
    }

    #send(message) {
        if (this.#isOpen) {
            this.#channel.send({ jsonrpc: '2.0', ...message });
        }
    }

    #failPendingRequests() {
        for (const pending of this.#pendingRequests.values()) {
            pending.cancellation?.dispose?.();
            pending.resolve(null);
        }
        this.#pendingRequests.clear();
    }
}

// A server names the kind directly, or inside an options object.
function getSyncKind(capabilities) {
    const sync = capabilities?.textDocumentSync;
    if (typeof sync === 'number') {
        return sync;
    }

    return sync?.change ?? 0;
}
