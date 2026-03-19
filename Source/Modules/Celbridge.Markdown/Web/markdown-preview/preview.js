// Markdown Preview - Renders markdown content using marked.js
// Uses celbridge.js SDK for JSON-RPC communication with the .NET host

import { marked, markedHighlight, hljs } from './lib/marked.esm.js';
import { Celbridge } from 'https://celbridge-client.celbridge/celbridge.js';

// Create Celbridge instance for RPC communication
const celbridge = new Celbridge();

// Document base path (relative to project root, e.g., "01_markdown/")
// Set by the host via codePreview/setBasePath notification
let documentBasePath = '';

// Configure marked with GitHub-flavored markdown options and error handling
marked.setOptions({
    gfm: true,
    breaks: false,
    pedantic: false,
    silent: true  // Don't throw on errors, return error text instead
});

// Configure syntax highlighting using highlight.js
marked.use(markedHighlight({
    langPrefix: 'hljs language-',
    highlight(code, lang) {
        if (lang && hljs.getLanguage(lang)) {
            try {
                return hljs.highlight(code, { language: lang }).value;
            } catch (err) {
                console.warn('Highlight error for language:', lang, err);
            }
        }
        // Auto-detect language if not specified or not recognized
        try {
            return hljs.highlightAuto(code).value;
        } catch (err) {
            console.warn('Highlight auto-detect error:', err);
            return code;
        }
    }
}));

/**
 * Resolves a relative path against the document's base path.
 * @param {string} relativePath - The relative path from the markdown file.
 * @returns {string} - The full path relative to project root.
 */
function resolveRelativePath(relativePath) {
    if (!relativePath) return '';

    // Already absolute URL
    if (relativePath.startsWith('http://') || 
        relativePath.startsWith('https://') || 
        relativePath.startsWith('data:') ||
        relativePath.startsWith('/')) {
        return relativePath;
    }

    // Combine document base path with relative path
    if (documentBasePath) {
        return documentBasePath + relativePath;
    }

    return relativePath;
}

// Custom renderer for GFM features
// Note: We only customize specific elements. For lists, we let marked handle
// the structure natively and only customize listitem for task checkboxes.
const renderer = {
    // Generate heading IDs for anchor link support
    heading(token) {
        const text = this.parser.parseInline(token.tokens);
        // Generate slug from text (lowercase, replace spaces with hyphens, remove special chars)
        const slug = token.text
            .toLowerCase()
            .trim()
            .replace(/\s+/g, '-')
            .replace(/[^\w\-]/g, '');
        return `<h${token.depth} id="${slug}">${text}</h${token.depth}>\n`;
    },

    // Handle task list items (marked v5+ API uses object parameters)
    // We only add checkbox handling - let marked handle the content parsing
    listitem(token) {
        try {
            // Let marked's default parser handle the content
            // This properly handles nested lists, paragraphs, etc.
            let itemContent = '';

            if (token.tokens && Array.isArray(token.tokens)) {
                // Use the full parser for all content (handles nested lists correctly)
                itemContent = this.parser.parse(token.tokens);
            } else if (token.text) {
                itemContent = token.text;
            }

            // Trim trailing newlines/whitespace from parsed content
            itemContent = itemContent.trim();

            if (token.task) {
                const checkbox = token.checked
                    ? '<input type="checkbox" checked disabled>'
                    : '<input type="checkbox" disabled>';
                return `<li class="task-list-item">${checkbox} ${itemContent}</li>\n`;
            }

            return `<li>${itemContent}</li>\n`;
        } catch (error) {
            console.warn('Error rendering list item:', error);
            return `<li>${token.text || token.raw || ''}</li>\n`;
        }
    },

    // Note: We do NOT override list() - let marked handle list structure natively
    // This ensures nested lists work correctly

    // Handle images - transform local paths to project.celbridge URLs
    image(token) {
        try {
            let src = token.href || '';
            const alt = token.text || '';
            const title = token.title || '';

            // Transform local image paths to project.celbridge virtual host
            if (src && !src.startsWith('http://') && !src.startsWith('https://') && !src.startsWith('data:')) {
                // Resolve relative to document, then prefix with project.celbridge
                const resolvedPath = resolveRelativePath(src);
                src = `https://project.celbridge/${resolvedPath}`;
            }

            const titleAttr = title ? ` title="${title}"` : '';
            return `<img src="${src}" alt="${alt}"${titleAttr}>`;
        } catch (error) {
            console.warn('Error rendering image:', error);
            return `<span style="color: var(--hl-keyword);">[Image error]</span>`;
        }
    },

    // Handle links - transform local paths for resource links
    link(token) {
        try {
            let href = token.href || '';
            let text = '';

            if (token.tokens && Array.isArray(token.tokens)) {
                text = this.parser.parseInline(token.tokens);
            } else {
                text = token.text || href;
            }

            const title = token.title || '';

            // Check if this is a local resource link (not http/https/mailto/etc.)
            const isLocalLink = href &&
                !href.startsWith('http://') &&
                !href.startsWith('https://') &&
                !href.startsWith('mailto:') &&
                !href.startsWith('#');

            if (isLocalLink) {
                // Do NOT resolve the path here - C# will resolve it relative to the document
                // Just mark it as a local resource link for click handling
                const titleAttr = title ? ` title="${title}"` : '';
                return `<a href="${href}" data-local-resource="true"${titleAttr}>${text}</a>`;
            }

            // External or anchor link
            const titleAttr = title ? ` title="${title}"` : '';
            return `<a href="${href}"${titleAttr}>${text}</a>`;
        } catch (error) {
            console.warn('Error rendering link:', error);
            return `<span style="color: var(--hl-keyword);">[Link error]</span>`;
        }
    }
};

