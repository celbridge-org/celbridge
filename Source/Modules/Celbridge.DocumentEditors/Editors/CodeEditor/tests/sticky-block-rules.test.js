import { describe, it, expect } from 'vitest';
import { Lexer } from '../markdown-preview/lib/marked.esm.js';
import { makeBlockRulesSticky } from '../markdown-preview/sticky-block-rules.js';
import '../markdown-preview/preview-module.js';

describe('makeBlockRulesSticky', () => {
    it('makes a rule anchored with ^ sticky, keeping its flags', () => {
        const ruleSets = { normal: { html: /^<div>/i } };

        makeBlockRulesSticky(ruleSets);

        const rule = ruleSets.normal.html;
        expect(rule.sticky).toBe(true);
        expect(rule.ignoreCase).toBe(true);
        expect(rule.source).toBe('^<div>');
    });

    it('leaves a rule that is not anchored, global or multiline, and a value that is not a rule, as it was', () => {
        const unanchored = /---/;
        const global = /^---/g;
        const multiline = /^---/m;
        const ruleSets = { normal: { unanchored, global, multiline, name: 'hr' } };

        makeBlockRulesSticky(ruleSets);

        expect(ruleSets.normal).toEqual({ unanchored, global, multiline, name: 'hr' });
        expect(ruleSets.normal.unanchored).toBe(unanchored);
    });

    it('matches only at the start of each text it is given, as the anchored rule did', () => {
        const ruleSets = { normal: { hr: /^ {0,3}-{3,}(?:\n+|$)/ } };
        makeBlockRulesSticky(ruleSets);
        const rule = ruleSets.normal.hr;

        expect(rule.exec('----\nnext')[0]).toBe('----\n');
        // A match moves lastIndex on, and marked hands the rule a new, shorter text next time.
        expect(rule.exec('---')[0]).toBe('---');
        expect(rule.exec('text\n---')).toBeNull();
        expect(rule.test('---')).toBe(true);
    });

    it('is applied to marked\'s own block rules by the preview module', () => {
        expect(Lexer.rules.block.gfm.hr.sticky).toBe(true);
        expect(Lexer.rules.block.gfm.html.sticky).toBe(true);
    });
});
