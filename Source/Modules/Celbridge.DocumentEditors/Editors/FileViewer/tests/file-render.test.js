import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { setStrings } from '/assets/celbridge-client/localization.js';
import strings from '../localization/en.json';
import { renderFile } from '../js/file-render.js';

// The tests read the text a reader sees, so the page gets the editor's own strings.
setStrings(strings);

const FILE_URL = 'http://127.0.0.1:5000/project/media/file?t=1';
const NEWER_URL = 'http://127.0.0.1:5000/project/media/newer?t=2';

let container;

beforeEach(() => {
    document.body.innerHTML = '<div id="file-viewer-container"></div>';
    container = document.getElementById('file-viewer-container');
});

afterEach(() => {
    vi.unstubAllGlobals();
    delete document.fonts;
});

// jsdom lacks FontFace and document.fonts, so each font test supplies stubs. Each stub face's load waits until the
// test calls resolve or reject.
function stubFonts() {
    const faces = [];

    class StubFontFace {
        constructor(family, source) {
            this.family = family;
            this.source = source;
            this.loaded = new Promise((resolve, reject) => {
                this.resolve = () => resolve(this);
                this.reject = () => reject(new DOMException('The font could not be decoded.', 'NetworkError'));
            });
            faces.push(this);
        }

        load() {
            return this.loaded;
        }
    }

    vi.stubGlobal('FontFace', StubFontFace);
    document.fonts = { add: vi.fn(), delete: vi.fn() };

    return faces;
}

describe('renderFile', () => {
    it.each([
        ['photo.avif', 'IMG'],
        ['photo.JXL', 'IMG'],
        ['tone.opus', 'AUDIO'],
        ['clip.m4v', 'VIDEO'],
        ['manual.pdf', 'IFRAME']
    ])('draws %s in an %s element', async (fileName, tagName) => {
        await renderFile(container, fileName, FILE_URL);

        const element = container.firstElementChild;
        expect(element.tagName).toBe(tagName);
        expect(element.getAttribute('src')).toBe(FILE_URL);
    });

    it.each(['photo.png', 'tone.wav', 'clip.mp4'])('shows a message in place of %s when it cannot be decoded',
        async (fileName) => {
            await renderFile(container, fileName, FILE_URL);
            container.firstElementChild.dispatchEvent(new Event('error'));

            expect(container.querySelector('img, audio, video')).toBeNull();
            expect(container.textContent).toBe(strings.FileViewer_CannotDisplay);
        });

    it('keeps the newer file when an older one fails to decode', async () => {
        await renderFile(container, 'old.png', FILE_URL);
        const olderImage = container.firstElementChild;

        await renderFile(container, 'new.png', NEWER_URL);
        olderImage.dispatchEvent(new Event('error'));

        expect(container.firstElementChild.getAttribute('src')).toBe(NEWER_URL);
    });

    it('shows the message for a type it has no renderer for', async () => {
        await renderFile(container, 'notes.txt', FILE_URL);

        expect(container.textContent).toBe(strings.FileViewer_CannotDisplay);
    });
});

describe('a font', () => {
    it('shows the file name, then the sample text in the font at several sizes', async () => {
        const faces = stubFonts();

        const rendering = renderFile(container, 'Example.woff2', FILE_URL);
        faces[0].resolve();
        await rendering;

        expect(faces[0].source).toBe(`url("${FILE_URL}")`);
        expect(document.fonts.add).toHaveBeenCalledWith(faces[0]);

        expect(container.querySelector('.file-font-name').textContent).toBe('Example.woff2');

        const samples = [...container.querySelectorAll('.file-font-sample')];
        expect(samples.length).toBeGreaterThan(1);
        expect(new Set(samples.map(sample => sample.style.fontSize)).size).toBe(samples.length);
        for (const sample of samples) {
            expect(sample.textContent).toBe(strings.FileViewer_FontSample);
            expect(sample.style.fontFamily).toContain(faces[0].family);
        }
    });

    it('shows the message when the font cannot be decoded', async () => {
        const faces = stubFonts();

        const rendering = renderFile(container, 'Broken.ttf', FILE_URL);
        faces[0].reject();
        await rendering;

        expect(container.querySelector('.file-font-sample')).toBeNull();
        expect(container.textContent).toBe(strings.FileViewer_CannotDisplay);
        expect(document.fonts.add).not.toHaveBeenCalled();
    });

    it('keeps the newer file when an older font finishes loading', async () => {
        const faces = stubFonts();

        const olderRendering = renderFile(container, 'Older.otf', FILE_URL);
        await renderFile(container, 'photo.png', NEWER_URL);
        faces[0].resolve();
        await olderRendering;

        expect(container.querySelector('.file-font')).toBeNull();
        expect(container.firstElementChild.getAttribute('src')).toBe(NEWER_URL);
    });

    it('removes the previous font when the file is shown again', async () => {
        const faces = stubFonts();

        const firstRendering = renderFile(container, 'Example.ttf', FILE_URL);
        faces[0].resolve();
        await firstRendering;

        const secondRendering = renderFile(container, 'Example.ttf', NEWER_URL);
        faces[1].resolve();
        await secondRendering;

        expect(document.fonts.delete).toHaveBeenCalledWith(faces[0]);
        expect(document.fonts.add).toHaveBeenLastCalledWith(faces[1]);
    });
});
