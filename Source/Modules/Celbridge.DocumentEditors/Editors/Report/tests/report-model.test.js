import { describe, expect, it } from 'vitest';

import {
    Severity,
    groupItems,
    normalizeSeverity,
    parseReport,
    resolveActionPosition,
    resolveActions,
    severityRank
} from '../js/report-model.js';

function finding(code, message, overrides = {}) {
    return { code, message, severity: Severity.Warning, ...overrides };
}

describe('parseReport', () => {
    it('parses a report with sections', () => {
        const report = parseReport('{"id":"project-load","sections":[]}');

        expect(report.id).toBe('project-load');
        expect(report.sections).toEqual([]);
    });

    it('rejects content that is not a report', () => {
        // The editor binds an extension, so it can be handed a file that is valid JSON but holds
        // something else entirely.
        expect(() => parseReport('not json')).toThrow();
        expect(() => parseReport('[1, 2, 3]')).toThrow();
        expect(() => parseReport('{"id":"project-load"}')).toThrow();
    });
});

describe('groupItems', () => {
    it('collapses occurrences of one finding into a single group', () => {
        const items = [
            finding('CEL_RESOURCE_003', 'References a missing resource.', { resource: 'project:a.cel' }),
            finding('CEL_RESOURCE_003', 'References a missing resource.', { resource: 'project:b.cel' }),
            finding('CEL_RESOURCE_003', 'References a missing resource.', { resource: 'project:c.cel' })
        ];

        const groups = groupItems(items);

        expect(groups).toHaveLength(1);
        expect(groups[0].code).toBe('CEL_RESOURCE_003');
        expect(groups[0].items).toHaveLength(3);
    });

    it('keeps occurrences of one code apart when their messages differ', () => {
        // A descriptor that composes its message from arguments reads differently per occurrence, and
        // one group header cannot describe both.
        const items = [
            finding('CEL_PROJECT_005', 'Config entry skipped: alpha'),
            finding('CEL_PROJECT_005', 'Config entry skipped: beta')
        ];

        const groups = groupItems(items);

        expect(groups).toHaveLength(2);
        expect(groups.map(group => group.message))
            .toEqual(['Config entry skipped: alpha', 'Config entry skipped: beta']);
    });

    it('never groups items that carry no code', () => {
        const items = [
            { message: 'Resources', value: '412 files', severity: Severity.Info },
            { message: 'Resources', value: '37 folders', severity: Severity.Info }
        ];

        const groups = groupItems(items);

        expect(groups).toHaveLength(2);
        expect(groups.every(group => group.items.length === 1)).toBe(true);
    });

    it('gives a group the worst severity among its occurrences', () => {
        const items = [
            finding('CEL_PACKAGE_003', 'Editor degraded.', { severity: Severity.Warning }),
            finding('CEL_PACKAGE_003', 'Editor degraded.', { severity: Severity.Error })
        ];

        const groups = groupItems(items);

        expect(groups[0].severity).toBe(Severity.Error);
    });

    it('preserves the order the groups first appear in', () => {
        const items = [
            finding('CEL_RESOURCE_001', 'Orphan .cel file.'),
            finding('CEL_RESOURCE_003', 'References a missing resource.'),
            finding('CEL_RESOURCE_001', 'Orphan .cel file.')
        ];

        const groups = groupItems(items);

        expect(groups.map(group => group.code)).toEqual(['CEL_RESOURCE_001', 'CEL_RESOURCE_003']);
    });
});

describe('severity', () => {
    it('orders info below warning below error', () => {
        expect(severityRank(Severity.Info)).toBeLessThan(severityRank(Severity.Warning));
        expect(severityRank(Severity.Warning)).toBeLessThan(severityRank(Severity.Error));
    });

    it('renders a severity this build does not know as info', () => {
        // Reports persist on disk and can arrive from a newer producer, so an unknown value must not
        // stop the report rendering.
        expect(normalizeSeverity('catastrophe')).toBe(Severity.Info);
    });
});

describe('resolveActions', () => {
    it('keeps open-resource actions that name a resource', () => {
        const item = {
            actions: [
                { kind: 'openResource', label: 'Open a.cel', resource: 'project:a.cel' }
            ]
        };

        expect(resolveActions(item)).toHaveLength(1);
    });

    it('drops actions with no resource or an unknown kind', () => {
        // A report can be written into the project and arrive from outside, so an action naming work
        // this build does not offer is ignored rather than rendered as a dead control.
        const item = {
            actions: [
                { kind: 'openResource', label: 'Nowhere' },
                { kind: 'runCommand', label: 'Run', resource: 'project:a.cel' }
            ]
        };

        expect(resolveActions(item)).toEqual([]);
    });

    it('treats a missing actions list as no actions', () => {
        expect(resolveActions({})).toEqual([]);
    });
});

describe('resolveActionPosition', () => {
    it('reads a line and column', () => {
        const action = { location: { line: 42, column: 7 } };

        expect(resolveActionPosition(action)).toEqual({ line: 42, column: 7 });
    });

    it('opens at the top when the action carries no location', () => {
        expect(resolveActionPosition({})).toEqual({ line: 0, column: 0 });
    });

    it('ignores a column with no line to resolve it against', () => {
        const action = { location: { column: 7 } };

        expect(resolveActionPosition(action)).toEqual({ line: 0, column: 0 });
    });
});
