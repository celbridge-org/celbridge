// Editor for a setting that is a list of records: one expander card per entry, with add, delete and reorder
// controls. The cards are the source of truth for the setting they edit, the way form inputs are for a
// setting that is a single value.
//
// The module drives the DOM and nothing else. Localization, the change notification and the writable check
// are injected, so it carries no dependency on the rest of the client.
//
// The card template supplies the structural classes the module drives (`.cel-card-grip`, `.cel-card-delete`)
// and celbridge.css styles them, along with `.cel-card-list` on the list element.
//
//   const list = createCardList({
//     listElement, emptyElement, addButton, template,
//     blankItem: () => ({ ... }),   // the entry an added card starts from
//     focusSelector: '.some-input', // focused when a card is added
//     reorderable: false,           // opt out of the grip handle and Alt+Up / Alt+Down on the header
//     fillCard(card, item) {},      // entry -> inputs
//     readCard(card) {},            // inputs -> entry, or null to drop the card from the result
//     updateHeader(card) {},        // refresh the collapsed summary
//     localize(card) {},            // apply localized strings to a cloned card
//     onChanged() {},               // an edit the document should record
//     isWritable() {},              // false disables every control
//   });

// Places a dragged row for a pointer position: the slot it should occupy, and how far it sits from that
// slot's resting position so it stays under the pointer. The row is held inside the list, so dragging past
// either end pins it to the first or last slot instead of carrying it off the list.
//
// Every input is either the pointer or a measurement taken at the grab, never where the rows currently sit,
// so a row that has just moved cannot feed back into the next placement and set the list oscillating.
// Returns null for a list too small to measure, which simply does not reorder.
export function placementForPointer(options) {
    const { pointerY, grabOffset, listTop, rowPitch, rowCount } = options;

    if (rowPitch <= 0) {
        return null;
    }

    const lastSlotTop = listTop + (rowCount - 1) * rowPitch;
    const rowTop = Math.max(listTop, Math.min(pointerY - grabOffset, lastSlotTop));
    const slotIndex = Math.round((rowTop - listTop) / rowPitch);

    return { slotIndex, offset: rowTop - (listTop + slotIndex * rowPitch) };
}

