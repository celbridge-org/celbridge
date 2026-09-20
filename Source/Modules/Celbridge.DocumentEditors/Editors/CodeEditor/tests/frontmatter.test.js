import { describe, it, expect } from 'vitest';
import { stripFrontmatter } from '../markdown-preview/frontmatter.js';

describe('stripFrontmatter', () => {
    it('removes a leading block and reports where the body starts', () => {
        const source = '---\ntitle: Home\ntags:\n  - Getting Started\n---\n\n# Welcome to My Site\n';
        const { body, bodyOffset } = stripFrontmatter(source);

        expect(body).toBe('\n# Welcome to My Site\n');
        expect(source.substring(bodyOffset)).toBe(body);
    });

    it('leaves a document without frontmatter alone', () => {
        const source = '# Welcome to My Site\n\nBody text.\n';

        expect(stripFrontmatter(source)).toEqual({ body: source, bodyOffset: 0 });
    });

    it('ignores an unterminated block', () => {
        const source = '---\ntitle: Home\n\n# Welcome to My Site\n';

        expect(stripFrontmatter(source).bodyOffset).toBe(0);
    });

    it('ignores a block with no content, so a pair of horizontal rules still renders', () => {
        const source = '---\n---\n\n# Welcome to My Site\n';

        expect(stripFrontmatter(source).bodyOffset).toBe(0);
    });

    it('ignores a block that does not start on the first line', () => {
        const source = '# Welcome to My Site\n\n---\ntitle: Home\n---\n';

        expect(stripFrontmatter(source).bodyOffset).toBe(0);
    });

    it('runs to the real closing delimiter past an indented run of dashes', () => {
        const source = '---\ndescription: |\n  ---\n  indented\ntemplate: home.html\n---\n\n# Welcome to My Site\n';

        expect(stripFrontmatter(source).body).toBe('\n# Welcome to My Site\n');
    });

    it('handles CRLF line endings', () => {
        const source = '---\r\ntitle: Home\r\n---\r\n\r\n# Welcome to My Site\r\n';
        const { body, bodyOffset } = stripFrontmatter(source);

        expect(body).toBe('\r\n# Welcome to My Site\r\n');
        expect(source.substring(bodyOffset)).toBe(body);
    });
});
