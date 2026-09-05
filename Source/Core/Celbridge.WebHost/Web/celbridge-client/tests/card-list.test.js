// @vitest-environment jsdom

import { describe, it, expect, beforeEach, vi } from 'vitest';
import { createCardList, placementForPointer } from '../ui/card-list.js';

const ROW_HEIGHT = 40;
const ROW_GAP = 8;
const ROW_PITCH = ROW_HEIGHT + ROW_GAP;
const LIST_TOP = 100;
// Every drag below grabs a row 10px down from its top, so the pointer positions read as row top + 10.
const GRAB_OFFSET = 10;

// jsdom reports no layout, so the list and its rows are given stacked rects. The drag measures the pitch and
// the grab offset once, at grab time, and reads the list's own top as it goes.
function stubCardLayout() {
    const list = document.getElementById('cards');
    list.getBoundingClientRect = () => ({
        top: LIST_TOP, bottom: LIST_TOP + 400, height: 400, left: 0, right: 200, width: 200, x: 0, y: LIST_TOP,
    });

    [...list.children].forEach((card, index) => {
        const top = LIST_TOP + index * ROW_PITCH;
        card.getBoundingClientRect = () => ({
            top, bottom: top + ROW_HEIGHT, height: ROW_HEIGHT,
            left: 0, right: 200, width: 200, x: 0, y: top,
        });
    });
}

// Grabs the row at its own index's resting position, so the grab offset is a known 10px.
function startDrag(card) {
    stubCardLayout();
    const clientY = card.getBoundingClientRect().top + GRAB_OFFSET;
    card.querySelector('.cel-card-grip').dispatchEvent(
        new window.PointerEvent('pointerdown', { button: 0, clientY, bubbles: true, cancelable: true }));
}

// Moves the pointer so the dragged row's top lands at the given offset from the top of the list.
function dragToRowTop(offsetFromListTop) {
    window.dispatchEvent(new window.PointerEvent('pointermove', {
        clientY: LIST_TOP + offsetFromListTop + GRAB_OFFSET, bubbles: true,
    }));
}

// A minimal stand-in for the shortcut card template: the classes the card list drives, and one input.
const CARD_TEMPLATE = `
    <details class="cel-expander">
        <summary>
            <span class="cel-card-grip"></span>
            <span class="cel-card-title"></span>
            <span class="cel-card-actions">
                <button class="cel-icon-button cel-card-delete" type="button"></button>
            </span>
        </summary>
        <div class="cel-expander-body">
            <input class="card-name" type="text">
        </div>
    </details>`;

function buildList(overrides = {}) {
    document.body.innerHTML = `
        <div id="cards" class="cel-card-list"></div>
        <p id="empty"></p>
        <button id="add" type="button"></button>
        <template id="template">${CARD_TEMPLATE}</template>`;

    const onChanged = vi.fn();
    const list = createCardList({
        listElement: document.getElementById('cards'),
        emptyElement: document.getElementById('empty'),
        addButton: document.getElementById('add'),
        template: document.getElementById('template'),
        blankItem: () => ({ name: '' }),
        focusSelector: '.card-name',
        localize: () => { },
        onChanged,
        isWritable: () => true,
        fillCard(card, item) {
            card.querySelector('.card-name').value = item.name || '';
        },
        readCard(card) {
            const name = card.querySelector('.card-name').value.trim();
            if (name === '') {
                return null;
            }

            return { name };
        },
        updateHeader(card) {
            card.querySelector('.cel-card-title').textContent = card.querySelector('.card-name').value;
        },
        ...overrides,
    });

    return { list, onChanged };
}

function names() {
    return [...document.querySelectorAll('#cards .card-name')].map((input) => input.value);
}

function cardAt(index) {
    return document.getElementById('cards').children[index];
}

function pressOnHeader(card, key, { altKey = true } = {}) {
    card.querySelector('summary').dispatchEvent(
        new window.KeyboardEvent('keydown', { key, altKey, bubbles: true, cancelable: true }));
}

