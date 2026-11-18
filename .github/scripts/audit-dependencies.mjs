// Audits the npm packages in the current folder, and fails on an advisory of moderate severity or worse, as
// `npm audit --audit-level=moderate` does. npm audit can't ignore an advisory, so the script reads npm's JSON report
// and skips the withdrawn advisories below.

import { spawnSync } from 'node:child_process';

// Advisories that GitHub has withdrawn and npm's registry still reports. The script warns once npm stops reporting
// one, and the entry then goes.
const withdrawnAdvisories = new Set([
    'GHSA-fc5q-39xc-vjpf', // picomatch, withdrawn on 2026-11-18
]);

const failingSeverities = new Set(['moderate', 'high', 'critical']);

const audit = spawnSync('npm', ['audit', '--json'], {
    encoding: 'utf8',
    shell: process.platform === 'win32',
});
const report = JSON.parse(audit.stdout);
if (report.error) {
    console.error(`npm audit failed: ${report.error.summary}`);
    process.exit(1);
}

const advisories = new Map();
for (const vulnerability of Object.values(report.vulnerabilities ?? {})) {
    for (const cause of vulnerability.via) {
        // A cause given as a string names another vulnerable package, whose own entry holds the advisory.
        if (typeof cause === 'object') {
            const id = cause.url.split('/').pop();
            advisories.set(id, cause);
        }
    }
}

let failed = false;
for (const [id, advisory] of advisories) {
    const description = `${id} (${advisory.severity}): ${advisory.name} ${advisory.range}, ${advisory.title}`;
    if (withdrawnAdvisories.has(id)) {
        console.log(`Skipped a withdrawn advisory: ${description}`);
    } else if (failingSeverities.has(advisory.severity)) {
        console.log(description);
        failed = true;
    }
}

for (const id of withdrawnAdvisories) {
    if (!advisories.has(id)) {
        console.log(`::warning::npm no longer reports ${id}. Remove it from the withdrawn advisories in audit-dependencies.mjs.`);
    }
}

if (failed) {
    process.exit(1);
}
console.log(`No advisory of moderate severity or worse, in ${advisories.size} reported.`);
