// Link toolbar module for Note editor
// Phase 1: Existing toggleLink functionality
// Phase 2: Will add floating link bubble

let ctx = null;

/**
 * Initialize link toolbar and event handlers.
 * @param {Object} context - Shared context with editor instance
 */
export function init(context) {
    ctx = context;
    const editorWrapper = document.getElementById('editor-wrapper');

    // Handle link clicks — send to C# host for routing
    editorWrapper.addEventListener('click', (e) => {
        const link = e.target.closest('.tiptap a');
        if (!link) return;

        e.preventDefault();
        e.stopPropagation();

        const href = link.getAttribute('href');
        if (href) {
            ctx.sendMessage({ type: 'link-clicked', payload: { href } });
        }
    });
}

/**
 * Toggle or edit a link on the current selection.
 * Called from the main toolbar.
 */
export async function toggleLink() {
    const currentHref = ctx.editor.isActive('link')
        ? ctx.editor.getAttributes('link').href || ''
        : '';

    const url = await ctx.showPrompt('URL:', currentHref);
    if (url === null) return;

    if (url.trim() === '') {
        ctx.editor.chain().focus().unsetLink().run();
    } else {
        ctx.editor.chain().focus().setLink({ href: url.trim() }).run();
    }
}
