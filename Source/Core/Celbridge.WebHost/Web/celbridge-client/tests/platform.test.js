import { describe, it, expect, vi, afterEach } from 'vitest';
import { isMacOS, isWindows } from '../platform.js';

function stubNavigator(fields) {
    vi.stubGlobal('navigator', fields);
}

describe('platform', () => {
    afterEach(() => {
        vi.unstubAllGlobals();
    });

    it('recognises WebKit on macOS by its platform string', () => {
        stubNavigator({ platform: 'MacIntel' });

        expect(isMacOS()).toBe(true);
        expect(isWindows()).toBe(false);
    });

    it('recognises WebView2 on Windows by its client hints', () => {
        stubNavigator({ userAgentData: { platform: 'Windows' }, platform: 'Win32' });

        expect(isWindows()).toBe(true);
        expect(isMacOS()).toBe(false);
    });

    it('falls back to the user agent when the platform string is empty', () => {
        stubNavigator({ platform: '', userAgent: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)' });

        expect(isMacOS()).toBe(true);
    });

    it('recognises neither on another platform', () => {
        stubNavigator({ platform: 'Linux x86_64' });

        expect(isMacOS()).toBe(false);
        expect(isWindows()).toBe(false);
    });
});
