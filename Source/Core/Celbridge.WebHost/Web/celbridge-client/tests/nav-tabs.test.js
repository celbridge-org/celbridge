// @vitest-environment jsdom

import { describe, it, expect, beforeEach, vi } from 'vitest';
import { attachNavTabs } from '../ui/nav-tabs.js';

function buildStrip(selectedSection = 'session') {
    document.body.innerHTML = `
        <div id="strip" class="cel-nav-tab-strip" role="tablist">
            <button class="cel-nav-tab" type="button" role="tab" data-section="session"></button>
            <button class="cel-nav-tab" type="button" role="tab" data-section="environment"></button>
            <button class="cel-nav-tab" type="button" role="tab" data-section="shortcuts"></button>
        </div>`;

    const strip = document.getElementById('strip');
    strip.querySelector(`[data-section="${selectedSection}"]`).setAttribute('aria-selected', 'true');

    return strip;
}

function tabFor(strip, sectionId) {
    return strip.querySelector(`[data-section="${sectionId}"]`);
}

function pressKey(strip, key) {
    strip.dispatchEvent(new window.KeyboardEvent('keydown', { key, bubbles: true }));
}

describe('attachNavTabs', () => {
    let strip;
    let onChange;

    beforeEach(() => {
        strip = buildStrip();
        onChange = vi.fn();
    });

    it('selects the tab marked in the markup and reports it', () => {
        const tabs = attachNavTabs(strip, { onChange });

        expect(tabs.selected()).toBe('session');
        expect(onChange).toHaveBeenCalledWith('session');
        expect(tabFor(strip, 'session').getAttribute('aria-selected')).toBe('true');
        expect(tabFor(strip, 'environment').getAttribute('aria-selected')).toBe('false');
    });

    it('falls back to the first tab when the markup marks none', () => {
        strip = buildStrip();
        tabFor(strip, 'session').setAttribute('aria-selected', 'false');

        const tabs = attachNavTabs(strip, { onChange });

        expect(tabs.selected()).toBe('session');
    });

    it('selects a tab on click and moves the tab order to it', () => {
        const tabs = attachNavTabs(strip, { onChange });
        onChange.mockClear();

        tabFor(strip, 'shortcuts').click();

        expect(tabs.selected()).toBe('shortcuts');
        expect(onChange).toHaveBeenCalledExactlyOnceWith('shortcuts');
        expect(tabFor(strip, 'shortcuts').tabIndex).toBe(0);
        expect(tabFor(strip, 'session').tabIndex).toBe(-1);
    });

    it('ignores a click on the already selected tab', () => {
        attachNavTabs(strip, { onChange });
        onChange.mockClear();

        tabFor(strip, 'session').click();

        expect(onChange).not.toHaveBeenCalled();
    });

    it('ignores a select for an unknown section', () => {
        const tabs = attachNavTabs(strip, { onChange });

        tabs.select('nonexistent');

        expect(tabs.selected()).toBe('session');
    });

    it('moves between tabs with the arrow keys, wrapping at each end', () => {
        const tabs = attachNavTabs(strip, { onChange });

        pressKey(strip, 'ArrowRight');
        expect(tabs.selected()).toBe('environment');

        pressKey(strip, 'ArrowLeft');
        expect(tabs.selected()).toBe('session');

        pressKey(strip, 'ArrowLeft');
        expect(tabs.selected()).toBe('shortcuts');
    });

    it('jumps to the first and last tabs with Home and End', () => {
        const tabs = attachNavTabs(strip, { onChange });

        pressKey(strip, 'End');
        expect(tabs.selected()).toBe('shortcuts');

        pressKey(strip, 'Home');
        expect(tabs.selected()).toBe('session');
    });

    it('leaves other keys to the page', () => {
        const tabs = attachNavTabs(strip, { onChange });

        pressKey(strip, 'ArrowDown');

        expect(tabs.selected()).toBe('session');
    });

    it('returns a no-op controller when the strip is missing', () => {
        const tabs = attachNavTabs(null, { onChange });

        tabs.select('session');

        expect(tabs.selected()).toBeNull();
        expect(onChange).not.toHaveBeenCalled();
    });
});