describe('createCardList', () => {
    let list;
    let onChanged;

    beforeEach(() => {
        ({ list, onChanged } = buildList());
        list.populate([{ name: 'first' }, { name: 'second' }, { name: 'third' }]);
        onChanged.mockClear();
    });

    it('renders one card per entry and reads them back in order', () => {
        expect(names()).toEqual(['first', 'second', 'third']);
        expect(list.read()).toEqual([{ name: 'first' }, { name: 'second' }, { name: 'third' }]);
    });

    it('replaces the previous cards on repopulate', () => {
        list.populate([{ name: 'only' }]);

        expect(names()).toEqual(['only']);
    });

    it('drops cards the reader rejects', () => {
        cardAt(1).querySelector('.card-name').value = '   ';

        expect(list.read()).toEqual([{ name: 'first' }, { name: 'third' }]);
    });

    it('shows the empty message only when the list has no cards', () => {
        expect(document.getElementById('empty').classList.contains('hidden')).toBe(true);

        list.populate([]);

        expect(document.getElementById('empty').classList.contains('hidden')).toBe(false);
    });

    it('appends an expanded card on add, without reporting a change yet', () => {
        document.getElementById('add').click();

        expect(names()).toEqual(['first', 'second', 'third', '']);
        expect(cardAt(3).open).toBe(true);
        // The blank card contributes nothing, so the document is not dirty until something is typed.
        expect(onChanged).not.toHaveBeenCalled();
    });

    it('reports a change when a card is edited', () => {
        const input = cardAt(0).querySelector('.card-name');
        input.value = 'edited';
        input.dispatchEvent(new window.Event('input', { bubbles: true }));

        expect(onChanged).toHaveBeenCalled();
        expect(cardAt(0).querySelector('.cel-card-title').textContent).toBe('edited');
    });

    it('removes a card on delete and reports the change', () => {
        // Expanded first because the delete button only shows on an open card, so this is the only way a
        // user reaches it.
        cardAt(1).open = true;
        cardAt(1).querySelector('.cel-card-delete').click();

        expect(names()).toEqual(['first', 'third']);
        expect(onChanged).toHaveBeenCalled();
    });

    it('moves a card with Alt+Up and Alt+Down, reporting each change', () => {
        pressOnHeader(cardAt(2), 'ArrowUp');
        expect(names()).toEqual(['first', 'third', 'second']);

        pressOnHeader(cardAt(0), 'ArrowDown');
        expect(names()).toEqual(['third', 'first', 'second']);
        expect(onChanged).toHaveBeenCalledTimes(2);
    });

    it('ignores an arrow key without Alt, leaving it to the page', () => {
        pressOnHeader(cardAt(1), 'ArrowUp', { altKey: false });

        expect(names()).toEqual(['first', 'second', 'third']);
        expect(onChanged).not.toHaveBeenCalled();
    });

    it('does not report a change when a move runs off either end', () => {
        pressOnHeader(cardAt(0), 'ArrowUp');
        pressOnHeader(cardAt(2), 'ArrowDown');

        expect(names()).toEqual(['first', 'second', 'third']);
        expect(onChanged).not.toHaveBeenCalled();
    });

    it('keeps a card open and its edits intact across a move', () => {
        cardAt(0).open = true;
        cardAt(0).querySelector('.card-name').value = 'in progress';

        pressOnHeader(cardAt(0), 'ArrowDown');

        expect(cardAt(1).open).toBe(true);
        expect(cardAt(1).querySelector('.card-name').value).toBe('in progress');
    });

    it('collapses the list instead of dragging when a card is expanded', () => {
        cardAt(0).open = true;
        cardAt(2).open = true;

        startDrag(cardAt(0));

        expect([...document.getElementById('cards').children].every((card) => !card.open)).toBe(true);
        expect(document.getElementById('cards').classList.contains('reordering')).toBe(false);

        // No drag is in progress, so the pointer moving does not reorder anything.
        dragToRowTop(ROW_PITCH * 2);
        expect(names()).toEqual(['first', 'second', 'third']);
        expect(onChanged).not.toHaveBeenCalled();
    });

    it('drags on the next press, once the list is collapsed', () => {
        cardAt(0).open = true;

        startDrag(cardAt(0));
        window.dispatchEvent(new window.PointerEvent('pointerup', { bubbles: true }));
        startDrag(cardAt(0));
        dragToRowTop(ROW_PITCH);

        expect(names()).toEqual(['second', 'first', 'third']);
    });

    it('leaves the cards collapsed after a drop', () => {
        startDrag(cardAt(0));
        dragToRowTop(ROW_PITCH);
        window.dispatchEvent(new window.PointerEvent('pointerup', { bubbles: true }));

        expect([...document.getElementById('cards').children].every((card) => !card.open)).toBe(true);
    });

    it('moves the card element itself on a drop, so a control in its body keeps its state', () => {
        const dragged = cardAt(0);
        // Stands in for state that lives on the element rather than in the entry.
        dragged.querySelector('.card-name').value = 'in progress';

        startDrag(dragged);
        dragToRowTop(ROW_PITCH * 2);
        window.dispatchEvent(new window.PointerEvent('pointerup', { bubbles: true }));

        expect(cardAt(2)).toBe(dragged);
        expect(cardAt(2).querySelector('.card-name').value).toBe('in progress');
    });

    it('does not toggle a card open when its grip is pressed', () => {
        const clickEvent = new window.MouseEvent('click', { bubbles: true, cancelable: true });
        cardAt(0).querySelector('.cel-card-grip').dispatchEvent(clickEvent);

        expect(clickEvent.defaultPrevented).toBe(true);
    });

    it('focuses the card whose grip is pressed, so the reorder keys act on the one it found', () => {
        cardAt(0).open = true;

        // The press only collapses the list, so this is the card marked without a drag having started.
        startDrag(cardAt(1));

        expect(document.activeElement).toBe(cardAt(1).querySelector('summary'));
    });

    it('reorders the list live as the pointer crosses each row', () => {
        startDrag(cardAt(0));

        dragToRowTop(ROW_PITCH);
        expect(names()).toEqual(['second', 'first', 'third']);

        dragToRowTop(ROW_PITCH * 2);
        expect(names()).toEqual(['second', 'third', 'first']);

        // Dragging back up returns it, so the live reorder is not one-way.
        dragToRowTop(0);
        expect(names()).toEqual(['first', 'second', 'third']);
    });

    it('holds the dragged card under the pointer between slots', () => {
        startDrag(cardAt(0));

        // Short of half a row, so no slot change yet and the card carries the whole travel.
        dragToRowTop(20);
        expect(names()).toEqual(['first', 'second', 'third']);
        expect(cardAt(0).style.transform).toBe('translateY(20px)');

        // Past a whole row: the slot absorbs the pitch and the transform carries only the overshoot.
        dragToRowTop(ROW_PITCH + 6);
        expect(names()).toEqual(['second', 'first', 'third']);
        expect(cardAt(1).style.transform).toBe('translateY(6px)');
    });

    it('pins the dragged card to the first slot when dragged above the list', () => {
        startDrag(cardAt(1));

        dragToRowTop(-ROW_PITCH * 10);

        expect(names()).toEqual(['second', 'first', 'third']);
        // Pinned rather than carried off: no leftover offset above the first slot.
        expect(cardAt(0).style.transform).toBe('translateY(0px)');
    });

    it('pins the dragged card to the last slot when dragged below the list', () => {
        startDrag(cardAt(1));

        dragToRowTop(ROW_PITCH * 10);

        expect(names()).toEqual(['first', 'third', 'second']);
        expect(cardAt(2).style.transform).toBe('translateY(0px)');
    });

    it('clears the dragged card transform once the drag ends', () => {
        startDrag(cardAt(0));
        dragToRowTop(20);
        expect(cardAt(0).style.transform).not.toBe('');

        window.dispatchEvent(new window.PointerEvent('pointerup', { bubbles: true }));

        expect(cardAt(0).style.transform).toBe('');
    });

    it('clears the dragged card transform when the drag is cancelled', () => {
        startDrag(cardAt(0));
        dragToRowTop(ROW_PITCH + 6);

        window.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));

        expect(names()).toEqual(['first', 'second', 'third']);
        expect(cardAt(0).style.transform).toBe('');
    });

    it('reports one change on drop rather than one per slot', () => {
        startDrag(cardAt(0));

        dragToRowTop(ROW_PITCH);
        dragToRowTop(ROW_PITCH * 2);
        expect(onChanged).not.toHaveBeenCalled();

        window.dispatchEvent(new window.PointerEvent('pointerup', { bubbles: true }));

        expect(onChanged).toHaveBeenCalledTimes(1);
        expect(names()).toEqual(['second', 'third', 'first']);
    });

    it('reports no change when the card is dropped back in its original slot', () => {
        startDrag(cardAt(0));

        dragToRowTop(ROW_PITCH * 2);
        dragToRowTop(0);
        window.dispatchEvent(new window.PointerEvent('pointerup', { bubbles: true }));

        expect(names()).toEqual(['first', 'second', 'third']);
        expect(onChanged).not.toHaveBeenCalled();
    });

    it('restores the original order when the drag is cancelled with Escape', () => {
        startDrag(cardAt(0));
        dragToRowTop(ROW_PITCH * 2);
        expect(names()).toEqual(['second', 'third', 'first']);

        window.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));

        expect(names()).toEqual(['first', 'second', 'third']);
        expect(onChanged).not.toHaveBeenCalled();
        expect(document.getElementById('cards').classList.contains('reordering')).toBe(false);
    });

    it('restores the original order when the browser cancels the pointer', () => {
        startDrag(cardAt(2));
        dragToRowTop(0);
        expect(names()).toEqual(['third', 'first', 'second']);

        window.dispatchEvent(new window.PointerEvent('pointercancel', { bubbles: true }));

        expect(names()).toEqual(['first', 'second', 'third']);
        expect(onChanged).not.toHaveBeenCalled();
    });

    it('stops tracking the pointer once the drag has ended', () => {
        startDrag(cardAt(0));
        window.dispatchEvent(new window.PointerEvent('pointerup', { bubbles: true }));

        dragToRowTop(ROW_PITCH * 2);

        expect(names()).toEqual(['first', 'second', 'third']);
        expect(document.getElementById('cards').classList.contains('reordering')).toBe(false);
    });

    it('refuses to start a drag while the document is read-only', () => {
        const readOnly = buildList({ isWritable: () => false });
        readOnly.list.populate([{ name: 'first' }, { name: 'second' }]);
        cardAt(0).open = true;

        startDrag(cardAt(0));
        dragToRowTop(ROW_PITCH);

        expect(names()).toEqual(['first', 'second']);
        expect(cardAt(0).open).toBe(true);
    });

    it('disables every control while the document is read-only', () => {
        const readOnly = buildList({ isWritable: () => false });
        readOnly.list.populate([{ name: 'first' }, { name: 'second' }]);

        expect(document.getElementById('add').disabled).toBe(true);
        expect(cardAt(0).querySelector('.card-name').disabled).toBe(true);
        expect(cardAt(0).querySelector('.cel-card-delete').disabled).toBe(true);
        expect(document.getElementById('cards').classList.contains('reorder-disabled')).toBe(true);
    });

    it('refuses a keyboard move while the document is read-only', () => {
        const readOnly = buildList({ isWritable: () => false });
        readOnly.list.populate([{ name: 'first' }, { name: 'second' }]);

        pressOnHeader(cardAt(1), 'ArrowUp');

        expect(names()).toEqual(['first', 'second']);
        expect(readOnly.onChanged).not.toHaveBeenCalled();
    });

    // Every drag test above builds its list without the option, so they all run against this default; the
    // assertion is repeated here so the default is stated somewhere rather than only implied.
    it('reorders by default, without the caller asking for it', () => {
        const { list } = buildList();
        list.populate([{ name: 'first' }, { name: 'second' }]);

        pressOnHeader(cardAt(1), 'ArrowUp');

        expect(names()).toEqual(['second', 'first']);
    });

    it('leaves a list that is not reorderable without reorder handling', () => {
        const plain = buildList({ reorderable: false });
        plain.list.populate([{ name: 'first' }, { name: 'second' }]);

        pressOnHeader(cardAt(1), 'ArrowUp');

        expect(names()).toEqual(['first', 'second']);
        expect(document.getElementById('cards').classList.contains('reorder-disabled')).toBe(false);
        expect(plain.list.read()).toEqual([{ name: 'first' }, { name: 'second' }]);
    });
});

