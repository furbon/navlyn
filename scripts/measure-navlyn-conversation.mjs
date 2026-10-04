// Optional local real-task experiment. Node/.NET/authenticated Codex required; not a CI gate.
// One live app-server and one thread per session retain conversation and MCP workspace across prompts.
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import cp from 'node:child_process';
import readline from 'node:readline';
import { performance } from 'node:perf_hooks';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
const args = process.argv.slice(2);
const value = (key, fallback) => args.includes(key) ? args[args.indexOf(key) + 1] : fallback;
const task = JSON.parse(fs.readFileSync(value('--task'), 'utf8'));
const client = value('--client', 'codex');
const model = value('--model', 'gpt-5.5');
const output = path.resolve(value('--output', 'artifacts/real-task'));
const serverDll = path.resolve(value('--server-dll', 'navlyn.Mcp/bin/Release/net10.0/navlyn.Mcp.dll'));
const cliDll = path.resolve(value('--cli-dll', 'navlyn/bin/Release/net10.0/navlyn.dll'));
const source = path.resolve(value('--source'));
const conditions = value('--conditions', 'ordinary,mcp').split(',');
const workflows = value('--workflows', 'oneshot,conversation').split(',');
const attempts = Number(value('--attempts', '1'));
const probe = args.includes('--probe');
if (conditions.some(x => !['ordinary', 'cli', 'mcp'].includes(x)) || workflows.some(x => !['oneshot', 'conversation'].includes(x))) throw Error('Invalid condition/workflow');
if (!Number.isInteger(attempts) || attempts < 1 || attempts > 3 || task.prompts.length < 2 || task.prompts.length > 3) throw Error('Use 1..3 attempts and 2..3 prompts');
fs.mkdirSync(output, { recursive: true });
if (fs.existsSync(path.join(output, 'results.json'))) throw Error('Use a fresh output directory');
const checked = (exe, argv, options = {}) => cp.execFileSync(exe, argv, { encoding: 'utf8', windowsHide: true, timeout: 120000, maxBuffer: 8 * 1024 * 1024, ...options });
if (!/^[0-9a-f]{40}$/.test(task.sourceCommit)) throw Error('Use an exact source commit');
const sha = checked('git', ['-C', source, 'rev-parse', `${task.sourceCommit}^{commit}`]).trim();
if (sha !== task.sourceCommit) throw Error('Source snapshot does not match task');
const archive = path.join(output, 'source.zip');
checked('git', ['-C', source, 'archive', '--format=zip', '--output', archive, sha]);
const expandScript = path.join(output, 'expand.ps1');
fs.writeFileSync(expandScript, 'param([string]$Archive, [string]$Destination)\n$ErrorActionPreference = "Stop"\nExpand-Archive -LiteralPath $Archive -DestinationPath $Destination\n');
const submoduleArchives = [];
for (const relative of task.submodules ?? []) {
  const module = path.resolve(source, relative);
  if (!module.startsWith(source + path.sep)) throw Error('Invalid submodule path');
  const expected = checked('git', ['-C', source, 'rev-parse', `${sha}:${relative}`]).trim();
  if (checked('git', ['-C', module, 'rev-parse', 'HEAD']).trim() !== expected) throw Error('Initialize the pinned submodule before evaluation');
  const zip = path.join(output, `submodule-${submoduleArchives.length}.zip`);
  checked('git', ['-C', module, 'archive', '--format=zip', '--output', zip, expected]);
  submoduleArchives.push({ relative, sha: expected, zip });
}
const root = fs.mkdtempSync(path.join(os.tmpdir(), 'navlyn-real-task-'));
const proxy = path.resolve(path.dirname(fileURLToPath(import.meta.url)), 'measure-navlyn-adoption.mjs');
const disabled = args.flatMap((x, i) => x === '--disable-server' ? [args[i + 1]] : []);
const runnerRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const expandCommand = command => command.map(argument => argument.replaceAll('{runnerRoot}', runnerRoot));
if (disabled.some(x => !/^[a-zA-Z0-9_-]+$/.test(x))) throw Error('Invalid server name');
const report = { schemaVersion: 'navlyn.real-task.v1', task: task.id, sourceCommit: sha, sourceUrl: task.sourceUrl,
  measuredAt: new Date().toISOString(), model, clientVersion: checked(client, ['--version']).trim(),
  serverVersion: checked('dotnet', [serverDll, '--version']).trim(), conditions, workflows, attempts, probe,
  reasoningEffort: 'medium', candidateCommit: checked('git', ['-C', runnerRoot, 'rev-parse', 'HEAD']).trim(),
  candidateDirty: checked('git', ['-C', runnerRoot, 'status', '--porcelain']).trim().length > 0,
  serverSha256: createHash('sha256').update(fs.readFileSync(serverDll)).digest('hex'),
  routingSkillAvailable: true, taskDefinition: task,
  submodules: submoduleArchives.map(({ relative, sha }) => ({ path: relative, commit: sha })),
  method: 'Same final acceptance conditions. One-shot concatenates the same prompt requirements; conversation supplies them in 2..3 user turns on a persistent app-server/thread. No forced Navlyn use. Preparation/independent validation are reported outside task time.', results: [] };
