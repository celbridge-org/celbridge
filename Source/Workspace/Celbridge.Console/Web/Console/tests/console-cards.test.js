// @vitest-environment jsdom

import { describe, it, expect, beforeEach, vi } from 'vitest';
import { createCardList } from '../console-cards.js';

// A minimal stand-in for the shortcut card template: the classes the card list drives, and one input.
const CARD_TEMPLATE = `
    <details class="cel-expander card">
        <summary>
            <span class="card-title"></span>
            <span class="card-actions">
                <button class="cel-icon-button card-move-up" type="button"></button>
                <button class="cel-icon-button card-move-down" type="button"></button>
                <button class="cel-icon-button card-delete" type="button"></button>
            </span>
        </summary>
        <div class="cel-expander-body">
            <input class="card-name" type="text">
        </div>
    </details>`;

function buildList(overrides = {}) {
    document.body.innerHTML = `
        <div id="cards" class="card-list"></div>
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
        reorderable: true,
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
            card.querySelector('.card-title').textContent = card.querySelector('.card-name').value;
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
        expect(cardAt(0).querySelector('.card-title').textContent).toBe('edited');
    });

    it('removes a card on delete and reports the change', () => {
        cardAt(1).querySelector('.card-delete').click();

        expect(names()).toEqual(['first', 'third']);
        expect(onChanged).toHaveBeenCalled();
    });

    it('moves a card up and down, reporting each change', () => {
        cardAt(2).querySelector('.card-move-up').click();
        expect(names()).toEqual(['first', 'third', 'second']);

        cardAt(0).querySelector('.card-move-down').click();
        expect(names()).toEqual(['third', 'first', 'second']);
        expect(onChanged).toHaveBeenCalledTimes(2);
    });

    it('disables the move buttons at each end of the list', () => {
        expect(cardAt(0).querySelector('.card-move-up').disabled).toBe(true);
        expect(cardAt(0).querySelector('.card-move-down').disabled).toBe(false);
        expect(cardAt(2).querySelector('.card-move-up').disabled).toBe(false);
        expect(cardAt(2).querySelector('.card-move-down').disabled).toBe(true);
    });

    it('re-evaluates the end cards after a move', () => {
        cardAt(0).querySelector('.card-move-down').click();

        expect(cardAt(0).querySelector('.card-move-up').disabled).toBe(true);
        expect(names()).toEqual(['second', 'first', 'third']);
    });

    it('keeps a card open and its edits intact across a move', () => {
        cardAt(0).open = true;
        cardAt(0).querySelector('.card-name').value = 'in progress';

        cardAt(0).querySelector('.card-move-down').click();

        expect(cardAt(1).open).toBe(true);
        expect(cardAt(1).querySelector('.card-name').value).toBe('in progress');
    });

    it('does not report a change when a move is a no-op', () => {
        cardAt(0).querySelector('.card-move-up').disabled = false;
        cardAt(0).querySelector('.card-move-up').click();

        expect(names()).toEqual(['first', 'second', 'third']);
        expect(onChanged).not.toHaveBeenCalled();
    });

    it('disables every control while the document is read-only', () => {
        const readOnly = buildList({ isWritable: () => false });
        readOnly.list.populate([{ name: 'first' }, { name: 'second' }]);

        expect(document.getElementById('add').disabled).toBe(true);
        expect(cardAt(0).querySelector('.card-name').disabled).toBe(true);
        expect(cardAt(0).querySelector('.card-delete').disabled).toBe(true);
        expect(cardAt(0).querySelector('.card-move-down').disabled).toBe(true);
    });

    it('leaves the move buttons alone for a list that is not reorderable', () => {
        const plain = buildList({ reorderable: false });
        plain.list.populate([{ name: 'first' }, { name: 'second' }]);

        // Untouched by refreshState beyond the blanket writable pass, which leaves them enabled.
        expect(cardAt(0).querySelector('.card-move-up').disabled).toBe(false);
        expect(plain.list.read()).toEqual([{ name: 'first' }, { name: 'second' }]);
    });
});