marked.use({ renderer });

// DOM elements
const previewContent = document.getElementById('preview-content');

// Current markdown content
let currentContent = '';

/**
 * Renders markdown content to HTML and updates the preview pane.
 * Uses graceful error recovery - if parsing fails, shows the error inline
 * rather than breaking the entire preview.
 * @param {string} markdown - The markdown content to render.
 */
function renderMarkdown(markdown) {
    currentContent = markdown;

    if (!markdown || markdown.trim() === '') {
        previewContent.innerHTML = '';
        return;
    }

    try {
        const html = marked.parse(markdown);
        previewContent.innerHTML = html;

        // Set up link click handlers
        setupLinkHandlers();
    } catch (error) {
        console.error('Error rendering markdown:', error);

        // Try to render with a fallback - escape HTML and show as preformatted
        try {
            const escapedMarkdown = markdown
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;');

            previewContent.innerHTML = `
                <div class="markdown-error">
                    <p><strong>Preview error:</strong> ${error.message}</p>
                    <p>Showing raw markdown:</p>
                </div>
                <pre style="white-space: pre-wrap; word-wrap: break-word;">${escapedMarkdown}</pre>
            `;
        } catch (fallbackError) {
            previewContent.innerHTML = `<p style="color: var(--hl-keyword);">Error rendering markdown: ${error.message}</p>`;
        }
    }
}

/**
 * Sets up click handlers for links in the preview.
 */
function setupLinkHandlers() {
    const links = previewContent.querySelectorAll('a[href]');

    links.forEach(link => {
        const href = link.getAttribute('href');
        const isLocalResource = link.getAttribute('data-local-resource') === 'true';

        // Check for anchor links - browser may resolve #anchor to full URL
        // so we check if href ends with #something on the same page
        const currentUrl = window.location.href.split('#')[0];
        const isAnchorLink = href && (
            href.startsWith('#') || 
            (href.startsWith(currentUrl) && href.includes('#'))
        );

        if (isLocalResource) {
            // Local resource link - use celbridge.js to open in editor
            link.addEventListener('click', (e) => {
                e.preventDefault();
                celbridge.codePreview.openResource(href);
            });
        } else if (isAnchorLink) {
            // Anchor link - scroll within preview
            link.addEventListener('click', (e) => {
                e.preventDefault();
                // Extract the anchor part (after #)
                const hashIndex = href.indexOf('#');
                const targetId = hashIndex >= 0 ? href.slice(hashIndex + 1) : '';
                const target = document.getElementById(targetId);
                if (target) {
                    target.scrollIntoView({ behavior: 'smooth' });
                }
            });
        } else if (href && (href.startsWith('http://') || href.startsWith('https://'))) {
            // External link - use celbridge.js to open in browser
            link.addEventListener('click', (e) => {
                e.preventDefault();
                celbridge.codePreview.openExternal(href);
            });
        }
    });
}

/**
 * Scrolls the preview to approximately match the source editor position.
 * @param {number} scrollPercentage - The scroll position as a percentage (0-1).
 */
function scrollToPosition(scrollPercentage) {
    const container = document.getElementById('preview-container');
    const scrollHeight = container.scrollHeight - container.clientHeight;
    container.scrollTop = scrollHeight * scrollPercentage;
}

/**
 * Sets up click-to-sync functionality.
 * When the user clicks in the preview (not on a link), sends the click position
 * to the host so the editor can scroll to the corresponding location.
 */
function setupClickToSync() {
    const container = document.getElementById('preview-container');

    container.addEventListener('click', (e) => {
        // Ignore clicks on links (they have their own handlers)
        if (e.target.closest('a')) {
            return;
        }

        // Calculate click position as a percentage of the scroll height
        const containerRect = container.getBoundingClientRect();
        const clickY = e.clientY - containerRect.top + container.scrollTop;
        const totalHeight = container.scrollHeight;

        const percentage = totalHeight > 0 ? clickY / totalHeight : 0;

        // Use celbridge.js to sync editor scroll position
        celbridge.codePreview.syncToEditor(Math.max(0, Math.min(1, percentage)));
    });
}

// Initialize preview and set up RPC handlers
function init() {
    console.log('Markdown preview initializing with celbridge.js SDK');

    // Register handler for document context updates
    celbridge.codePreview.onSetBasePath((basePath) => {
        // Ensure the base path ends with a slash if not empty
        if (basePath && !basePath.endsWith('/')) {
            basePath += '/';
        }
        documentBasePath = basePath || '';
        console.log('Document base path set to:', documentBasePath);
    });

    // Register handler for content updates
    celbridge.codePreview.onUpdate((content) => {
        renderMarkdown(content);
    });

    // Register handler for scroll position changes
    celbridge.codePreview.onScroll((percentage) => {
        scrollToPosition(percentage);
    });

    // Set up click-to-sync handler
    setupClickToSync();

    console.log('Markdown preview initialized');
}

// Initialize when DOM is ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
} else {
    init();
}
