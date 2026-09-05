// @vitest-environment jsdom

import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { attachSectionSwitcher } from '../ui/section-switcher.js';

// jsdom carries no ResizeObserver, so the switcher's layout measurement is driven by hand.
let resizeCallbacks = [];

class ResizeObserverStub {
    constructor(callback) {
        resizeCallbacks.push(callback);
    }

    observe() { }

    disconnect() { }
}

function reportWidth(width) {
    for (const callback of resizeCallbacks) {
        callback([{ contentRect: { width } }]);
    }
}

function buildSwitcher(selectedSection = 'session') {
    document.body.innerHTML = `
        <div id="switcher" class="cel-section-switcher">
            <div class="cel-section-nav" role="tablist">
                <button class="cel-section-nav-item" type="button" role="tab" data-section="session"></button>
                <button class="cel-section-nav-item" type="button" role="tab" data-section="shortcuts"></button>
                <button class="cel-section-nav-item" type="button" role="tab" data-section="runners"></button>
            </div>
            <div class="cel-section-area">
                <div class="cel-section-header" data-section="session"><h1>Session</h1></div>
                <div class="cel-section-header" data-section="shortcuts"><h1>Shortcuts</h1></div>
                <div class="cel-section-header" data-section="runners"><h1>Runners</h1></div>
                <div class="cel-section-notice" hidden></div>
                <div class="cel-section-content">
                    <section data-section="session"><input id="session-field" type="text"></section>
                    <section data-section="shortcuts"><input id="shortcuts-field" type="text"></section>
                    <section data-section="runners"></section>
                </div>
            </div>
            <div class="cel-section-footer"><button id="footer-action" type="button">Reopen</button></div>
        </div>`;

    const root = document.getElementById('switcher');
    root.querySelector(`.cel-section-nav-item[data-section="${selectedSection}"]`)
        .setAttribute('aria-selected', 'true');

    return root;
}

function rowFor(root, sectionId) {
    return root.querySelector(`.cel-section-nav-item[data-section="${sectionId}"]`);
}

function sectionFor(root, sectionId) {
    return root.querySelector(`.cel-section-content > [data-section="${sectionId}"]`);
}

function headerFor(root, sectionId) {
    return root.querySelector(`.cel-section-header[data-section="${sectionId}"]`);
}

function pressKey(root, key) {
    root.querySelector('.cel-section-nav')
        .dispatchEvent(new window.KeyboardEvent('keydown', { key, bubbles: true }));
}

