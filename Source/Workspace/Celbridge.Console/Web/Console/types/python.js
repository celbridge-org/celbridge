// The python session type as the settings form sees it: the icon it shows on the rail, the markup its
// settings are edited through, and the keys those controls write. A session type is one file here the way
// it is one provider on the host side.

export default {
    typeId: 'python',
    icon: 'bi-filetype-py',

    // Injected into the type section while this type is selected, then localized. Each field below names
    // one of these controls by id.
    markup: `
        <label class="field">
            <span class="field-label" data-loc-key="Console_Field_PythonVersion"></span>
            <input id="python-version" type="text" spellcheck="false" data-loc-key="Console_Placeholder_PythonVersion" data-loc-attr="placeholder">
            <span class="field-hint" data-loc-key="Console_Hint_PythonVersion"></span>
        </label>

        <label class="field">
            <span class="field-label" data-loc-key="Console_Field_Dependencies"></span>
            <textarea id="dependencies" rows="3" spellcheck="false" data-loc-key="Console_Placeholder_Dependencies" data-loc-attr="placeholder"></textarea>
            <span class="field-hint" data-loc-key="Console_Hint_Dependencies"></span>
        </label>

        <label class="field">
            <span class="field-label" data-loc-key="Console_Field_Script"></span>
            <textarea id="python-script" rows="3" spellcheck="false" data-loc-key="Console_Placeholder_Script" data-loc-attr="placeholder"></textarea>
            <span class="field-hint" data-loc-key="Console_Hint_PythonScript"></span>
        </label>
    `,

    // The key each control holds in this type's [session.python] table, and how its text is read. Only the
    // fields listed here are read and written, so the table of the type left behind by a switch is never
    // rewritten from another type's controls. The script is a REPL script rather than a shell script, so
    // it keeps its own control and its own value.
    fields: [
        { id: 'python-version', key: 'python_version', kind: 'text' },
        { id: 'dependencies', key: 'dependencies', kind: 'lines' },
        { id: 'python-script', key: 'script', kind: 'script' },
    ],
};
