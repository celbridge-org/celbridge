// Log API: writes diagnostics into the host application log.

/**
 * Application log API. Entries appear in the host's log named by the surface that reported them, alongside
 * the host's own diagnostics, which is what makes an editor's behaviour visible when something goes wrong in
 * a build with no debugger attached. Reporting is one way and best effort: nothing here returns a result or
 * affects the editor, and what becomes of an entry is the host's business.
 */
export class LogAPI {
    /** @type {import('../core/rpc-transport.js').RpcTransport} */
    #transport;

    /**
     * @param {import('../core/rpc-transport.js').RpcTransport} transport
     */
    constructor(transport) {
        this.#transport = transport;
    }

    /**
     * Logs a message for diagnosing editor behaviour. Not shown to the user.
     * @param {string} message
     */
    debug(message) {
        this.#write('debug', message);
    }

    /**
     * Logs a message worth seeing in a normal application log.
     * @param {string} message
     */
    info(message) {
        this.#write('info', message);
    }

    /**
     * Logs a recoverable problem.
     * @param {string} message
     */
    warn(message) {
        this.#write('warn', message);
    }

    /**
     * Logs a failure. Prefer this over a bare console.error for anything a user might report, because the
     * console buffer is only readable while a debugging session is attached.
     * @param {string} message
     * @param {Error} [error] - Appended with its stack when supplied.
     */
    error(message, error = null) {
        if (error === null || error === undefined) {
            this.#write('error', message);
            return;
        }

        // WebKit's Error.stack carries only the frames, so an error reported through the stack alone reaches
        // the log without the one line that says what went wrong. V8 prefixes the stack with the same text,
        // which is why it is only added when the stack does not already open with it.
        const name = error.name ?? 'Error';
        const reason = error.message ? `${name}: ${error.message}` : name;
        const stack = error.stack ?? '';

        let detail;
        if (!stack) {
            detail = `${message}: ${reason}`;
        } else if (stack.startsWith(name)) {
            detail = `${message}: ${stack}`;
        } else {
            detail = `${message}: ${reason}\n${stack}`;
        }

        this.#write('error', detail);
    }

    #write(level, message) {
        if (message === undefined || message === null) {
            return;
        }

        this.#transport.notify('host/log', { level, message: String(message) });
    }
}
