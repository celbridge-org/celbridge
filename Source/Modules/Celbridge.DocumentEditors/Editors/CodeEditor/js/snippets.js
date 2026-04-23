// Snippet definitions for the editor's optional snippet dropdown.
// Keys are used with data-loc-key / t() for localized menu labels.

const MARKDOWN_SNIPPETS = [
    { locKey: 'CodeEditor_Snippet_Bold',           text: '**bold text**' },
    { locKey: 'CodeEditor_Snippet_Italic',         text: '*italic text*' },
    { locKey: 'CodeEditor_Snippet_Strikethrough',  text: '~~strikethrough text~~' },
    { separator: true },
    { locKey: 'CodeEditor_Snippet_UnorderedList',  text: '- Item 1\n- Item 2\n- Item 3\n' },
    { locKey: 'CodeEditor_Snippet_OrderedList',    text: '1. Item 1\n2. Item 2\n3. Item 3\n' },
    { locKey: 'CodeEditor_Snippet_TaskList',       text: '- [ ] Task 1\n- [ ] Task 2\n- [x] Completed task\n' },
    { separator: true },
    { locKey: 'CodeEditor_Snippet_CodeBlock',      text: '```language\ncode here\n```\n' },
    { locKey: 'CodeEditor_Snippet_Blockquote',     text: '> Quoted text here\n' },
    { separator: true },
    { locKey: 'CodeEditor_Snippet_Link',           text: '[title](https://example.com)' },
    { locKey: 'CodeEditor_Snippet_Image',          text: '![alt text](image.png)' },
    { separator: true },
    { locKey: 'CodeEditor_Snippet_Table',          text: '| Header 1 | Header 2 | Header 3 |\n| -------- | -------- | -------- |\n| Cell     | Cell     | Cell     |\n| Cell     | Cell     | Cell     |\n' },
    { locKey: 'CodeEditor_Snippet_Footnote',       text: 'Here is a footnote reference.[^1]\n\n[^1]: Footnote text here.\n' },
    { locKey: 'CodeEditor_Snippet_HorizontalRule', text: '\n---\n' }
];

export function getSnippetSet(name) {
    if (name === 'markdown') {
        return MARKDOWN_SNIPPETS;
    }
    return [];
}
