// Editor for a setting that names an icon, and the name to class resolution the surfaces drawing that icon
// share. The field builds itself into an empty mount element and carries its own strings, so a consumer's
// markup holds no icon-specific structure and no consumer defines the wording again. Give it a `<label>`
// where the fields around it are labels, so clicking the field name focuses the input as theirs do.
//
// An edit is reported as an `input` event on the field's own input, whether the name was typed or picked,
// so a consumer already listening for input on its form needs nothing more. The name the field holds is on
// `.cel-icon-field-input`, so a consumer holding only the mount element can read it back.
//
//   createIconField({
//     container,                              // element the field is built into
//     value: shortcut.icon,                   // the name it starts with
//     defaultIconName: 'bs-lightning-charge', // previewed while empty; omit where empty means no glyph
//     pickIcon: (iconName) => cel.dialog.pickIcon(iconName),
//   });

import { t } from '../localization.js';

// The host stores icon names under the bs- prefix (matching IconService's names); the bundled icon font
// addresses the same icons as bi-.
const HOST_PREFIX = 'bs-';
const FONT_PREFIX = 'bi-';

// The font class for a host icon name, or empty for a name the host does not store under that prefix.
export function toIconClass(iconName) {
    const name = (iconName ?? '').trim();
    if (!name.startsWith(HOST_PREFIX)) {
        return '';
    }

    return FONT_PREFIX + name.slice(HOST_PREFIX.length);
}

// The icon font defines a ::before glyph per icon class, so a name the bundled set does not carry resolves
// to no content at all. The element must already be in the document: the check resolves the class it is
// wearing against the loaded font.
export function hasIconGlyph(element) {
    const content = getComputedStyle(element, '::before').content;

    return content !== '' && content !== 'none' && content !== 'normal';
}

// The class that will actually draw for an icon name, falling back to the default when the font carries no
// glyph for it. Every surface drawing a named icon resolves through this, so a button and the field
// previewing it cannot disagree about what an unrecognized name shows. probeElement must already be in the
// document, and is left wearing the candidate class.
export function resolveIconClass(iconName, defaultIconName, probeElement) {
    const candidate = toIconClass(iconName);
    if (candidate !== '') {
        probeElement.className = 'bi ' + candidate;
        if (hasIconGlyph(probeElement)) {
            return candidate;
        }
    }

    return toIconClass(defaultIconName);
}

export function createIconField(options) {
    const {
        container,
        value = '',
        defaultIconName = '',
        pickIcon,
    } = options;

    const label = createElement('span', 'field-label', 'IconPicker_FieldLabel');

    const row = createElement('div', 'cel-icon-field-row');
    const preview = createElement('i', 'cel-icon-field-preview bi');

    const input = document.createElement('input');
    input.className = 'cel-icon-field-input';
    input.type = 'text';
    input.spellcheck = false;
    input.value = value;
    input.placeholder = t('IconPicker_FieldPlaceholder');
    input.dataset.locKey = 'IconPicker_FieldPlaceholder';
    input.dataset.locAttr = 'placeholder';

    const browseButton = document.createElement('button');
    browseButton.className = 'cel-icon-button cel-icon-field-browse';
    browseButton.type = 'button';
    browseButton.title = t('IconPicker_BrowseTooltip');
    browseButton.setAttribute('aria-label', browseButton.title);
    browseButton.dataset.locKey = 'IconPicker_BrowseTooltip';
    browseButton.dataset.locAttr = 'title,aria-label';
    browseButton.appendChild(createElement('i', 'bi bi-three-dots'));

    const hint = createElement('span', 'field-hint', 'IconPicker_FieldHint');
    const warning = createElement('span', 'field-warning cel-icon-field-warning hidden', 'IconPicker_UnknownIcon');

    row.append(preview, input, browseButton);

    // The mount element becomes the field itself rather than holding one, so it sits in its consumer's form
    // as the hand written fields beside it do, and the spacing rules keyed on a field's position reach it.
    container.classList.add('field');
    container.replaceChildren(label, row, hint, warning);

    function updateIconState() {
        const iconName = input.value.trim();
        const candidate = toIconClass(iconName);
        const resolved = resolveIconClass(iconName, defaultIconName, preview);
        preview.className = 'cel-icon-field-preview bi ' + resolved;

        // A blank field is unconfigured rather than wrong, so it does not report as unsupported. A name the
        // host does not store under the icon prefix has no candidate class at all, so it is unsupported
        // however the fallback resolves.
        const isIconUnknown = iconName !== '' && (candidate === '' || resolved !== candidate);
        warning.classList.toggle('hidden', !isIconUnknown);
    }

    input.addEventListener('input', updateIconState);

    browseButton.addEventListener('click', async () => {
        let pickedIconName;
        try {
            pickedIconName = await pickIcon(input.value.trim());
        } catch (error) {
            console.error('[IconField] Failed to open the icon picker:', error);
            return;
        }

        // The picker reports a dismissal as no icon, so the field keeps the name it had.
        if (!pickedIconName) {
            return;
        }

        input.value = pickedIconName;

        // Reported as though it had been typed, so a consumer listening for input records the change. The
        // field's own listener refreshes the preview off the same event.
        input.dispatchEvent(new Event('input', { bubbles: true }));
    });

    // The glyph check reads the loaded icon font, which resolves only once the field is in the document,
    // and a card list builds its card before inserting it. The first pass waits for the next frame when
    // the field is built detached.
    if (container.isConnected) {
        updateIconState();
    } else {
        requestAnimationFrame(updateIconState);
    }

    return {
        get value() {
            return input.value.trim();
        },
        set value(iconName) {
            input.value = iconName;
            updateIconState();
        },
    };
}

// The localization key is carried as well as applied, so the field re-localizes with the rest of the page
// when the host changes language.
function createElement(tagName, className, locKey) {
    const element = document.createElement(tagName);
    element.className = className;

    if (locKey) {
        element.textContent = t(locKey);
        element.dataset.locKey = locKey;
    }

    return element;
}
