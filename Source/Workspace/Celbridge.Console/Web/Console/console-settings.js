// The console's settings surface: the .console file as a form. The selected session type decides which
// fields the Type section offers and which built-in runners are listed. Everything below it is common to
// every type. The surface also carries the console's status, since a config that has moved on from the one
// the live session was launched from is only fixable here.

import { t, applyLocalization } from '/assets/celbridge-client/localization.js';
import { attachSectionSwitcher } from '/assets/celbridge-client/ui/section-switcher.js';
import { createCardList } from '/assets/celbridge-client/ui/card-list.js';
import { createIconField, resolveIconClass } from '/assets/celbridge-client/ui/icon-field.js';
import { parseConsoleToml, serializeConsoleToml, defaultConsoleConfig } from './console-toml.js';
import {
    splitLines,
    parseEnvironmentLines,
    parseExtensionList,
    configsEqual,
} from './console-config.js';
import shellType from './types/shell.js';
import pythonType from './types/python.js';

// The glyph for a shortcut with no icon, or one the bundled icon set does not carry.
const SHORTCUT_FALLBACK_ICON = 'bs-lightning-charge';

// The session types this client can edit, in the order the Type control offers them. A type is one module
// under types/, carrying the icon its rail row shows and the fields the form edits it through.
const typeModules = new Map([shellType, pythonType].map((typeModule) => [typeModule.typeId, typeModule]));

// A type needs a module to be offered.
const clientTypeIds = Array.from(typeModules.keys());

