// Selection behaviour for the shared `.cel-nav-tabs` strip (styled by celbridge.css), matching the native
// settings panels' icon tab strip. Mark up the tabs as `.cel-nav-tab` buttons carrying a `data-section` id,
// then call attachNavTabs() to make them selectable. The helper owns `aria-selected`, the roving tabindex,
// and arrow-key navigation, and reports the selected section id.
//
//   const tabs = attachNavTabs(stripElement, {
//     onChange(sectionId) { /* show that section */ },
//   });
//   tabs.select('environment');   // select programmatically, e.g. when restoring view state
//   tabs.selected();              // the current section id
//
// The initially selected tab is the one marked aria-selected="true" in the markup, or the first tab.

export function attachNavTabs(stripElement, options = {}) {
    if (!stripElement) {
        return {
            select() { },
            selected() {
                return null;
            },
        };
    }

    const { onChange } = options;
    const tabs = Array.from(stripElement.querySelectorAll('.cel-nav-tab'));
    let selectedId = null;

    function sectionIdOf(tab) {
        return tab.dataset.section || '';
    }

    function select(sectionId, notify) {
        const target = tabs.find((tab) => sectionIdOf(tab) === sectionId);
        if (!target || sectionId === selectedId) {
            return;
        }

        selectedId = sectionId;

        for (const tab of tabs) {
            const isSelected = tab === target;
            tab.setAttribute('aria-selected', isSelected ? 'true' : 'false');
            // Roving tabindex: only the selected tab is in the tab order, so Tab steps over the strip as one
            // control and the arrow keys move within it.
            tab.tabIndex = isSelected ? 0 : -1;
        }

        if (notify && typeof onChange === 'function') {
            onChange(sectionId);
        }
    }

    function selectByOffset(offset) {
        const currentIndex = tabs.findIndex((tab) => sectionIdOf(tab) === selectedId);
        const nextIndex = (currentIndex + offset + tabs.length) % tabs.length;
        const nextTab = tabs[nextIndex];
        select(sectionIdOf(nextTab), true);
        nextTab.focus();
    }

    for (const tab of tabs) {
        tab.addEventListener('click', () => select(sectionIdOf(tab), true));
    }

    stripElement.addEventListener('keydown', (event) => {
        if (event.key === 'ArrowLeft') {
            selectByOffset(-1);
        } else if (event.key === 'ArrowRight') {
            selectByOffset(1);
        } else if (event.key === 'Home') {
            select(sectionIdOf(tabs[0]), true);
            tabs[0].focus();
        } else if (event.key === 'End') {
            select(sectionIdOf(tabs[tabs.length - 1]), true);
            tabs[tabs.length - 1].focus();
        } else {
            return;
        }

        event.preventDefault();
    });

    const initialTab = tabs.find((tab) => tab.getAttribute('aria-selected') === 'true') || tabs[0];
    if (initialTab) {
        // Clear the markup's selection first, so the initial apply always runs and normalizes every tab.
        initialTab.setAttribute('aria-selected', 'false');
        select(sectionIdOf(initialTab), true);
    }

    return {
        select(sectionId) {
            select(sectionId, true);
        },
        selected() {
            return selectedId;
        },
    };
}