const save = () => fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify(report, null, 2));
report.cliSha256 = createHash('sha256').update(fs.readFileSync(cliDll)).digest('hex');
report.engineAssemblies = ['Navlyn.Core.dll', 'Navlyn.CommandLine.dll'].map(name => ({ name,
  sha256: createHash('sha256').update(fs.readFileSync(path.join(path.dirname(serverDll), name))).digest('hex') }));
report.routingSkillFiles = ['SKILL.md', 'references/routing-matrix.md', 'references/evidence-boundaries.md'].map(name => ({ name,
  sha256: createHash('sha256').update(fs.readFileSync(path.join(runnerRoot, '.agents/skills/navlyn-semantic-routing', name))).digest('hex') }));
report.validationFiles = (task.validationFiles ?? []).map(file => ({ destination: file.destination,
  sha256: createHash('sha256').update(fs.readFileSync(path.resolve(path.dirname(value('--task')), file.source))).digest('hex') }));
save();
for (let attempt = 1; attempt <= attempts; attempt++) {
  const offset = (attempt - 1) % conditions.length;
  for (const condition of [...conditions.slice(offset), ...conditions.slice(0, offset)]) {
    const ordered = (attempt + conditions.indexOf(condition)) % 2 ? workflows : [...workflows].reverse();
    for (const workflow of ordered) {
      const id = `${condition}-${workflow}-${attempt}`, cwd = path.join(root, id);
      fs.mkdirSync(cwd);
      // Git created this archive; copy only the pinned tracked snapshot, not parent build outputs/history.
      checked('pwsh', ['-NoProfile', '-File', expandScript, archive, cwd]);
      for (const module of submoduleArchives) checked('pwsh', ['-NoProfile', '-File', expandScript, module.zip, path.join(cwd, module.relative)]);
      checked('git', ['init', '-q', cwd]);
      checked('git', ['-C', cwd, 'add', '-A']);
      checked('git', ['-C', cwd, '-c', 'user.name=Navlyn evaluation', '-c', 'user.email=eval@localhost', 'commit', '-qm', 'Pinned evaluation input']);
      if (condition !== 'ordinary') {
        fs.cpSync(path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../.agents/skills/navlyn-semantic-routing'),
          path.join(cwd, '.agents/skills/navlyn-semantic-routing'), { recursive: true });
        // Integration setup is not a task edit.
        checked('git', ['-C', cwd, 'add', '.agents']);
        checked('git', ['-C', cwd, '-c', 'user.name=Navlyn evaluation', '-c', 'user.email=eval@localhost', 'commit', '-qm', 'Optional routing skill']);
      }
      try {
        for (const definition of task.prepare ?? []) { const command = expandCommand(definition); checked(command[0], command.slice(1), { cwd }); }
      } catch (error) {
        report.results.push({ condition, workflow, attempt, workspace: cwd, preparationFailed: true,
          exitCode: error.status, stdout: error.stdout, stderr: error.stderr }); save(); throw error;
      }
      let guidance = 'Use ordinary read/search/edit/.NET tools. Do not use Navlyn CLI, MCP or skills.';
      const configArgs = [];
      for (const name of [...new Set([...disabled, 'navlyn'])]) configArgs.push('-c', `mcp_servers.${name}.enabled=false`,
        '-c', `mcp_servers.${name}.command="dotnet"`, '-c', `mcp_servers.${name}.args=[]`);
      configArgs.push('-c', 'mcp_servers.navlyn.command="dotnet"', '-c', `mcp_servers.navlyn.args=${JSON.stringify([serverDll])}`);
      if (condition === 'mcp') {
        guidance = 'Ordinary tools and optional focused Navlyn MCP are available. Use whichever supplies sufficient evidence efficiently; use the configured MCP rather than Navlyn CLI for Navlyn access.';
        configArgs.push('-c', 'mcp_servers.navlyn.enabled=true', '-c', `mcp_servers.navlyn.command=${JSON.stringify(process.execPath)}`,
          '-c', `mcp_servers.navlyn.args=${JSON.stringify([proxy, '--proxy', path.join(output, id + '.server.jsonl'), 'dotnet', serverDll, '--workspace', path.join(cwd, task.workspace), '--working-directory', cwd])}`);
      } else if (condition === 'cli') guidance = `Ordinary tools and optional Navlyn CLI are available: dotnet "${cliDll}" <command> --workspace "${task.workspace}". Use that built DLL for Navlyn access. Use ordinary text tools when enough; compiler facts are available when binding/context/relationship scope is uncertain.`;
      const started = performance.now();
      const child = cp.spawn(client, ['app-server', '--stdio', ...configArgs], { cwd, windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
      const trace = fs.createWriteStream(path.join(output, id + '.events.jsonl'));
      const pending = new Map(), waiters = [];
      const notifications = [];
      let nextId = 0, stderr = '', latestUsage = {}, fatal;
      child.stderr.on('data', b => { stderr += b.toString(); });
      child.once('error', error => { fatal = error.message; for (const p of pending.values()) p.reject(error); });
      let closed = false, stopping = false;
      const childClosed = new Promise(resolve => child.once('close', code => {
        closed = true;
        if (!stopping) {
          fatal = `App-server exited unexpectedly (${code})`;
          for (const p of pending.values()) p.reject(Error(fatal));
        }
        resolve();
      }));
      readline.createInterface({ input: child.stdout }).on('line', line => {
        let message;
        try { message = JSON.parse(line); } catch { trace.write(JSON.stringify({ elapsedMs: performance.now() - started, nonJson: line }) + '\n'); return; }
        trace.write(JSON.stringify({ elapsedMs: +(performance.now() - started).toFixed(3), message }) + '\n');
        if (message.id !== undefined && pending.has(message.id)) {
          const p = pending.get(message.id); pending.delete(message.id); message.error ? p.reject(Error(JSON.stringify(message.error))) : p.resolve(message.result);
        } else if (message.method && message.id !== undefined) {
          // The benchmark never approves actions outside its pre-authorized local task.
          fatal = 'Unexpected client request: ' + message.method;
          child.stdin.write(JSON.stringify({ id: message.id, error: { code: -32601, message: fatal } }) + '\n');
        } else {
          notifications.push(message);
          if (message.method === 'thread/tokenUsage/updated') latestUsage = message.params.tokenUsage.total;
          for (let i = waiters.length - 1; i >= 0; i--) if (waiters[i].predicate(message)) { waiters.splice(i, 1)[0].resolve(message); }
        }
      });
      const request = (method, params) => new Promise((resolve, reject) => {
        const id = ++nextId;
        const timer = setTimeout(() => { pending.delete(id); reject(Error(method + ' timed out')); }, 30000);
        pending.set(id, { resolve: x => { clearTimeout(timer); resolve(x); }, reject: e => { clearTimeout(timer); reject(e); } });
        child.stdin.write(JSON.stringify({ id, method, params }) + '\n');
      });
      const waitFor = predicate => new Promise((resolve, reject) => {
        const found = notifications.find(predicate); if (found) { resolve(found); return; }
        const waiter = { predicate, resolve: x => { clearTimeout(timer); resolve(x); } };
        const timer = setTimeout(() => { const i = waiters.indexOf(waiter); if (i >= 0) waiters.splice(i, 1); reject(Error('Turn timeout')); }, task.turnTimeoutMs ?? 240000);
        waiters.push(waiter);
      });
      const turns = [];
      let threadId, completedAt;
      try {
        await request('initialize', { clientInfo: { name: 'navlyn-real-task-evaluation', version: '1.0.0' }, capabilities: { experimentalApi: true } });
        child.stdin.write(JSON.stringify({ method: 'initialized', params: {} }) + '\n');
        const thread = await request('thread/start', { cwd, model, approvalPolicy: 'never', sandbox: 'danger-full-access', ephemeral: true });
        threadId = thread.thread.id;
        const prompts = workflow === 'oneshot' ? [task.prompts.join('\n\n')] : task.prompts;
        if (probe && condition === 'mcp') await waitFor(m => m.method === 'mcpServer/startupStatus/updated' && m.params.name === 'navlyn' && m.params.status !== 'starting');
        if (!probe) for (let index = 0; index < prompts.length; index++) {
          const start = performance.now(), beforeUsage = { ...latestUsage }, before = notifications.length;
          const prompt = (index === 0 ? `Work only in this workspace. Do not browse/read other repositories or delegate. ${guidance}\n\n` : '') + prompts[index];
          const turn = await request('turn/start', { threadId, model, effort: 'medium', input: [{ type: 'text', text: prompt }] });
          let ended, turnFailure;
          try { ended = await waitFor(m => m.method === 'turn/completed' && m.params.turn.id === turn.turn.id); }
          catch (error) { turnFailure = error; }
          completedAt = performance.now();
          const events = notifications.slice(before), items = events.filter(m => m.method === 'item/completed').map(m => m.params.item);
          const answer = items.filter(i => i.type === 'agentMessage').at(-1)?.text ?? '';
          const usage = Object.fromEntries(Object.entries(latestUsage).map(([key, n]) => [key, n - (beforeUsage[key] ?? 0)]));
          turns.push({ round: index + 1, turnId: turn.turn.id, status: ended?.params.turn.status ?? 'timed-out', error: ended?.params.turn.error ?? turnFailure?.message,
            elapsedMs: +(completedAt - start).toFixed(3), usage, answer,
            commandCalls: items.filter(i => i.type === 'commandExecution').length, mcpCalls: items.filter(i => i.type === 'mcpToolCall').length,
            fileChanges: items.filter(i => i.type === 'fileChange').length });
          console.log(JSON.stringify({ session: id, ...turns.at(-1), answer: undefined }));
          if (turnFailure) throw turnFailure;
          if (fatal || ended.params.turn.status !== 'completed') throw Error(fatal ?? 'Unsuccessful turn');
        }
      } catch (error) { fatal = error.message; }
      finally {
        stopping = true;
        child.stdin.end();
        if (!closed && child.pid) {
          if (process.platform === 'win32') cp.spawnSync('taskkill', ['/PID', String(child.pid), '/T', '/F'], { windowsHide: true, timeout: 10000 });
          else child.kill('SIGTERM');
        }
        await Promise.race([childClosed, new Promise(resolve => setTimeout(resolve, 2000))]);
        await new Promise(resolve => trace.end(resolve));
        fs.writeFileSync(path.join(output, id + '.stderr.txt'), stderr);
      }
      const status = checked('git', ['-C', cwd, 'status', '--porcelain']).trim();
      // Capture only the agent's edits before injecting independent acceptance checks.
      checked('git', ['-C', cwd, 'add', '--intent-to-add', '.']);
      const diffStat = checked('git', ['-C', cwd, 'diff', '--stat']).trim();
      fs.writeFileSync(path.join(output, id + '.diff'), checked('git', ['-C', cwd, 'diff']));
      const validation = [];
      if (!probe && !fatal) for (const file of task.validationFiles ?? []) {
        const destination = path.resolve(cwd, file.destination);
        if (!destination.startsWith(cwd + path.sep)) throw Error('Validation file outside workspace');
        fs.mkdirSync(path.dirname(destination), { recursive: true });
        fs.copyFileSync(path.resolve(path.dirname(value('--task')), file.source), destination);
      }
      if (!probe && !fatal) for (const definition of task.validate ?? []) {
        const command = expandCommand(definition);
        try { validation.push({ command, exitCode: 0, output: checked(command[0], command.slice(1), { cwd }) }); }
        catch (error) { validation.push({ command, exitCode: error.status, stdout: error.stdout, stderr: error.stderr }); }
      }
      const result = { condition, workflow, attempt, threadId, workspace: cwd, elapsedMs: +((completedAt ?? performance.now()) - started).toFixed(3),
        fatal, turns, totalUsage: latestUsage, routingSkillInstalledByRunner: condition !== 'ordinary', validation,
        validationPassed: !probe && !fatal && validation.length > 0 && validation.every(v => v.exitCode === 0),
        validationKind: task.validationKind ?? 'behavior', correctness: 'Requires independent review of acceptance scope and evidence',
        serverStartup: notifications.filter(m => m.method === 'mcpServer/startupStatus/updated' && m.params.name === 'navlyn').at(-1)?.params,
        status, diffStat };
      report.results.push(result); save();
      console.log(JSON.stringify({ ...result, turns: undefined, validation: undefined }));
      // A failed/expired session is evidence. Continue independent conditions; never retry it automatically.
    }
  }
}
