import { describe, it, expect } from 'vitest';
import { renderToHtml, disableScriptLinks } from '../markdown-preview/preview-module.js';

// Line numbers are what the source map promises, so the fixture is written a line at a time.
const documentWithFrontmatter = [
    '---',                 // 1
    'title: Home',         // 2
    'tags:',               // 3
    '  - Getting Started', // 4
    '---',                 // 5
    '',                    // 6
    '# Welcome',           // 7
    '',                    // 8
    'First paragraph.',    // 9
    '',                    // 10
    '## Section',          // 11
    '',                    // 12
    'Second paragraph.',   // 13
    ''
].join('\n');

function sourceLines(html) {
    const container = document.createElement('div');
    container.innerHTML = html;

    return [...container.querySelectorAll('[data-source-line]')]
        .map((element) => [element.tagName.toLowerCase(), Number(element.dataset.sourceLine)]);
}

describe('renderToHtml', () => {
    it('maps each block to its line in the full document, past the frontmatter', () => {
        expect(sourceLines(renderToHtml(documentWithFrontmatter))).toEqual([
            ['h1', 7],
            ['p', 9],
            ['h2', 11],
            ['p', 13]
        ]);
    });

    it('maps the same lines when the document uses CRLF line endings', () => {
        const crlf = documentWithFrontmatter.replace(/\n/g, '\r\n');

        expect(sourceLines(renderToHtml(crlf))).toEqual(sourceLines(renderToHtml(documentWithFrontmatter)));
    });

    it('maps blocks from their own lines when there is no frontmatter', () => {
        const source = '# Welcome\n\nFirst paragraph.\n';

        expect(sourceLines(renderToHtml(source))).toEqual([
            ['h1', 1],
            ['p', 3]
        ]);
    });

    it('leaves the frontmatter out of the rendered body', () => {
        const html = renderToHtml(documentWithFrontmatter);

        expect(html).not.toContain('title:');
        expect(html).not.toContain('Getting Started');
    });
});

describe('disableScriptLinks', () => {
    function renderLinks(markdown) {
        const container = document.createElement('div');
        container.innerHTML = renderToHtml(markdown);
        disableScriptLinks(container);

        return [...container.querySelectorAll('a')].map((link) => link.getAttribute('href'));
    }

    it('removes the address from a script link, written in markdown or as raw HTML', () => {
        const markdown = [
            '[Markdown](javascript:alert(1))',
            '',
            '<a href="JavaScript:alert(2)">Raw</a>',
            '',
            '<a href=" java&#9;script:alert(3)">Disguised</a>',
            ''
        ].join('\n');

        expect(renderLinks(markdown)).toEqual([null, null, null]);
    });

    it('leaves every other link as written', () => {
        const markdown = '[Guide](guide.md) [Site](https://example.com/) [Top](#top) [Mail](mailto:someone@example.com)\n';

        expect(renderLinks(markdown)).toEqual([
            'guide.md',
            'https://example.com/',
            '#top',
            'mailto:someone@example.com'
        ]);
    });
});
