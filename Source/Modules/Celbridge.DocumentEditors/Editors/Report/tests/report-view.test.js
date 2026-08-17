import { beforeEach, describe, expect, it, vi } from 'vitest';

import { Severity } from '../js/report-model.js';
import { renderReport, showParseError } from '../js/report-view.js';

// The page skeleton report-view.js renders into, matching index.html.
function installPage() {
    document.body.innerHTML = `
        <article id="report">
            <header id="report-header">
                <i id="report-severity" class="bi"></i>
                <h1 id="report-title"></h1>
            </header>
            <p id="report-summary"></p>
            <p id="report-generated"></p>
            <p id="report-truncated" hidden></p>
            <div id="report-sections"></div>
        </article>
        <div id="report-error" hidden></div>`;
}

function report(overrides = {}) {
    return {
        id: 'project-load',
        title: 'Project Load',
        generatedAt: '2026-08-16T14:32:11Z',
        severity: Severity.Warning,
        summary: '3 issues found.',
        sections: [],
        ...overrides
    };
}

function brokenReference(resource) {
    return {
        code: 'CEL_RESOURCE_003',
        severity: Severity.Warning,
        message: 'References a missing resource.',
        resource,
        target: 'project:missing.cel',
        actions: [
            {
                kind: 'openResource',
                label: `Open ${resource}`,
                resource,
                location: { line: 42, column: 7 }
            }
        ]
    };
}

beforeEach(() => {
    installPage();
});

describe('renderReport', () => {
    it('renders the report header', () => {
        renderReport(report(), () => { });

        expect(document.getElementById('report-title').textContent).toBe('Project Load');
        expect(document.getElementById('report-summary').textContent).toBe('3 issues found.');
        expect(document.getElementById('report-severity').className)
            .toContain('severity-warning');
        expect(document.getElementById('report-truncated').hidden).toBe(true);
    });

    it('reports what the producer left out', () => {
        renderReport(report({ truncated: { omitted: 12 } }), () => { });

        const truncated = document.getElementById('report-truncated');
        expect(truncated.hidden).toBe(false);
        expect(truncated.textContent).toContain('12');
    });

    it('renders a facts section as labelled readings', () => {
        const sections = [
            {
                title: 'Summary',
                kind: 'facts',
                severity: Severity.Info,
                items: [{ severity: Severity.Info, message: 'Resources', value: '412 files' }]
            }
        ];

        renderReport(report({ sections }), () => { });

        expect(document.querySelector('.fact-label').textContent).toBe('Resources');
        expect(document.querySelector('.fact-value').textContent).toBe('412 files');
    });

    it('renders a lone finding as a block rather than a one-row table', () => {
        const sections = [
            {
                title: 'Missing references',
                kind: 'findings',
                severity: Severity.Warning,
                items: [brokenReference('project:a.cel')]
            }
        ];

        renderReport(report({ sections }), () => { });

        expect(document.querySelectorAll('.finding-row')).toHaveLength(1);
        expect(document.querySelector('.finding-table')).toBeNull();
    });

    it('lines repeated occurrences up as a table under one heading', () => {
        const sections = [
            {
                title: 'Missing references',
                kind: 'findings',
                severity: Severity.Warning,
                items: [
                    brokenReference('project:a.cel'),
                    brokenReference('project:b.cel'),
                    brokenReference('project:c.cel')
                ]
            }
        ];

        renderReport(report({ sections }), () => { });

        const groups = document.querySelectorAll('.finding-group');
        expect(groups).toHaveLength(1);

        // The message is stated once in the heading, not repeated down the rows.
        expect(groups[0].querySelector('.group-message').textContent)
            .toBe('References a missing resource.');
        expect(groups[0].querySelector('.group-count').textContent).toContain('3');

        const rows = groups[0].querySelectorAll('.finding-table tbody tr');
        expect(rows).toHaveLength(3);
        expect([...rows[0].querySelectorAll('td')].map(cell => cell.textContent))
            .toEqual(['project:a.cel', 'project:missing.cel', '42']);
    });

    it('gives a table only the columns its rows fill', () => {
        // A column every row leaves blank costs width and says nothing.
        const sections = [
            {
                title: 'Sidecar files',
                kind: 'findings',
                severity: Severity.Warning,
                items: [
                    { code: 'CEL_RESOURCE_001', severity: Severity.Warning, message: 'Orphan .cel file.', resource: 'project:a.cel' },
                    { code: 'CEL_RESOURCE_001', severity: Severity.Warning, message: 'Orphan .cel file.', resource: 'project:b.cel' }
                ]
            }
        ];

        renderReport(report({ sections }), () => { });

        const headers = [...document.querySelectorAll('.finding-table th')].map(cell => cell.textContent);
        expect(headers).toEqual(['Resource']);
    });

    it('makes the resource the control, with no separate action button', () => {
        const onOpenResource = vi.fn();
        const sections = [
            {
                title: 'Missing references',
                kind: 'findings',
                severity: Severity.Warning,
                items: [brokenReference('project:a.cel'), brokenReference('project:b.cel')]
            }
        ];

        renderReport(report({ sections }), onOpenResource);

        expect(document.querySelector('.item-action')).toBeNull();

        const link = document.querySelector('.resource-link');
        expect(link.textContent).toBe('project:a.cel');
        link.click();

        expect(onOpenResource).toHaveBeenCalledWith('project:a.cel', 42, 7);
    });

    it('opens a lone finding resource at its recorded position', () => {
        const onOpenResource = vi.fn();
        const sections = [
            {
                title: 'Missing references',
                kind: 'findings',
                severity: Severity.Warning,
                items: [brokenReference('project:a.cel')]
            }
        ];

        renderReport(report({ sections }), onOpenResource);
        document.querySelector('.resource-link').click();

        expect(onOpenResource).toHaveBeenCalledWith('project:a.cel', 42, 7);
    });

    it('replaces earlier content on a re-render', () => {
        const sections = [
            {
                title: 'Missing references',
                kind: 'findings',
                severity: Severity.Warning,
                items: [brokenReference('project:a.cel')]
            }
        ];

        renderReport(report({ sections }), () => { });
        renderReport(report({ sections: [] }), () => { });

        expect(document.querySelectorAll('.report-section')).toHaveLength(0);
    });
});

describe('showParseError', () => {
    it('hides the report and shows the error', () => {
        renderReport(report(), () => { });
        showParseError();

        expect(document.getElementById('report').hidden).toBe(true);
        expect(document.getElementById('report-error').hidden).toBe(false);
    });
});
