// Frontmatter handling for the markdown preview.

// The opening delimiter must be the first line of the file. Matching without the multiline flag
// anchors it to offset 0.
const FRONTMATTER_PATTERN = /^\uFEFF?---[ \t]*\r?\n([\s\S]*?)\r?\n---[ \t]*(?:\r?\n|$)/;

/**
 * Removes a leading YAML frontmatter block from the markdown source.
 * @param {string} markdown - The full document source.
 * @returns {{body: string, bodyOffset: number}} The markdown that follows the block, and the
 * character offset it starts at in the original source. A document with no frontmatter comes back
 * unchanged at offset 0.
 */
export function stripFrontmatter(markdown) {
    const source = markdown || '';
    const match = FRONTMATTER_PATTERN.exec(source);
    if (!match ||
        match[1].trim() === '') {
        return { body: source, bodyOffset: 0 };
    }

    const bodyOffset = match[0].length;

    return {
        body: source.substring(bodyOffset),
        bodyOffset
    };
}
