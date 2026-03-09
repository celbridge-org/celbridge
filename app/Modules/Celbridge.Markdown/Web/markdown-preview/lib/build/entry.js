// Entry point for marked.js vendor bundle
// Re-exports the marked library for use in the markdown preview

export { marked, Renderer, Lexer, Parser } from 'marked';
export { markedHighlight } from 'marked-highlight';
export { default as hljs } from 'highlight.js';
