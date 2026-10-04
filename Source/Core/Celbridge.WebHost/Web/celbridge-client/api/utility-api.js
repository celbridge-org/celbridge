// Utility API: what a utility editor shows on its rail button.

/**
 * @typedef {'none'|'danger'|'caution'|'success'|'accent'} UtilityIndicatorTone
 */

/**
 * A utility is often out of sight, in the Utility Panel or behind another tab, but its rail button is always on
 * screen. A state the user must stay aware of while working elsewhere (recording, a found problem) belongs on
 * the button. Only a utility editor has one; the calls reject for a document editor.
 *
 * ```js
 * await client.utility.setIndicator('danger', 'Recording', 'bs-record-circle-fill');   // red, filled, a pop
 * await client.utility.clearIndicator();
 * ```
 *
 * The indicator is cleared when the page reloads, so a page that restores a state sets it again.
 */
export class UtilityAPI {
    /** @type {import('../core/rpc-transport.js').RpcTransport} */
    #transport;

    /**
     * @param {import('../core/rpc-transport.js').RpcTransport} transport
     */
    constructor(transport) {
        this.#transport = transport;
    }

    /**
     * Marks the rail button with a tone. Turning a tone on plays a brief pop and flash, so the moment is hard
     * to miss; changing the label alone does not.
     * @param {UtilityIndicatorTone} tone - 'danger' (red), 'caution' (amber), 'success' (green), 'accent', or
     *   'none' to clear.
     * @param {string} [label] - A short state word added to the button's tooltip, e.g. 'Recording'.
     * @param {string} [icon] - A prefixed icon name shown in place of the manifest's while the indicator is on.
     *   A filled variant of an outline icon reads far better in colour, e.g. 'bs-record-circle-fill'.
     * @returns {Promise<void>}
     */
    async setIndicator(tone, label = '', icon = '') {
        await this.#transport.request('utility/setIndicator', { tone, label, icon });
    }

    /**
     * Clears the rail button's indicator.
     * @returns {Promise<void>}
     */
    async clearIndicator() {
        await this.#transport.request('utility/setIndicator', { tone: 'none', label: '', icon: '' });
    }
}
