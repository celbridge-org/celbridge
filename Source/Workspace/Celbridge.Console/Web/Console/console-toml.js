// Minimal TOML parse/serialise for the constrained .console config shape. It handles the documented shape
// (single-line string and string-array values under [session], [session.options], [session.environment],
// plus [[session.runner]], [[session.trigger]] and [[session.shortcut]] array-of-tables). Unknown keys and
// sections are parsed and ignored; a malformed line raises a config error surfaced in the settings view.
// Comments are not preserved across a save.

/**
 * @typedef {Object} ConsoleRunner
 * @property {string[]} extensions
 * @property {string} command
 */

/**
 * @typedef {Object} ConsoleTrigger
 * @property {string} pattern
 * @property {string} command
 */

/**
 * @typedef {Object} ConsoleShortcut
 * @property {string} label
 * @property {string} icon
 * @property {string} text
 */

/**
 * @typedef {Object} ConsoleConfig
 * @property {string} type
 * @property {string} executable
 * @property {string} pythonVersion
 * @property {string[]} arguments
 * @property {string[]} dependencies
 * @property {string} workingDirectory
 * @property {string} startupScript
 * @property {Object<string,string>} environment
 * @property {ConsoleRunner[]} runners
 * @property {string[]} disabledExtensions file extensions the session type's own runners are ignored for
 * @property {ConsoleTrigger[]} triggers
 * @property {ConsoleShortcut[]} shortcuts
 */

/** Returns a fresh default shell config. */
export function defaultConsoleConfig() {
    return {
        type: 'shell',
        executable: '',
        pythonVersion: '',
        arguments: [],
        dependencies: [],
        workingDirectory: '',
        startupScript: '',
        environment: {},
        runners: [],
        disabledExtensions: [],
        triggers: [],
        shortcuts: [],
    };
}

/**
 * Parses .console TOML into a ConsoleConfig. Throws Error with a human-readable message on malformed input.
 * @param {string} text
 * @returns {ConsoleConfig}
 */