// Binds the settings surface to the page. The session reports what it launched from and whether it failed.
// Nothing else about it reaches here.
export function createConsoleSettings({ client }) {
    const pip = document.getElementById('pip');
    const shortcutRail = document.getElementById('shortcut-rail');
    const shortcutSeparator = document.getElementById('shortcut-separator');
    const settingsView = document.getElementById('settings-view');
    const sessionTypeSelect = document.getElementById('session-type');
    const typeNavItem = document.getElementById('type-nav-item');
    const typeNavIcon = document.getElementById('type-nav-icon');
    const typeNavLabel = document.getElementById('type-nav-label');
    const typeSectionTitle = document.getElementById('type-section-title');
    const typeSectionDescription = document.getElementById('type-section-description');
    const typeFieldsElement = document.getElementById('type-fields');
    const workingDirectoryInput = document.getElementById('working-directory');
    const environmentInput = document.getElementById('environment');
    const reopenSettingsButton = document.getElementById('reopen-settings');
    const builtInRunnerList = document.getElementById('runner-built-in');
    const builtInRunnerTemplate = document.getElementById('built-in-runner-template');

    const settingsSwitcher = attachSectionSwitcher(document.getElementById('settings-switcher'));

    // State. currentConfig mirrors the settings form / .console file. launchedConfig is the config the live
    // session was started from, so the pip can flag "changed, needs a reopen".
    let currentConfig = defaultConsoleConfig();
    let launchedConfig = null;
    let configError = null;
    // What the terminal's failure overlay is saying, so the settings surface can say it too while the
    // terminal is the hidden half of the row. Null while a session is running.
    let sessionFailure = null;
    // The registered session types, in the order the form offers them, each carrying the keys it accepts and
    // the runners it contributes. Null until an attach reports them.
    let hostSessionTypes = null;
    // The ids of the built-in runners switched off for this console.
    let disabledBuiltInRunners = [];

    // The selected type's fields, resolved from the markup applyType injected. Each entry pairs a control
    // with the key it holds in the type's [session.<type>] table.
    let typeFields = [];

    // The type section is one slot the selected type fills, not a section per type.
    function applyType(type) {
        const label = typeLabel(type);
        const typeModule = typeModules.get(type) || null;

        typeNavLabel.textContent = label;
        typeNavItem.title = label;
        typeNavItem.setAttribute('aria-label', label);
        typeNavIcon.className = `bi ${typeModule?.icon || 'bi-terminal'}`;

        typeSectionTitle.textContent = label;
        typeSectionDescription.textContent = localizedTypeString('Console_Desc_', type, '');

        typeFieldsElement.innerHTML = typeModule ? typeModule.markup : '';
        applyLocalization(typeFieldsElement);

        typeFields = (typeModule?.fields || []).map((field) => ({
            ...field,
            input: typeFieldsElement.querySelector(`#${field.id}`),
        }));

        // These controls arrive after the switcher's blanket read-only pass, so each takes the document's
        // state and its dirty-marking listener as it is bound.
        const readOnly = !isFormEditable();
        for (const field of typeFields) {
            field.input.disabled = readOnly;
            field.input.addEventListener('input', onFormInput);
        }
    }

    // A type with no string of its own shows its raw id.
    function typeLabel(type) {
        return localizedTypeString('Console_Type_', type, type);
    }

    // A type id is lowercase and the resource keys are title-cased, so the id is capitalized to name its
    // key. t() returns the key itself when it does not resolve, which is what stands in for "no string
    // defined".
    function localizedTypeString(prefix, type, fallback) {
        const key = `${prefix}${type.charAt(0).toUpperCase()}${type.slice(1)}`;
        const value = t(key);

        return value === key ? fallback : value;
    }

    function findSessionType(typeId) {
        return (hostSessionTypes || []).find((sessionType) => sessionType.typeId === typeId) || null;
    }

    // The Type options, offering the types the host reports as registered that this client also has fields
    // for, in the host's order. Before the attach that carries that list, the client's own set stands in.
    function renderSessionTypeOptions() {
        const offered = hostSessionTypes === null
            ? clientTypeIds
            : hostSessionTypes
                .map((sessionType) => sessionType.typeId)
                .filter((typeId) => clientTypeIds.includes(typeId));

        const selected = sessionTypeSelect.value;
        sessionTypeSelect.replaceChildren();

        for (const typeId of offered) {
            const option = document.createElement('option');
            option.value = typeId;
            option.textContent = typeLabel(typeId);
            sessionTypeSelect.appendChild(option);
        }

        sessionTypeSelect.value = selected;
    }

    // True when the document names a type this client has no fields for, which makes the form read-only.
    function isUnknownSessionType() {
        return !clientTypeIds.includes(currentConfig.type || 'shell');
    }

    // A stored option value as its control shows it. A value is typed by the TOML it was written as, not by
    // the field, so one of the wrong shape shows blank.
    function fieldText(value, kind) {
        if (kind === 'lines') {
            if (!Array.isArray(value)) {
                return '';
            }

            return value.filter((entry) => typeof entry === 'string' && entry.trim() !== '').join('\n');
        }

        if (typeof value !== 'string') {
            return '';
        }

        return value;
    }

    function populateForm(config) {
        const type = config.type || 'shell';
        const options = (config.optionsBySessionType || {})[type] || {};

        // The option labels need the localized strings, which arrive with the host handshake that also
        // delivers the first config.
        renderSessionTypeOptions();

        sessionTypeSelect.value = type;
        applyType(type);

        for (const field of typeFields) {
            field.input.value = fieldText(options[field.key], field.kind);
        }

        workingDirectoryInput.value = config.workingDirectory || '';
        environmentInput.value = Object.entries(config.environment || {})
            .map(([name, value]) => `${name}=${value}`)
            .join('\n');
        disabledBuiltInRunners = config.disabledBuiltInRunners || [];
        runnerCards.populate(config.runners);
        triggerCards.populate(config.triggers);
        shortcutCards.populate(config.shortcuts);
        renderShortcutRail();
    }

    // The runners the selected session type provides, shown above the console's own so the Run menu's
    // behaviour is visible in the form. They are not part of the config: the host layers them under whatever
    // the file declares, and re-reads them from the provider on every launch. Switching one off is the
    // exception: the config names it by id, which is what the host resolves against.
    function renderBuiltInRunners() {
        const runners = findSessionType(sessionTypeSelect.value)?.builtInRunners || [];
        builtInRunnerList.replaceChildren();

        for (const runner of runners) {
            const card = builtInRunnerTemplate.content.firstElementChild.cloneNode(true);
            applyLocalization(card);

            const extensionList = (runner.extensions || []).join(', ');
            card.querySelector('.cel-card-title').textContent = extensionList;
            card.querySelector('.built-in-extensions').textContent = extensionList;
            card.querySelector('.built-in-command').textContent = runner.command || '';

            const isOff = isBuiltInRunnerDisabled(runner.builtInId);
            card.classList.toggle('off', isOff);

            const toggle = card.querySelector('.built-in-switch');
            toggle.setAttribute('aria-checked', String(!isOff));
            toggle.disabled = !isFormEditable();

            // The switch sits inside the summary, whose default action would otherwise toggle the card open.
            toggle.addEventListener('click', (event) => {
                event.preventDefault();
                setBuiltInRunnerDisabled(runner.builtInId, !isOff);
            });

            builtInRunnerList.appendChild(card);
        }
    }

    function isBuiltInRunnerDisabled(id) {
        return disabledBuiltInRunners.some((disabled) => disabled.toLowerCase() === (id || '').toLowerCase());
    }

    function setBuiltInRunnerDisabled(id, disabled) {
        const remaining = disabledBuiltInRunners.filter((entry) => entry.toLowerCase() !== (id || '').toLowerCase());

        disabledBuiltInRunners = disabled ? remaining.concat(id) : remaining;
        renderBuiltInRunners();
        onFormInput();
    }

    function readForm() {
        // The config's own type stands in when the control holds no selection, which is what a type the host
        // does not offer leaves behind.
        const type = sessionTypeSelect.value || currentConfig.type || 'shell';

        // Every type's table is carried forward and only the selected type's is rewritten. An empty field
        // writes no key, matching what parsing a file that omits it gives.
        const optionsBySessionType = { ...(currentConfig.optionsBySessionType || {}) };
        const options = {};
        for (const field of typeFields) {
            if (field.kind === 'lines') {
                const values = splitLines(field.input.value);
                if (values.length > 0) {
                    options[field.key] = values;
                }
                continue;
            }

            const text = field.kind === 'script' ? field.input.value.trimEnd() : field.input.value.trim();
            if (text !== '') {
                options[field.key] = text;
            }
        }

        if (Object.keys(options).length > 0) {
            optionsBySessionType[type] = options;
        } else {
            delete optionsBySessionType[type];
        }

        return {
            type,
            workingDirectory: workingDirectoryInput.value.trim(),
            optionsBySessionType,
            environment: parseEnvironmentLines(environmentInput.value),
            runners: runnerCards.read(),
            disabledBuiltInRunners: disabledBuiltInRunners.slice(),
            triggers: triggerCards.read(),
            shortcuts: shortcutCards.read(),
        };
    }

    // The Automation lists. Each runner and shortcut is edited through its own card, so the cards are the
    // source of truth for those two settings, the way the inputs above are for the rest of the form.

    const runnerCards = createCardList({
        listElement: document.getElementById('runner-cards'),
        emptyElement: document.getElementById('runner-empty'),
        addButton: document.getElementById('add-runner'),
        template: document.getElementById('runner-card-template'),
        blankItem: () => ({ extensions: [], command: '' }),
        focusSelector: '.runner-extensions',
        localize: applyLocalization,
        onChanged: () => onFormInput(),
        isWritable: isFormEditable,

        fillCard(card, runner) {
            card.querySelector('.runner-extensions').value = (runner.extensions || []).join(', ');
            card.querySelector('.runner-command').value = runner.command || '';
        },

        readCard(card) {
            const extensions = parseExtensionList(card.querySelector('.runner-extensions').value);
            const command = card.querySelector('.runner-command').value.trim();
            if (extensions.length === 0 || command === '') {
                return null;
            }

            return { extensions, command };
        },

        // The collapsed card identifies the runner by the extensions it handles. The command is one expand
        // away.
        updateHeader(card) {
            const extensions = card.querySelector('.runner-extensions').value.trim();
            card.querySelector('.cel-card-title').textContent = extensions || t('Console_Runner_Untitled');
        },
    });

    const triggerCards = createCardList({
        listElement: document.getElementById('trigger-cards'),
        emptyElement: document.getElementById('trigger-empty'),
        addButton: document.getElementById('add-trigger'),
        template: document.getElementById('trigger-card-template'),
        blankItem: () => ({ pattern: '', command: '' }),
        focusSelector: '.trigger-pattern',
        localize: applyLocalization,
        onChanged: () => onFormInput(),
        isWritable: isFormEditable,

        fillCard(card, trigger) {
            card.querySelector('.trigger-pattern').value = trigger.pattern || '';
            card.querySelector('.trigger-command').value = trigger.command || '';
        },

        readCard(card) {
            const pattern = card.querySelector('.trigger-pattern').value.trim();
            const command = card.querySelector('.trigger-command').value.trim();
            if (pattern === '' || command === '') {
                return null;
            }

            return { pattern, command };
        },

        // The collapsed card identifies the trigger by the pattern it watches, as the runner cards do by the
        // extensions they handle.
        updateHeader(card) {
            const pattern = card.querySelector('.trigger-pattern').value.trim();
            card.querySelector('.cel-card-title').textContent = pattern || t('Console_Trigger_Untitled');
        },
    });

    const shortcutCards = createCardList({
        listElement: document.getElementById('shortcut-cards'),
        emptyElement: document.getElementById('shortcut-empty'),
        addButton: document.getElementById('add-shortcut'),
        template: document.getElementById('shortcut-card-template'),
        blankItem: () => ({ label: '', icon: '', text: '' }),
        focusSelector: '.shortcut-label',
        localize: applyLocalization,
        onChanged: () => onFormInput(),
        isWritable: isFormEditable,

        fillCard(card, shortcut) {
            card.querySelector('.shortcut-label').value = shortcut.label || '';
            card.querySelector('.shortcut-text').value = shortcut.text || '';

            createIconField({
                container: card.querySelector('.shortcut-icon-field'),
                value: shortcut.icon || '',
                defaultIconName: SHORTCUT_FALLBACK_ICON,
                pickIcon: (searchText) => client.dialog.pickIcon(searchText),
            });
        },

        readCard(card) {
            const label = card.querySelector('.shortcut-label').value.trim();
            const icon = card.querySelector('.cel-icon-field-input').value.trim();
            const text = card.querySelector('.shortcut-text').value.trim();
            if (label === '' && text === '') {
                return null;
            }

            return { label, icon, text };
        },

        // The header doubles as the shortcut's preview: the glyph and label here are what the rail button
        // shows.
        updateHeader(card) {
            const label = card.querySelector('.shortcut-label').value.trim();
            card.querySelector('.cel-card-title').textContent = label || t('Console_Shortcut_Untitled');

            const iconName = card.querySelector('.cel-icon-field-input').value.trim();
            const iconElement = card.querySelector('.cel-card-icon');
            iconElement.className = 'cel-card-icon bi ' + resolveIconClass(iconName, SHORTCUT_FALLBACK_ICON, iconElement);
        },
    });

    // The per-console shortcuts, rendered as icon-only cells at the top of the inspector rail. Each injects
    // its text into the pty on click. The tooltip carries the label, falling back to the text it types.
    function renderShortcutRail() {
        shortcutRail.replaceChildren();
        const shortcuts = currentConfig.shortcuts || [];
        shortcutSeparator.classList.toggle('hidden', shortcuts.length === 0);

        for (const shortcut of shortcuts) {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'cel-rail-button';

            const tooltip = shortcut.label || shortcut.text || '';
            button.title = tooltip;
            button.setAttribute('aria-label', tooltip);

            const iconElement = document.createElement('i');
            button.appendChild(iconElement);
            button.addEventListener('click', () => injectShortcut(shortcut.text));
            shortcutRail.appendChild(button);

            // Resolved once the button is in the rail: the glyph check reads the loaded icon font.
            iconElement.className = 'bi ' + resolveIconClass(shortcut.icon, SHORTCUT_FALLBACK_ICON, iconElement);
        }
    }

    // Injects a shortcut's text into the pty.
    function injectShortcut(text) {
        if (!text) {
            return;
        }
        client.sendNotification('console/submit', { invocation: text });
    }

    function onFormInput() {
        currentConfig = readForm();
        // Editing the form yields a well-formed config, so any prior parse error is cleared.
        configError = null;
        client.document.notifyChanged();
        // The shortcut rail is pure client-side UI, so it previews live as the user edits. Every other
        // setting applies on the next reopen.
        renderShortcutRail();
        updateAttention();
    }

    // Re-renders the form from the config already loaded, with the new type selected.
    sessionTypeSelect.addEventListener('change', () => {
        populateForm({ ...currentConfig, type: sessionTypeSelect.value });
        applyWritableState();
        onFormInput();
    });

    // The controls every type shares. A type's own controls are bound as its markup is injected, since they
    // exist only while that type is selected.
    const formFields = [
        workingDirectoryInput,
        environmentInput,
    ];

    for (const field of formFields) {
        field.addEventListener('input', onFormInput);
    }

    // The host mirrors the writable state as its enum name, so Writable is the only editable value. A view
    // state that has not been seeded yet leaves the form editable.
    function isDocumentWritable() {
        const writable = client.viewState.current?.writable;

        return writable === undefined || writable === 'Writable';
    }

    // Whether an edit is allowed to reach the document. A type this client has no fields for is read-only
    // too.
    function isFormEditable() {
        return isDocumentWritable() && !isUnknownSessionType();
    }

    // A read-only document disables the settings form so no edit marks the document dirty. The switcher's
    // blanket pass over the sections runs first, so the card lists below decide the final state of the
    // controls they own.
    function applyWritableState() {
        settingsSwitcher.setReadOnly(!isFormEditable());

        runnerCards.refreshState();
        triggerCards.refreshState();
        shortcutCards.refreshState();
        renderBuiltInRunners();
    }

    function unknownTypeText() {
        if (!isUnknownSessionType()) {
            return '';
        }

        return t('Console_UnknownType', currentConfig.type || '');
    }

    // The pip flags a config error, a config that diverges from the launched session, or a session that never
    // started.
    function updateAttention() {
        const diverged = launchedConfig !== null && !configsEqual(currentConfig, launchedConfig);
        const needsAttention = diverged || configError !== null || sessionFailure !== null || isUnknownSessionType();
        pip.classList.toggle('hidden', !needsAttention);

        // The Reopen button stays enabled so the session can be restarted at any time. The accent colour
        // appears only when a reopen is needed to apply changed launch settings. The footer caption
        // explains it.
        reopenSettingsButton.classList.toggle('cel-accent', diverged);

        // One slot, so a parse error wins: it is the one the surface showing it can also fix.
        settingsSwitcher.setNotice(configError || unknownTypeText() || sessionFailure);
    }

    function applyContent(content) {
        try {
            currentConfig = parseConsoleToml(content, clientTypeIds);
            configError = null;
        } catch (error) {
            configError = (error && error.message) || t('Console_InvalidConfig');
        }
        populateForm(currentConfig);
        applyWritableState();
        updateAttention();
    }

    return {
        applyContent,
        applyWritableState,

        // Flushes the form to the document. The host's save timer does this on its own. A reopen forces it
        // so the file on disk is what the new session launches from.
        save() {
            return client.document.save(serializeConsoleToml(currentConfig));
        },

        // The registered session types an attach reports: static host knowledge the form needs to offer the
        // Type options and list each type's built-in runners. The first attach lands after the form is
        // populated, hence the re-render.
        setSessionTypes(sessionTypes) {
            hostSessionTypes = sessionTypes;
            renderSessionTypeOptions();
            applyWritableState();
        },

        // The config the live session was launched from, as the TOML the host reports it as. Null while
        // nothing has launched, which leaves the pip dark. An unparseable one does too, until the next
        // reopen.
        setLaunchedConfig(launchedConfigToml) {
            launchedConfig = null;
            if (launchedConfigToml) {
                try {
                    launchedConfig = parseConsoleToml(launchedConfigToml, clientTypeIds);
                } catch {
                    // Nothing to compare against, so no divergence is flagged.
                }
            }
            updateAttention();
        },

        // What the session's failure overlay is saying, or null once a session is running again.
        setSessionFailure(message) {
            sessionFailure = message ?? null;
            updateAttention();
        },

        setVisible(visible) {
            settingsView.classList.toggle('hidden', !visible);
        },

        isVisible() {
            return !settingsView.classList.contains('hidden');
        },

        // Hiding the rail destroys the focus the settings button was holding.
        focusSelectedSection() {
            settingsView.querySelector('.cel-section-nav-item[aria-selected="true"]')?.focus();
        },

        // Persist the selected section and its scroll position so they survive a reopen.
        requestState() {
            return JSON.stringify({
                activeSection: settingsSwitcher.selected(),
                scrollTop: settingsSwitcher.scrollTop(),
            });
        },

        restoreState(stateJson) {
            try {
                const state = JSON.parse(stateJson);
                // An unknown id leaves the default section selected.
                if (typeof state.activeSection === 'string') {
                    settingsSwitcher.select(state.activeSection);
                }
                // The switcher holds the offset until the surface is on screen to apply it to.
                if (typeof state.scrollTop === 'number' && state.scrollTop > 0) {
                    settingsSwitcher.setScrollTop(state.scrollTop);
                }
            } catch {
                // Ignore malformed state. Fall back to the defaults.
            }
        },
    };
}
