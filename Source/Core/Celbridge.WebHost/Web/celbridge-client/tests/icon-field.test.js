// @vitest-environment jsdom

import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { createIconField, toIconClass } from '../ui/icon-field.js';
import { setStrings } from '../localization.js';

// jsdom resolves no stylesheets, so the glyph probe is stubbed: the icon font is treated as carrying the
// names listed here and nothing else, which is what the bundled stylesheet decides in the browser.
function stubIconFont(iconNames) {
    const classNames = iconNames.map((iconName) => toIconClass(iconName));

    vi.spyOn(window, 'getComputedStyle').mockImplementation((element, pseudoElement) => {
        if (pseudoElement !== '::before') {
            return { content: 'normal' };
        }

        const hasGlyph = classNames.some((className) => element.classList.contains(className));

        return { content: hasGlyph ? '"\\f1c0"' : '' };
    });
}

function createField(options) {
    const container = document.createElement('div');
    document.body.appendChild(container);

    const field = createIconField({ container, pickIcon: () => Promise.resolve(null), ...options });

    return { container, field };
}

function warningOf(container) {
    return container.querySelector('.cel-icon-field-warning');
}

function previewOf(container) {
    return container.querySelector('.cel-icon-field-preview');
}

beforeEach(() => {
    document.body.innerHTML = '';
    setStrings({
        IconPicker_FieldLabel: 'Icon',
        IconPicker_FieldPlaceholder: 'bs-book',
        IconPicker_FieldHint: 'Browse to pick an icon, or type its name.',
        IconPicker_UnknownIcon: 'There is no supported icon with this name.',
        IconPicker_BrowseTooltip: 'Choose an icon',
    });
    stubIconFont(['bs-gear', 'bs-lightning-charge']);
});

afterEach(() => {
    vi.restoreAllMocks();
});

describe('createIconField', () => {
    /// The mount element becomes the field. A field nested inside it would be the last child of its own
    /// wrapper, which is what the spacing rules for a form's last field key on.
    it('builds into the mount element rather than wrapping a field inside it', () => {
        const { container } = createField({ value: 'bs-gear' });

        expect(container.classList.contains('field')).toBe(true);
        expect(container.querySelector('.field')).toBeNull();
    });

    it('previews the named icon and reports nothing', () => {
        const { container } = createField({ value: 'bs-gear' });

        expect(previewOf(container).className).toContain('bi-gear');
        expect(warningOf(container).classList.contains('hidden')).toBe(true);
    });

    it('previews the default icon while the field is empty', () => {
        const { container } = createField({ value: '', defaultIconName: 'bs-lightning-charge' });

        expect(previewOf(container).className).toContain('bi-lightning-charge');
        expect(warningOf(container).classList.contains('hidden')).toBe(true);
    });

    it('draws no glyph for an empty field with no default', () => {
        const { container } = createField({ value: '' });

        expect(previewOf(container).className.trim()).toBe('cel-icon-field-preview bi');
    });

    /// A name the icon font does not carry, and a name from a font the host keeps to itself, are both
    /// unsupported: the button still draws, using the default glyph.
    it.each(['bs-not-a-real-icon', 'nf-seti-json'])('reports an unsupported name: %s', (iconName) => {
        const { container } = createField({ value: iconName, defaultIconName: 'bs-lightning-charge' });

        expect(warningOf(container).classList.contains('hidden')).toBe(false);
        expect(previewOf(container).className).toContain('bi-lightning-charge');
    });

    it('reports an unsupported name with no default to fall back to', () => {
        const { container } = createField({ value: 'nf-seti-json' });

        expect(warningOf(container).classList.contains('hidden')).toBe(false);
    });

    it('refreshes the preview and the warning as the name is typed', () => {
        const { container } = createField({ value: 'bs-gear' });
        const input = container.querySelector('.cel-icon-field-input');

        input.value = 'bs-nope';
        input.dispatchEvent(new window.Event('input', { bubbles: true }));

        expect(warningOf(container).classList.contains('hidden')).toBe(false);
    });

    it('takes the picked icon, and reports it as an edit', async () => {
        const edits = [];
        const { container } = createField({
            value: 'bs-nope',
            pickIcon: () => Promise.resolve('bs-gear'),
        });
        container.addEventListener('input', () => edits.push(container.querySelector('.cel-icon-field-input').value));

        container.querySelector('.cel-icon-field-browse').click();
        await vi.waitFor(() => expect(edits).toEqual(['bs-gear']));

        expect(previewOf(container).className).toContain('bi-gear');
        expect(warningOf(container).classList.contains('hidden')).toBe(true);
    });

    it('opens the picker on the name the field holds', async () => {
        const pickedFor = [];
        const { container } = createField({
            value: 'bs-gear',
            pickIcon: (iconName) => {
                pickedFor.push(iconName);
                return Promise.resolve(null);
            },
        });

        container.querySelector('.cel-icon-field-browse').click();
        await vi.waitFor(() => expect(pickedFor).toEqual(['bs-gear']));
    });

    it('keeps the name it had when the picker is dismissed', async () => {
        const { container, field } = createField({
            value: 'bs-gear',
            pickIcon: () => Promise.resolve(null),
        });

        container.querySelector('.cel-icon-field-browse').click();
        await vi.waitFor(() => expect(field.value).toBe('bs-gear'));
    });
});
