// The shell session type as the settings form sees it: the icon it shows on the rail, the markup its
// settings are edited through, and the keys those controls write.

export default {
    typeId: 'shell',
    icon: 'bi-terminal',

    // Injected into the type section while this type is selected, then localized. Each field below names
    // one of these controls by id.
    markup: `
        <label class="field">
            <span class="field-label" data-loc-key="Console_Field_Executable"></span>
            <input id="executable" type="text" spellcheck="false" data-loc-key="Console_Placeholder_Executable" data-loc-attr="placeholder">
            <span class="field-hint" data-loc-key="Console_Hint_Executable"></span>
        </label>

        <label class="field">
            <span class="field-label" data-loc-key="Console_Field_Arguments"></span>
            <textarea id="arguments" rows="2" spellcheck="false" data-loc-key="Console_Placeholder_Arguments" data-loc-attr="placeholder"></textarea>
            <span class="field-hint" data-loc-key="Console_Hint_Arguments"></span>
        </label>

        <label class="field">
            <span class="field-label" data-loc-key="Console_Field_Script"></span>
            <textarea id="shell-script" rows="3" spellcheck="false" data-loc-key="Console_Placeholder_Script" data-loc-attr="placeholder"></textarea>
            <span class="field-hint" data-loc-key="Console_Hint_ShellScript"></span>
        </label>
    `,

    // The key each control holds in this type's [session.shell] table, and how its text is read. Only the
    // fields listed here are read and written.
    fields: [
        { id: 'executable', key: 'executable', kind: 'text' },
        { id: 'arguments', key: 'arguments', kind: 'lines' },
        { id: 'shell-script', key: 'script', kind: 'script' },
    ],
};
