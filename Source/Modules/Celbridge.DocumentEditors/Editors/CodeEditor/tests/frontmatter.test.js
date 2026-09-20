import { describe, it, expect } from 'vitest';
import { marked } from '../markdown-preview/lib/marked.esm.js';
import { splitFrontmatter } from '../markdown-preview/frontmatter.js';

const documentWithFrontmatter = `---
title: Home
tags:
  - Getting Started
---

# Welcome to My Site

Body text.
`;

describe('splitFrontmatter', () => {
    it('splits a leading block off the body', () => {
        const { frontmatter, body, bodyOffset } = splitFrontmatter(documentWithFrontmatter);

        expect(frontmatter).toBe('title: Home\ntags:\n  - Getting Started');
        expect(body.trimStart()).toBe('# Welcome to My Site\n\nBody text.\n');
        expect(documentWithFrontmatter.substring(bodyOffset)).toBe(body);
    });

    it('leaves a document without frontmatter alone', () => {
        const source = '# Welcome to My Site\n\nBody text.\n';

        expect(splitFrontmatter(source)).toEqual({
            frontmatter: null,
            body: source,
            bodyOffset: 0
        });
    });

    it('ignores an unterminated block', () => {
        const source = '---\ntitle: Home\n\n# Welcome to My Site\n';

        expect(splitFrontmatter(source).frontmatter).toBeNull();
    });

    it('ignores a block with no content, so a pair of horizontal rules still renders', () => {
        const source = '---\n---\n\n# Welcome to My Site\n';

        expect(splitFrontmatter(source).frontmatter).toBeNull();
    });

    it('ignores a block that does not start on the first line', () => {
        const source = '# Welcome to My Site\n\n---\ntitle: Home\n---\n';

        expect(splitFrontmatter(source).frontmatter).toBeNull();
    });

    it('handles CRLF line endings', () => {
        const source = '---\r\ntitle: Home\r\n---\r\n\r\n# Welcome to My Site\r\n';
        const { frontmatter, body, bodyOffset } = splitFrontmatter(source);

        expect(frontmatter).toBe('title: Home');
        expect(source.substring(bodyOffset)).toBe(body);
    });
});

describe('frontmatter tokenization', () => {
    it('is read as a setext heading when left in the source', () => {
        const tokens = marked.lexer(documentWithFrontmatter);

        const heading = tokens.find((token) => token.type === 'heading');
        expect(heading.depth).toBe(2);
        expect(heading.text).toContain('title: Home');
    });

    it('leaves the body starting at its own first heading once split off', () => {
        const { body } = splitFrontmatter(documentWithFrontmatter);
        const tokens = marked.lexer(body);

        const heading = tokens.find((token) => token.type === 'heading');
        expect(heading.depth).toBe(1);
        expect(heading.text).toBe('Welcome to My Site');
    });
});