describe('attachSectionSwitcher', () => {
    let root;
    let onChange;

    beforeEach(() => {
        resizeCallbacks = [];
        vi.stubGlobal('ResizeObserver', ResizeObserverStub);
        root = buildSwitcher();
        onChange = vi.fn();
    });

    afterEach(() => {
        vi.unstubAllGlobals();
    });

    it('shows the section marked in the markup and reports it', () => {
        const switcher = attachSectionSwitcher(root, { onChange });

        expect(switcher.selected()).toBe('session');
        expect(onChange).toHaveBeenCalledWith('session');
        expect(rowFor(root, 'session').getAttribute('aria-selected')).toBe('true');
        expect(rowFor(root, 'runners').getAttribute('aria-selected')).toBe('false');
        expect(sectionFor(root, 'session').hidden).toBe(false);
        expect(sectionFor(root, 'runners').hidden).toBe(true);
        expect(headerFor(root, 'session').hidden).toBe(false);
        expect(headerFor(root, 'runners').hidden).toBe(true);
    });

    it('falls back to the first row when the markup marks none', () => {
        rowFor(root, 'session').setAttribute('aria-selected', 'false');

        const switcher = attachSectionSwitcher(root, { onChange });

        expect(switcher.selected()).toBe('session');
    });

    it('selects a section on click and moves the tab order to it', () => {
        const switcher = attachSectionSwitcher(root, { onChange });
        onChange.mockClear();

        rowFor(root, 'runners').click();

        expect(switcher.selected()).toBe('runners');
        expect(onChange).toHaveBeenCalledExactlyOnceWith('runners');
        expect(rowFor(root, 'runners').tabIndex).toBe(0);
        expect(rowFor(root, 'session').tabIndex).toBe(-1);
        expect(sectionFor(root, 'runners').hidden).toBe(false);
    });

    it('ignores a click on the already selected row', () => {
        attachSectionSwitcher(root, { onChange });
        onChange.mockClear();

        rowFor(root, 'session').click();

        expect(onChange).not.toHaveBeenCalled();
    });

    it('ignores a select for an unknown section', () => {
        const switcher = attachSectionSwitcher(root, { onChange });

        switcher.select('nonexistent');

        expect(switcher.selected()).toBe('session');
    });

    it('moves between rows with the arrow keys on either axis, wrapping at each end', () => {
        const switcher = attachSectionSwitcher(root, { onChange });

        pressKey(root, 'ArrowDown');
        expect(switcher.selected()).toBe('shortcuts');

        pressKey(root, 'ArrowUp');
        expect(switcher.selected()).toBe('session');

        pressKey(root, 'ArrowLeft');
        expect(switcher.selected()).toBe('runners');

        pressKey(root, 'ArrowRight');
        expect(switcher.selected()).toBe('session');
    });

    it('jumps to the first and last rows with Home and End', () => {
        const switcher = attachSectionSwitcher(root, { onChange });

        pressKey(root, 'End');
        expect(switcher.selected()).toBe('runners');

        pressKey(root, 'Home');
        expect(switcher.selected()).toBe('session');
    });

    it('leaves other keys to the page', () => {
        const switcher = attachSectionSwitcher(root, { onChange });

        pressKey(root, 'PageDown');

        expect(switcher.selected()).toBe('session');
    });

    it('keeps each section scroll offset while another section is showing', () => {
        const switcher = attachSectionSwitcher(root, { onChange });

        const sessionSection = sectionFor(root, 'session');
        sessionSection.scrollTop = 120;
        sessionSection.dispatchEvent(new window.Event('scroll'));

        switcher.select('runners');
        switcher.select('session');

        expect(switcher.scrollTop()).toBe(120);
        expect(sessionSection.scrollTop).toBe(120);
    });

    it('applies a persisted scroll offset to the selected section', () => {
        const switcher = attachSectionSwitcher(root, { onChange });

        switcher.select('shortcuts');
        switcher.setScrollTop(64);

        expect(sectionFor(root, 'shortcuts').scrollTop).toBe(64);
        expect(switcher.scrollTop()).toBe(64);
    });

    it('shows and hides the notice slot', () => {
        const switcher = attachSectionSwitcher(root, { onChange });
        const notice = root.querySelector('.cel-section-notice');

        switcher.setNotice('The file would not parse.');
        expect(notice.hidden).toBe(false);
        expect(notice.textContent).toBe('The file would not parse.');

        switcher.setNotice('');
        expect(notice.hidden).toBe(true);
    });

    it('disables the controls inside its sections when read-only, leaving the footer action alone', () => {
        const switcher = attachSectionSwitcher(root, { onChange });

        switcher.setReadOnly(true);

        expect(root.dataset.readonly).toBe('true');
        expect(document.getElementById('session-field').disabled).toBe(true);
        expect(document.getElementById('shortcuts-field').disabled).toBe(true);
        expect(document.getElementById('footer-action').disabled).toBe(false);

        switcher.setReadOnly(false);

        expect(document.getElementById('session-field').disabled).toBe(false);
    });

    it('takes a read-only state declared in the markup', () => {
        root.dataset.readonly = 'true';

        attachSectionSwitcher(root, { onChange });

        expect(document.getElementById('session-field').disabled).toBe(true);
    });

    it('picks the layout from the measured width', () => {
        attachSectionSwitcher(root, { onChange });

        reportWidth(800);
        expect(root.dataset.layout).toBe('inline');
        expect(root.querySelector('.cel-section-nav').getAttribute('aria-orientation')).toBe('vertical');

        reportWidth(300);
        expect(root.dataset.layout).toBe('stacked');
        expect(root.querySelector('.cel-section-nav').getAttribute('aria-orientation')).toBe('horizontal');
    });

    it('holds a layout named in the markup', () => {
        root.dataset.layout = 'stacked';

        attachSectionSwitcher(root, { onChange });
        reportWidth(800);

        expect(root.dataset.layout).toBe('stacked');
    });

    it('returns a no-op controller when the switcher is missing', () => {
        const switcher = attachSectionSwitcher(null, { onChange });

        switcher.select('session');

        expect(switcher.selected()).toBeNull();
        expect(switcher.scrollTop()).toBe(0);
        expect(onChange).not.toHaveBeenCalled();
    });
});
