// Makes marked's anchored block rules sticky. JavaScriptCore, the engine behind WebKit on macOS, tries some of
// them, such as hr and html, at every position of the text despite their leading ^. marked runs each rule once
// per block against the rest of the document, so without this, lexing time grows with the square of its length.

// A rule that matches only at the start of the text it is given, as a rule anchored with ^ does. Every
// test starts from position 0, since marked hands each rule a fresh string.
class StickyRule extends RegExp {
    exec(text) {
        this.lastIndex = 0;
        return super.exec(text);
    }
}

/**
 * Replaces every rule anchored with ^ in marked's block rule sets with one that is also sticky. Matches
 * are unchanged. marked builds its composite rules from these when its module loads, so the replacement
 * affects only the rules the lexer runs.
 * @param {Record<string, Record<string, unknown>>} blockRuleSets - marked's `Lexer.rules.block`.
 */
export function makeBlockRulesSticky(blockRuleSets) {
    for (const rules of Object.values(blockRuleSets)) {
        for (const [name, rule] of Object.entries(rules)) {
            if (rule instanceof RegExp
                && !rule.sticky
                && !rule.global
                && !rule.multiline
                && rule.source.startsWith('^')) {
                rules[name] = new StickyRule(rule.source, rule.flags + 'y');
            }
        }
    }
}