describe('placementForPointer', () => {
    // Four rows of pitch 48 starting at y=100, grabbed 10px down from the row's top.
    const place = (pointerY) => placementForPointer({
        pointerY, grabOffset: 10, listTop: 100, rowPitch: 48, rowCount: 4,
    });

    it('keeps the row at its slot with no offset when the pointer has not moved', () => {
        expect(place(110)).toEqual({ slotIndex: 0, offset: 0 });
    });

    it('holds the slot and carries the travel as an offset, up to half a row', () => {
        expect(place(130)).toEqual({ slotIndex: 0, offset: 20 });
    });

    it('takes the next slot past half a row, leaving only the overshoot as an offset', () => {
        expect(place(158)).toEqual({ slotIndex: 1, offset: 0 });
        expect(place(164)).toEqual({ slotIndex: 1, offset: 6 });
    });

    it('pins to the first slot above the list rather than carrying the row off it', () => {
        expect(place(-1000)).toEqual({ slotIndex: 0, offset: 0 });
    });

    it('pins to the last slot below the list', () => {
        expect(place(1000)).toEqual({ slotIndex: 3, offset: 0 });
    });

    it('follows the list when the panel scrolls under the drag', () => {
        const scrolled = placementForPointer({
            pointerY: 110, grabOffset: 10, listTop: 60, rowPitch: 48, rowCount: 4,
        });

        expect(scrolled).toEqual({ slotIndex: 1, offset: -8 });
    });

    it('declines to place a list too small to measure, rather than dividing by zero', () => {
        expect(placementForPointer({ pointerY: 500, grabOffset: 0, listTop: 0, rowPitch: 0, rowCount: 4 }))
            .toBeNull();
    });
});
