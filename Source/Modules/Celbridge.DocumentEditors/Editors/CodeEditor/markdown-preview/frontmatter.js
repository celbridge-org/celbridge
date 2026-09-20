// Frontmatter detection for the markdown preview.
//
// A YAML frontmatter block is metadata for whatever publishes the file, not document body, so the
// preview shows it separately rather than as markdown. Left in the source handed to marked, the block
// parses as a setext heading underlined by its closing delimiter and renders as one large heading.

// The opening delimiter has to be the first line of the file and the closing delimiter a line of
// exactly three dashes. Matching without the multiline flag anchors the opening delimiter to offset 0.
// A block that is unterminated or holds no content is not frontmatter, so a document opening with a
// horizontal rule still renders as markdown.
const FRONTMATTER_PATTERN = /^\uFEFF?---[ \t]*\r?\n([\s\S]*?)\r?\n---[ \t]*(?:\r?\n|$)/;

/**
 * Splits a leading YAML frontmatter block off the markdown source.
 * @param {string} markdown - The full document source.
 * @returns {{frontmatter: string|null, body: string, bodyOffset: number}} The block's contents, null
 * when the document has none, the markdown that follows it, and the character offset of that markdown
 * in the original source.
 */
export function splitFrontmatter(markdown) {
    const source = markdown || '';
    const match = FRONTMATTER_PATTERN.exec(source);
    if (!match ||
        match[1].trim() === '') {
        return { frontmatter: null, body: source, bodyOffset: 0 };
    }

    const bodyOffset = match[0].length;

    return {
        frontmatter: match[1],
        body: source.substring(bodyOffset),
        bodyOffset
    };
}
