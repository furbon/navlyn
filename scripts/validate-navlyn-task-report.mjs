// Checks evidence artifact integrity only. Semantic correctness needs independent review.
import fs from 'node:fs';
import path from 'node:path';
const report = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
if (!Array.isArray(report.findings) || !report.findings.length || !report.testPlan) throw Error('Missing findings/testPlan');
const root = process.cwd();
for (const finding of report.findings) {
  if (!finding.id || !finding.statement || !finding.certainty || !finding.evidence?.length) throw Error('Incomplete finding');
  for (const evidence of finding.evidence) {
    const file = path.resolve(root, evidence.path);
    if (!file.startsWith(root + path.sep) || !Number.isInteger(evidence.line) || evidence.line < 1) throw Error('Invalid evidence location');
    const lines = fs.readFileSync(file, 'utf8').split(/\r?\n/);
    if (evidence.line > lines.length) throw Error('Evidence line outside file');
  }
}
console.log('Evidence artifact valid; correctness is not inferred from this check.');