export function parseConsoleToml(text) {
    const config = defaultConsoleConfig();

    let section = '';
    let currentTable = null;
    const lines = (text || '').split(/\r?\n/);

    for (let index = 0; index < lines.length; index++) {
        // A multi-line block is read before comment stripping, so a '#' inside a script survives.
        const blockMatch = lines[index].match(/^\s*([^=\[]+?)\s*=\s*('''|\"\"\")(.*)$/);
        if (blockMatch && !blockMatch[3].includes(blockMatch[2])) {
            const blockKey = parseKey(blockMatch[1].trim());
            const delimiter = blockMatch[2];
            const collected = [];

            // TOML drops a newline immediately after the opening delimiter, so only same-line content counts.
            if (blockMatch[3] !== '') {
                collected.push(blockMatch[3]);
            }

            let closed = false;
            while (++index < lines.length) {
                const closeIndex = lines[index].indexOf(delimiter);
                if (closeIndex >= 0) {
                    if (closeIndex > 0) {
                        collected.push(lines[index].slice(0, closeIndex));
                    }
                    closed = true;
                    break;
                }
                collected.push(lines[index]);
            }

            if (!closed) {
                throw new Error(`Unterminated ${delimiter} block in .console config`);
            }

            // The block's content is taken verbatim (no escape processing), so requote it for assignValue.
            assignValue(config, section, currentTable, blockKey, quote(collected.join('\n')));
            continue;
        }

        const line = stripComment(lines[index]).trim();
        if (line === '') {
            continue;
        }

        // An [[array.of.tables]] header appends a new element to a repeated table.
        const arrayMatch = line.match(/^\[\[(.+)\]\]$/);
        if (arrayMatch) {
            section = arrayMatch[1].trim();
            currentTable = beginArrayTable(config, section);
            continue;
        }

        const sectionMatch = line.match(/^\[(.+)\]$/);
        if (sectionMatch) {
            section = sectionMatch[1].trim();
            currentTable = null;
            continue;
        }

        const equalsIndex = line.indexOf('=');
        if (equalsIndex < 0) {
            throw new Error(`Invalid line in .console config: "${line}"`);
        }

        const key = parseKey(line.slice(0, equalsIndex).trim());
        const rawValue = line.slice(equalsIndex + 1).trim();
        assignValue(config, section, currentTable, key, rawValue);
    }

    return config;
}

/**
 * Serialises a ConsoleConfig back to .console TOML. Only non-empty optional fields are emitted.
 * @param {ConsoleConfig} config
 * @returns {string}
 */
export function serializeConsoleToml(config) {
    const lines = [];

    lines.push('[session]');
    lines.push(`type = ${quote(config.type || 'shell')}`);
    if (config.workingDirectory) {
        lines.push(`working_directory = ${quote(config.workingDirectory)}`);
    }
    if (config.startupScript) {
        lines.push(`startup_script = ${quoteScript(config.startupScript)}`);
    }
    if (config.disabledExtensions && config.disabledExtensions.length > 0) {
        lines.push(`disabled_extensions = [${config.disabledExtensions.map(quote).join(', ')}]`);
    }

    lines.push('');
    lines.push('[session.options]');
    if (config.executable) {
        lines.push(`executable = ${quote(config.executable)}`);
    }
    if (config.pythonVersion) {
        lines.push(`python_version = ${quote(config.pythonVersion)}`);
    }
    if (config.arguments && config.arguments.length > 0) {
        lines.push(`arguments = [${config.arguments.map(quote).join(', ')}]`);
    }
    if (config.dependencies && config.dependencies.length > 0) {
        lines.push(`dependencies = [${config.dependencies.map(quote).join(', ')}]`);
    }

    lines.push('');
    lines.push('[session.environment]');
    for (const [name, value] of Object.entries(config.environment || {})) {
        lines.push(`${serializeKey(name)} = ${quote(value)}`);
    }

    for (const runner of config.runners || []) {
        lines.push('');
        lines.push('[[session.runner]]');
        lines.push(`extensions = [${(runner.extensions || []).map(quote).join(', ')}]`);
        lines.push(`command = ${quote(runner.command || '')}`);
    }

    for (const trigger of config.triggers || []) {
        lines.push('');
        lines.push('[[session.trigger]]');
        lines.push(`pattern = ${quote(trigger.pattern || '')}`);
        lines.push(`command = ${quote(trigger.command || '')}`);
    }

    for (const shortcut of config.shortcuts || []) {
        lines.push('');
        lines.push('[[session.shortcut]]');
        lines.push(`label = ${quote(shortcut.label || '')}`);
        if (shortcut.icon) {
            lines.push(`icon = ${quote(shortcut.icon)}`);
        }
        lines.push(`text = ${quote(shortcut.text || '')}`);
    }

    return lines.join('\n') + '\n';
}

// Appends a fresh element to the array the [[header]] names and returns it, so subsequent keys fill it.
// Unknown array tables get a throwaway object so their keys are parsed and ignored.
function beginArrayTable(config, section) {
    if (section === 'session.runner') {
        const runner = { extensions: [], command: '' };
        config.runners.push(runner);
        return runner;
    }

    if (section === 'session.trigger') {
        const trigger = { pattern: '', command: '' };
        config.triggers.push(trigger);
        return trigger;
    }

    if (section === 'session.shortcut') {
        const shortcut = { label: '', icon: '', text: '' };
        config.shortcuts.push(shortcut);
        return shortcut;
    }

    return {};
}

function assignValue(config, section, currentTable, key, rawValue) {
    if (section === 'session') {
        if (key === 'type') {
            config.type = parseScalar(rawValue);
        } else if (key === 'working_directory') {
            config.workingDirectory = parseScalar(rawValue);
        } else if (key === 'startup_script') {
            config.startupScript = parseScalar(rawValue);
        } else if (key === 'disabled_extensions') {
            config.disabledExtensions = parseArray(rawValue);
        }
        return;
    }

    if (section === 'session.options') {
        if (key === 'executable') {
            config.executable = parseScalar(rawValue);
        } else if (key === 'python_version') {
            config.pythonVersion = parseScalar(rawValue);
        } else if (key === 'arguments') {
            config.arguments = parseArray(rawValue);
        } else if (key === 'dependencies') {
            config.dependencies = parseArray(rawValue);
        }
        return;
    }

    if (section === 'session.environment') {
        config.environment[key] = parseScalar(rawValue);
        return;
    }

    if (section === 'session.runner' && currentTable) {
        if (key === 'extensions') {
            currentTable.extensions = parseArray(rawValue);
        } else if (key === 'command') {
            currentTable.command = parseScalar(rawValue);
        }
        return;
    }

    if (section === 'session.trigger' && currentTable) {
        if (key === 'pattern') {
            currentTable.pattern = parseScalar(rawValue);
        } else if (key === 'command') {
            currentTable.command = parseScalar(rawValue);
        }
        return;
    }

    if (section === 'session.shortcut' && currentTable) {
        if (key === 'label') {
            currentTable.label = parseScalar(rawValue);
        } else if (key === 'icon') {
            currentTable.icon = parseScalar(rawValue);
        } else if (key === 'text') {
            currentTable.text = parseScalar(rawValue);
        }
        return;
    }
}

// Cuts a line at its first unquoted '#'. A '#' inside a quoted value (either quote style) is preserved.
// Inside a double-quoted string a backslash escapes the next character; a literal (single-quoted) string
// has no escapes.
function stripComment(line) {
    let quoteChar = null;
    for (let index = 0; index < line.length; index++) {
        const character = line[index];
        if (quoteChar === '"' && character === '\\') {
            index++;
        } else if (quoteChar !== null) {
            if (character === quoteChar) {
                quoteChar = null;
            }
        } else if (character === '"' || character === "'") {
            quoteChar = character;
        } else if (character === '#') {
            return line.slice(0, index);
        }
    }
    return line;
}

// A quoted key (emitted for environment names that are not TOML bare keys) is unquoted like a scalar.
function parseKey(rawKey) {
    if ((rawKey.startsWith('"') && rawKey.endsWith('"') && rawKey.length >= 2) ||
        (rawKey.startsWith("'") && rawKey.endsWith("'") && rawKey.length >= 2)) {
        return parseScalar(rawKey);
    }
    return rawKey;
}

function parseScalar(rawValue) {
    if (rawValue.startsWith('"') && rawValue.endsWith('"') && rawValue.length >= 2) {
        return unescapeBasicString(rawValue.slice(1, -1));
    }
    // TOML literal (single-quoted) strings take their content verbatim, with no escape processing. Used for
    // values that contain double quotes, such as a runner command like: '%run "{resource}"'.
    if (rawValue.startsWith("'") && rawValue.endsWith("'") && rawValue.length >= 2) {
        return rawValue.slice(1, -1);
    }
    // A bare (unquoted) value is returned verbatim, so numbers and booleans round-trip as text.
    return rawValue;
}

// Unescapes a double-quoted string body in one pass, so an unescaped backslash never pairs with the
// character an earlier escape produced. Escapes other than \" and \\ pass through verbatim.
function unescapeBasicString(inner) {
    let result = '';
    for (let index = 0; index < inner.length; index++) {
        const character = inner[index];
        if (character === '\\' && index + 1 < inner.length) {
            const next = inner[index + 1];
            if (next === '"' || next === '\\') {
                result += next;
                index++;
                continue;
            }
        }
        result += character;
    }
    return result;
}

function parseArray(rawValue) {
    if (!rawValue.startsWith('[') || !rawValue.endsWith(']')) {
        throw new Error(`Invalid array value in .console config: "${rawValue}"`);
    }

    const inner = rawValue.slice(1, -1).trim();
    if (inner === '') {
        return [];
    }

    return splitTopLevel(inner).map((item) => parseScalar(item.trim()));
}

// Splits an array body on commas that sit outside a quoted string (either quote style). Inside a
// double-quoted string a backslash escapes the next character.
function splitTopLevel(inner) {
    const items = [];
    let current = '';
    let quoteChar = null;

    for (let index = 0; index < inner.length; index++) {
        const character = inner[index];
        if (quoteChar === '"' && character === '\\') {
            current += character;
            index++;
            if (index < inner.length) {
                current += inner[index];
            }
        } else if (quoteChar !== null) {
            if (character === quoteChar) {
                quoteChar = null;
            }
            current += character;
        } else if (character === '"' || character === "'") {
            quoteChar = character;
            current += character;
        } else if (character === ',') {
            items.push(current);
            current = '';
        } else {
            current += character;
        }
    }

    if (current.trim() !== '') {
        items.push(current);
    }

    return items;
}

// A multi-line script is emitted as a TOML literal block, which needs no escaping and stays readable in
// the file. A script containing the delimiter itself falls back to a single-line escaped string.
function quoteScript(value) {
    const text = String(value);
    if (text.includes('\n') && !text.includes("'''")) {
        return `'''\n${text}\n'''`;
    }
    return quote(text);
}

function quote(value) {
    const escaped = String(value).replace(/\\/g, '\\\\').replace(/"/g, '\\"');
    return `"${escaped}"`;
}

// An environment name that is not a TOML bare key (letters, digits, underscore, dash) is emitted quoted so
// it survives the round trip.
function serializeKey(name) {
    if (/^[A-Za-z0-9_-]+$/.test(name)) {
        return name;
    }
    return quote(name);
}