export function createCardList(options) {
    const {
        listElement,
        emptyElement,
        addButton,
        template,
        blankItem,
        focusSelector,
        // A card list is a list the user curates, and its file order is what gets persisted, so it is
        // reorderable unless a caller says otherwise.
        reorderable = true,
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
        card.querySelector('.cel-card-delete').addEventListener('click', (event) => {
            event.preventDefault();
            card.remove();
            refreshState();
            onChanged();
        });

        if (reorderable) {
            const grip = card.querySelector('.cel-card-grip');

            grip.addEventListener('pointerdown', (event) => {
                // Only the primary button starts a drag.
                if (event.button !== 0) {
                    return;
                }
                beginDrag(card, event);
            });

            // The grip sits inside the <summary>, so the click ending a press on it would otherwise toggle
            // the card. A handle is not a toggle, and the press may just have collapsed the list.
            grip.addEventListener('click', (event) => event.preventDefault());

            // The keyboard path to the same reorder. The summary is focusable already, and Alt+Arrow carries
            // no default action on it, so this adds no tab stop and steals no key.
            card.querySelector('summary').addEventListener('keydown', (event) => {
                if (!event.altKey) {
                    return;
                }

                if (event.key === 'ArrowUp') {
                    moveCard(card, -1);
                } else if (event.key === 'ArrowDown') {
                    moveCard(card, 1);
                } else {
                    return;
                }

                event.preventDefault();
                card.querySelector('summary').focus();
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
        if (!isWritable()) {
            return;
        }

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

    // Reordering by drag, with the list reordering live under the pointer. Every card collapses on grab and
    // stays collapsed, which gives the rows a uniform pitch; the dragged row then follows the pointer, and
    // its slot follows the row. Neither reads where the rows currently sit, which is the one-way dependency
    // that lets them move during the drag without the list oscillating.
    let dragState = null;

    function beginDrag(card, event) {
        if (!isWritable()) {
            return;
        }

        // A drag can only run against a collapsed list, since uniform rows are what make the placement below
        // work. Collapsing as part of the grab would shorten everything above the grabbed row and slide it
        // out from under the pointer, so the first press on a grip while anything is open only tidies the
        // list; the user presses again to drag. The cards stay collapsed afterwards.
        const expandedCards = Array.from(listElement.children).filter((other) => other.open);
        if (expandedCards.length > 0) {
            for (const expandedCard of expandedCards) {
                expandedCard.open = false;
            }

            return;
        }

        const cards = Array.from(listElement.children);
        const rects = cards.map((other) => other.getBoundingClientRect());
        const originalIndex = cards.indexOf(card);

        // The pitch is measured between two rows rather than from one row's height, so it includes the gap
        // between cards. A single card cannot be reordered, so its own height is a harmless stand-in.
        let rowPitch = rects[0].height;
        if (rects.length > 1) {
            rowPitch = rects[1].top - rects[0].top;
        }

        dragState = {
            card,
            cards,
            otherCards: cards.filter((other) => other !== card),
            originalIndex,
            slotIndex: originalIndex,
            // Where the pointer took hold within the row. Exact, because the list was already collapsed when
            // the press landed, so nothing moved between the press and this measurement.
            grabOffset: event.clientY - rects[originalIndex].top,
            rowPitch,
        };

        card.classList.add('dragging');
        listElement.classList.add('reordering');

        event.preventDefault();
        window.addEventListener('pointermove', onDragMove);
        window.addEventListener('pointerup', dropDrag);
        window.addEventListener('pointercancel', cancelDrag);
        window.addEventListener('keydown', onDragKeyDown);
    }

    function onDragMove(event) {
        const placement = placementForPointer({
            pointerY: event.clientY,
            grabOffset: dragState.grabOffset,
            // Read live: the list's own box is unaffected by the rows reordering inside it or by their
            // transforms, but it does follow the panel scrolling under the drag.
            listTop: listElement.getBoundingClientRect().top,
            rowPitch: dragState.rowPitch,
            rowCount: dragState.cards.length,
        });

        if (placement === null) {
            return;
        }

        applySlot(placement.slotIndex);

        // Transforms do not affect layout, so holding the card away from its slot neither moves the rows
        // around it nor feeds back into the next placement.
        dragState.card.style.transform = `translateY(${placement.offset}px)`;
    }

    // The other cards hold their relative order for the whole drag, so inserting before whichever one now
    // holds the target slot always produces the same arrangement, whatever the list currently looks like.
    // That makes reapplying a slot a no-op and leaves no incremental state to drift.
    function applySlot(slotIndex) {
        if (slotIndex === dragState.slotIndex) {
            return;
        }

        dragState.slotIndex = slotIndex;
        animateDisplacedCards(() => {
            listElement.insertBefore(dragState.card, dragState.otherCards[slotIndex] || null);
        });
    }

    // Slides the cards the reorder displaced, by measuring them either side of the change, putting them back
    // where they were with a transform, then letting the CSS transition carry that transform away. The
    // dragged card is left out so it snaps to the slot under the pointer instead of lagging behind it.
    //
    // Purely decorative: the slot arithmetic reads the pointer delta and a pitch measured once at the grab,
    // never the rows, so a transform in flight cannot feed back into where the next slot lands.
    function animateDisplacedCards(applyReorder) {
        if (prefersReducedMotion()) {
            applyReorder();
            return;
        }

        const displacedCards = dragState.otherCards;

        // Measured live rather than from the grab snapshot, so a card interrupted mid-slide continues from
        // where it visually is instead of jumping back to its resting position.
        const positionsBefore = new Map();
        for (const displacedCard of displacedCards) {
            positionsBefore.set(displacedCard, displacedCard.getBoundingClientRect().top);
        }

        applyReorder();

        for (const displacedCard of displacedCards) {
            const offset = positionsBefore.get(displacedCard) - displacedCard.getBoundingClientRect().top;
            if (offset === 0) {
                continue;
            }

            displacedCard.style.transition = 'none';
            displacedCard.style.transform = `translateY(${offset}px)`;
        }

        // Reading a layout property commits the inverted transforms, so re-enabling the transition below
        // animates away from them rather than the browser coalescing both writes into no visible change.
        listElement.getBoundingClientRect();

        for (const displacedCard of displacedCards) {
            displacedCard.style.transition = '';
            displacedCard.style.transform = '';
        }
    }

    function prefersReducedMotion() {
        return window.matchMedia !== undefined
            && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    }

    function onDragKeyDown(event) {
        if (event.key === 'Escape') {
            event.preventDefault();
            cancelDrag();
        }
    }

    function dropDrag() {
        if (dragState === null) {
            return;
        }

        const moved = dragState.slotIndex !== dragState.originalIndex;
        finishDrag();

        // Reported once, on release, rather than on each slot: a notification per slot would mark the
        // document dirty and re-render anything driven by the order all the way through the drag.
        if (moved) {
            refreshState();
            onChanged();
        }
    }

    // Escape, or a pointer the browser has taken over, puts the list back exactly as it was. Re-appending the
    // snapshot in order restores it whatever slot the card had reached.
    function cancelDrag() {
        if (dragState === null) {
            return;
        }

        for (const snapshotCard of dragState.cards) {
            listElement.appendChild(snapshotCard);
        }

        finishDrag();
    }

    function finishDrag() {
        window.removeEventListener('pointermove', onDragMove);
        window.removeEventListener('pointerup', dropDrag);
        window.removeEventListener('pointercancel', cancelDrag);
        window.removeEventListener('keydown', onDragKeyDown);

        // A slide still in flight ends here: the DOM order is already final, so the card snaps the last few
        // pixels rather than animating on after the gesture is over.
        for (const snapshotCard of dragState.cards) {
            snapshotCard.style.transition = '';
            snapshotCard.style.transform = '';
        }

        dragState.card.classList.remove('dragging');
        listElement.classList.remove('reordering');
        dragState = null;
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

    // Shows the empty-list message and applies the document's writable state. The grip is not a control, so
    // read-only is carried on the list for the styling and enforced in the gesture itself.
    function refreshState() {
        const writable = isWritable();

        emptyElement.classList.toggle('hidden', listElement.children.length > 0);
        addButton.disabled = !writable;

        for (const control of listElement.querySelectorAll('input, button')) {
            control.disabled = !writable;
        }

        if (reorderable) {
            listElement.classList.toggle('reorder-disabled', !writable);
        }
    }

    addButton.addEventListener('click', () => {
        const card = appendCard(blankItem());
        card.open = true;
        refreshState();
        card.querySelector(focusSelector).focus();
    });

    return { populate, read, refreshState };
}
