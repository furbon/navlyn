// Actual stdio comparison with identical pages/oracles; Node/.NET only, no live model.
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import cp from 'node:child_process';
import readline from 'node:readline';
import assert from 'node:assert/strict';
import { performance } from 'node:perf_hooks';
const args = process.argv.slice(2);
const value = (name, fallback) => args.includes(name) ? args[args.indexOf(name) + 1] : fallback;
const baseline = value('--baseline', 'navlyn-mcp');
const serverDll = path.resolve(value('--server-dll', 'navlyn.Mcp/bin/Debug/net10.0/navlyn.Mcp.dll'));
const output = path.resolve(value('--output', 'artifacts/outline-comparison.json'));
const count = Number(value('--entries', '1200'));
if (!Number.isInteger(count) || count < 200 || count > 10000) throw Error('entries must be 200..10000');
if (fs.existsSync(output)) throw Error('Use a fresh report path');
fs.mkdirSync(path.dirname(output), { recursive: true });
const fixture = fs.mkdtempSync(path.join(os.tmpdir(), 'navlyn-outline-'));
fs.mkdirSync(path.join(fixture, '.git'));
fs.writeFileSync(path.join(fixture, 'Fixture.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>');
fs.writeFileSync(path.join(fixture, 'Code.cs'), '\uFEFFnamespace Fixture; public class Container {\r\n' + Array.from({ length: count }, (_, index) => `public int Method${String(index).padStart(4, '0')}(int value) => value + ${index};\r\n`).join('') + '}\r\n');
cp.execFileSync('dotnet', ['restore', path.join(fixture, 'Fixture.csproj'), '--verbosity', 'quiet'], { windowsHide: true, timeout: 120000 });
function memory(pid) {
  if (process.platform !== 'win32') return null;
  const measured = cp.execFileSync('pwsh', ['-NoProfile', '-Command', `Get-Process -Id ${pid} | Select-Object WorkingSet64,PeakWorkingSet64,PrivateMemorySize64 | ConvertTo-Json -Compress`], { encoding: 'utf8', windowsHide: true, timeout: 10000 });
  return JSON.parse(measured);
}
async function session(label, executable, prefix) {
  const started = performance.now();
  const child = cp.spawn(executable, [...prefix, '--workspace', path.join(fixture, 'Fixture.csproj'), '--working-directory', fixture], {
    windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'], env: { ...process.env, NAVLYN_PROFILE_TIMINGS: '1' }
  });
  const waiting = new Map();
  let nextId = 0, stderr = '';
  child.stderr.on('data', buffer => { stderr += buffer.toString(); });
  readline.createInterface({ input: child.stdout }).on('line', line => {
    try { const message = JSON.parse(line); waiting.get(message.id)?.(message); waiting.delete(message.id); }
    catch (error) { for (const resolve of waiting.values()) resolve({ error: { message: error.message } }); waiting.clear(); }
  });
  async function request(method, params) {
    const id = ++nextId, start = performance.now();
    const message = await new Promise((resolve, reject) => {
      const timer = setTimeout(() => { waiting.delete(id); reject(Error('stdio timeout')); }, 30000);
      waiting.set(id, message => { clearTimeout(timer); resolve(message); });
      child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n');
    });
    if (message.error) throw Error(JSON.stringify(message.error));
    return { elapsedMs: +(performance.now() - start).toFixed(3), result: message.result };
  }
  const records = [];
  let version, discoveryChars, startupMs;
  try {
    const initialize = await request('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'outline-comparison', version: '1' } });
    version = initialize.result.serverInfo.version;
    startupMs = +(performance.now() - started).toFixed(3);
    child.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');
    const discovery = await request('tools/list', {});
    discoveryChars = JSON.stringify(discovery.result).length;
    assert.equal(discovery.result.tools.length, 4);
    for (let round = 0; round < 4; round++) {
      const offset = round % 2 ? 100 : 0;
      const call = await request('tools/call', { name: 'navlyn_file_outline', arguments: { file: 'Code.cs', entryLimit: 100, entryOffset: offset } });
      const data = call.result.structuredContent;
      assert.equal(data.ok, true, JSON.stringify(data.error));
      assert.equal(data.result.entriesTotal, count + 2);
      assert.equal(data.result.entryOffset, offset);
      assert.equal(data.result.entries.length, 100);
      assert.equal(data.result.nextEntryOffset, offset + 100);
      const expected = Array.from({ length: 100 }, (_, index) => {
        const position = offset + index;
        return position === 0 ? 'Fixture' : position === 1 ? 'Container' : 'Method' + String(position - 2).padStart(4, '0');
      });
      assert.deepEqual(data.result.entries.map(entry => entry.name), expected);
      assert.deepEqual(JSON.parse(call.result.content[0].text), data);
      records.push({ round, phase: round === 0 ? 'cold' : 'warm', elapsedMs: call.elapsedMs, structuredChars: JSON.stringify(data).length, entries: data.result.entries, metadata: data.metadata,
        serverProcessMemory: memory(child.pid) }); // Sample outside the request interval; excludes MSBuild children and client/model memory.
    }
  } finally {
    child.stdin.end();
    await new Promise(resolve => { if (child.exitCode !== null) return resolve(); const timer = setTimeout(() => { child.kill(); resolve(); }, 3000); child.once('close', () => { clearTimeout(timer); resolve(); }); });
  }
  const timings = stderr.split(/\r?\n/).filter(line => line.startsWith('NAVLYN_MCP_TIMING ')).map(line => JSON.parse(line.slice('NAVLYN_MCP_TIMING '.length)));
  return { label, version, discoveryChars, startupMs, records, timings, stderr };
}
const report = { measuredAt: new Date().toISOString(), fixture, methodCount: count,
  environment: { platform: process.platform, node: process.version, sdk: cp.execFileSync('dotnet', ['--version'], { encoding: 'utf8', windowsHide: true }).trim() },
  limits: 'One synthetic workspace/session per version; memory is the server process only. Inclusive timings are not additive. Not an agent task or a statistical memory estimate.', sessions: [] };
try {
  report.sessions.push(await session('released-0.9.1', baseline, []));
  fs.writeFileSync(output, JSON.stringify(report, null, 2));
  report.sessions.push(await session('candidate', 'dotnet', [serverDll]));
  for (let round = 0; round < 4; round++) assert.deepEqual(report.sessions[0].records[round].entries, report.sessions[1].records[round].entries);
  report.identicalRequestedFacts = true;
} finally { fs.writeFileSync(output, JSON.stringify(report, null, 2)); }
console.log(JSON.stringify({ identicalRequestedFacts: report.identicalRequestedFacts, sessions: report.sessions.map(session => ({ label: session.label, version: session.version, discoveryChars: session.discoveryChars, startupMs: session.startupMs, records: session.records.map(({ entries, metadata, ...record }) => record) })) }, null, 2));
