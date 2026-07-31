// Card-list editor for the console's Automation settings: one expander card per entry, with add, delete and
// optional reorder controls. The cards are the source of truth for the setting they edit, the way the form
// inputs are for the rest of the settings.
//
// The module drives the DOM and nothing else. Localization, the change notification and the writable check
// are injected, so it carries no dependency on the celbridge client.
//
//   const list = createCardList({
//     listElement, emptyElement, addButton, template,
//     blankItem: () => ({ ... }),   // the entry an added card starts from
//     focusSelector: '.some-input', // focused when a card is added
//     reorderable: true,            // wires the move-up / move-down buttons
//     fillCard(card, item) {},      // entry -> inputs
//     readCard(card) {},            // inputs -> entry, or null to drop the card from the result
//     updateHeader(card) {},        // refresh the collapsed summary
//     localize(card) {},            // apply localized strings to a cloned card
//     onChanged() {},               // an edit the document should record
//     isWritable() {},              // false disables every control
//   });

export function createCardList(options) {
    const {
        listElement,
        emptyElement,
        addButton,
        template,
        blankItem,
        focusSelector,
        reorderable = false,
        fillCard,
        readCard,
        updateHeader,
        localize,
        onChanged,
        isWritable,
    } = options;

    function createCard(item) {
        const card = template.content.firstElementChild.cloneNode(true);
        localize(card);
        fillCard(card, item);

        for (const input of card.querySelectorAll('input')) {
            input.addEventListener('input', () => {
                updateHeader(card);
                onChanged();
            });
        }

        // The card controls sit inside the <summary>, whose default action toggles the card open, so each
        // one cancels that before acting.
        card.querySelector('.card-delete').addEventListener('click', (event) => {
            event.preventDefault();
            card.remove();
            refreshState();
            onChanged();
        });

        if (reorderable) {
            card.querySelector('.card-move-up').addEventListener('click', (event) => {
                event.preventDefault();
                moveCard(card, -1);
            });
            card.querySelector('.card-move-down').addEventListener('click', (event) => {
                event.preventDefault();
                moveCard(card, 1);
            });
        }

        return card;
    }

    // The header is filled in after the card is in the document, so a header that resolves styles (the
    // shortcut card's icon glyph) has them available.
    function appendCard(item) {
        const card = createCard(item);
        listElement.appendChild(card);
        updateHeader(card);

        return card;
    }

    // Moving the element keeps its open state and its in-progress edits, which rebuilding the list would
    // discard.
    function moveCard(card, offset) {
        if (offset < 0 && card.previousElementSibling) {
            listElement.insertBefore(card, card.previousElementSibling);
        } else if (offset > 0 && card.nextElementSibling) {
            listElement.insertBefore(card.nextElementSibling, card);
        } else {
            return;
        }

        refreshState();
        onChanged();
    }

    function populate(items) {
        listElement.replaceChildren();
        for (const item of items || []) {
            appendCard(item);
        }

        refreshState();
    }

    function read() {
        const items = [];

        for (const card of listElement.children) {
            const item = readCard(card);
            if (item !== null) {
                items.push(item);
            }
        }

        return items;
    }

    // Shows the empty-list message, applies the document's writable state, and stops the end cards moving
    // past the ends. The blanket writable pass runs first so the move buttons settle last.
    function refreshState() {
        const writable = isWritable();
        const cards = Array.from(listElement.children);

        emptyElement.classList.toggle('hidden', cards.length > 0);
        addButton.disabled = !writable;

        for (const control of listElement.querySelectorAll('input, button')) {
            control.disabled = !writable;
        }

        if (!reorderable) {
            return;
        }

        cards.forEach((card, index) => {
            card.querySelector('.card-move-up').disabled = !writable || index === 0;
            card.querySelector('.card-move-down').disabled = !writable || index === cards.length - 1;
        });
    }

    addButton.addEventListener('click', () => {
        const card = appendCard(blankItem());
        card.open = true;
        refreshState();
        card.querySelector(focusSelector).focus();
    });

    return { populate, read, refreshState };
}
