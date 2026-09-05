// Behaviour for the shared `.cel-section-switcher` settings surface (styled by ui/section-switcher.css),
// mirroring the native SettingsSectionSwitcher. Mark up a `.cel-section-nav-item` row, a
// `.cel-section-header` and a child of `.cel-section-content` per section, each carrying the same
// `data-section` id, then call attachSectionSwitcher() to drive them. The helper owns `aria-selected`, the
// roving tabindex, the arrow keys, which header and section are showing, each section's scroll position, and
// which of the two layouts the surface is in.
//
//   const switcher = attachSectionSwitcher(rootElement, {
//     onChange(sectionId) { /* the selection moved */ },
//   });
//   switcher.select('shortcuts');   // select programmatically, e.g. when restoring view state
//   switcher.selected();            // the current section id
//   switcher.scrollTop();           // the selected section's scroll offset, worth persisting with the id
//   switcher.setScrollTop(240);     // apply a persisted offset to the selected section
//   switcher.setNotice(message);    // show the notice slot, or hide it with an empty message
//   switcher.setReadOnly(true);     // disable every control inside the sections
//
// The initially selected row is the one marked aria-selected="true" in the markup, or the first row. A panel
// whose section set is not known ahead of time generates its rows, headers and sections before attaching.
//
// The layout is written to data-layout on the root: `inline` where the nav fits beside the section, and
// `stacked` where it does not, measured against --cel-section-stack-threshold. Markup naming one of them
// holds it, and the module then only keeps aria-orientation in step.

// Used where the stylesheet has not been served, which leaves the switcher measuring against nothing.
const STACK_THRESHOLD_FALLBACK = 387;

export function attachSectionSwitcher(rootElement, options = {}) {
    if (!rootElement) {
        return {
            select() { },
            selected() {
                return null;
            },
            scrollTop() {
                return 0;
            },
            setScrollTop() { },
            setNotice() { },
            setReadOnly() { },
        };
    }

    const { onChange } = options;

    const navElement = rootElement.querySelector('.cel-section-nav');
    const noticeElement = rootElement.querySelector('.cel-section-notice');
    const contentElement = rootElement.querySelector('.cel-section-content');
    const navItems = Array.from(rootElement.querySelectorAll('.cel-section-nav-item'));
    const headers = Array.from(rootElement.querySelectorAll('.cel-section-header'));
    const sections = contentElement ? Array.from(contentElement.children) : [];

    // Each section's scroll offset, keyed by section id.
    const scrollOffsets = new Map();

    // A layout named in the markup is the author's, so the measurement never overrides it.
    const authoredLayout = rootElement.dataset.layout || 'auto';

    let selectedId = null;
    let lastWidth = 0;

    function sectionIdOf(element) {
        return element.dataset.section || '';
    }

    function selectedSection() {
        return sections.find((section) => sectionIdOf(section) === selectedId) || null;
    }

    function applyScrollOffset() {
        const section = selectedSection();
        if (!section) {
            return;
        }

        section.scrollTop = scrollOffsets.get(selectedId) || 0;
    }

    function select(sectionId, notify) {
        const target = navItems.find((item) => sectionIdOf(item) === sectionId);
        if (!target || sectionId === selectedId) {
            return;
        }

        selectedId = sectionId;

        for (const item of navItems) {
            const isSelected = item === target;
            item.setAttribute('aria-selected', isSelected ? 'true' : 'false');
            // Roving tabindex: only the selected row is in the tab order, so Tab steps over the nav as one
            // control.
            item.tabIndex = isSelected ? 0 : -1;
        }

        for (const header of headers) {
            header.hidden = sectionIdOf(header) !== sectionId;
        }

        for (const section of sections) {
            section.hidden = sectionIdOf(section) !== sectionId;
        }

        applyScrollOffset();

        if (notify && typeof onChange === 'function') {
            onChange(sectionId);
        }
    }

    function selectByOffset(offset) {
        const currentIndex = navItems.findIndex((item) => sectionIdOf(item) === selectedId);
        const nextIndex = (currentIndex + offset + navItems.length) % navItems.length;
        const nextItem = navItems[nextIndex];
        select(sectionIdOf(nextItem), true);
        nextItem.focus();
    }

    function resolveLayout(width) {
        if (authoredLayout === 'inline' ||
            authoredLayout === 'stacked') {
            return authoredLayout;
        }

        const declaredThreshold = getComputedStyle(rootElement)
            .getPropertyValue('--cel-section-stack-threshold');
        let threshold = Number.parseFloat(declaredThreshold);
        if (!Number.isFinite(threshold) ||
            threshold <= 0) {
            threshold = STACK_THRESHOLD_FALLBACK;
        }

        if (width >= threshold) {
            return 'inline';
        }

        return 'stacked';
    }

    // A hidden surface reports no width, so the last resolved layout stands until it is on screen again.
    function applyLayout(width) {
        if (width <= 0) {
            return;
        }

        const layout = resolveLayout(width);
        rootElement.dataset.layout = layout;

        if (navElement) {
            navElement.setAttribute('aria-orientation', layout === 'inline' ? 'vertical' : 'horizontal');
        }
    }

    for (const item of navItems) {
        item.addEventListener('click', () => select(sectionIdOf(item), true));
    }

    for (const section of sections) {
        section.addEventListener('scroll', () => {
            if (section.hidden) {
                return;
            }

            scrollOffsets.set(sectionIdOf(section), section.scrollTop);
        }, { passive: true });
    }

    if (navElement) {
        navElement.addEventListener('keydown', (event) => {
            // Both axes move the selection: the nav is a column in one layout and a strip in the other.
            if (event.key === 'ArrowLeft' ||
                event.key === 'ArrowUp') {
                selectByOffset(-1);
            } else if (event.key === 'ArrowRight' ||
                event.key === 'ArrowDown') {
                selectByOffset(1);
            } else if (event.key === 'Home') {
                select(sectionIdOf(navItems[0]), true);
                navItems[0].focus();
            } else if (event.key === 'End') {
                const lastItem = navItems[navItems.length - 1];
                select(sectionIdOf(lastItem), true);
                lastItem.focus();
            } else {
                return;
            }

            event.preventDefault();
        });
    }

    // The observer is also what reports the surface coming back on screen, which is where each section's
    // scroll offset has to be put back: hiding the surface destroyed the scroll boxes holding them.
    if (typeof ResizeObserver === 'function') {
        const resizeObserver = new ResizeObserver((entries) => {
            for (const entry of entries) {
                const width = entry.contentRect.width;
                applyLayout(width);

                if (width > 0 &&
                    lastWidth === 0) {
                    applyScrollOffset();
                }

                lastWidth = width;
            }
        });

        resizeObserver.observe(rootElement);
    }

    function setReadOnly(isReadOnly) {
        rootElement.dataset.readonly = isReadOnly ? 'true' : 'false';

        for (const section of sections) {
            for (const control of section.querySelectorAll('input, select, textarea, button')) {
                control.disabled = isReadOnly;
            }
        }
    }

    if (rootElement.dataset.readonly === 'true') {
        setReadOnly(true);
    }

    const initialItem = navItems.find((item) => item.getAttribute('aria-selected') === 'true') || navItems[0];
    if (initialItem) {
        // Clear the markup's selection first, so the initial apply always runs and normalizes every row.
        initialItem.setAttribute('aria-selected', 'false');
        select(sectionIdOf(initialItem), true);
    }

    applyLayout(rootElement.getBoundingClientRect().width);

    return {
        select(sectionId) {
            select(sectionId, true);
        },
        selected() {
            return selectedId;
        },
        scrollTop() {
            return scrollOffsets.get(selectedId) || 0;
        },
        setScrollTop(offset) {
            if (selectedId === null) {
                return;
            }

            scrollOffsets.set(selectedId, offset);
            applyScrollOffset();
        },
        setNotice(message) {
            if (!noticeElement) {
                return;
            }

            const text = message || '';
            noticeElement.textContent = text;
            noticeElement.hidden = text === '';
        },
        setReadOnly,
    };
}
