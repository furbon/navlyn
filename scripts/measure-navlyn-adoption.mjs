// Optional local experiment; requires Node, .NET 10 and an authenticated Codex CLI.
// No external libraries, compulsory Navlyn use, live-model CI gate or automatic retries.
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import cp from 'node:child_process';
import readline from 'node:readline';
import { performance } from 'node:perf_hooks';
import { fileURLToPath } from 'node:url';

const script = fileURLToPath(import.meta.url);
const args = process.argv.slice(2);
// Transparent measurement proxy: never write diagnostics on protocol stdout.
if (args[0] === '--proxy') {
  const [, traceFile, executable, ...serverArgs] = args;
  const started = performance.now();
  const trace = fs.createWriteStream(traceFile);
  const server = cp.spawn(executable, serverArgs, { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'], env: { ...process.env, NAVLYN_PROFILE_TIMINGS: '1' } });
  process.stdin.pipe(server.stdin);
  server.stdout.pipe(process.stdout);
  server.stderr.on('data', buffer => {
    trace.write(JSON.stringify({ elapsedMs: performance.now() - started, stderr: buffer.toString() }) + '\n');
    process.stderr.write(buffer);
  });
  server.on('error', error => { trace.end(JSON.stringify({ error: error.message }) + '\n'); process.exitCode = 1; });
  server.on('close', code => { trace.end(); process.exitCode = code ?? 1; });
} else {
  function value(name, fallback) { const index = args.indexOf(name); return index < 0 ? fallback : args[index + 1]; }
  const repo = path.resolve(path.dirname(script), '..');
  const output = path.resolve(value('--output', path.join(repo, 'artifacts/adoption')));
  const client = value('--client', 'codex');
  const model = value('--model', 'gpt-5.5');
  const reasoning = value('--reasoning', 'medium');
  const cli = value('--cli', 'navlyn');
  const server = value('--server', 'navlyn-mcp');
  const serverDll = value('--server-dll', null);
  const cliDll = value('--cli-dll', null);
  const conditions = value('--conditions', 'ordinary,cli,mcp').split(',');
  const taskNames = value('--tasks', 'config,binary').split(',');
  const attempts = Number(value('--attempts', '1'));
  const routingSkill = args.includes('--routing-skill');
  const disabled = args.flatMap((arg, index) => arg === '--disable-server' ? [args[index + 1]] : []);
  if (!Number.isInteger(attempts) || attempts < 1 || attempts > 5) throw Error('attempts must be 1..5');
  if (conditions.some(condition => !['ordinary', 'cli', 'mcp'].includes(condition))) throw Error('Unknown condition');
  if (disabled.some(name => !/^[A-Za-z0-9_-]+$/.test(name))) throw Error('Invalid server key');
  fs.mkdirSync(output, { recursive: true });
  if (fs.existsSync(path.join(output, 'results.json'))) throw Error('Use a fresh output directory; never overwrite earlier attempts');
  const workspaceRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'navlyn-adoption-'));
  const source = `namespace Sample;
public interface IFormatter { string Format(string value); }
public sealed class Formatter : IFormatter {
 public string Format(string value) => "text:" + value;
 public string Format(int value) => "number:" + value;
}
public sealed class Client {
 public string Text(IFormatter formatter) => formatter.Format("hello");
 public string Number(Formatter formatter) => formatter.Format(42);
 public string Direct(Formatter formatter) => formatter.Format("world");
}
`;
  const consumer = `namespace Sample;
public static class Consumer {
 public static int Calculate(short value) => TrialDependency.Checksum.Normalize(value);
}
`;
  const producer = path.join(workspaceRoot, 'producer');
  fs.mkdirSync(producer);
  fs.writeFileSync(path.join(producer, 'TrialDependency.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><DebugType>none</DebugType><DebugSymbols>false</DebugSymbols></PropertyGroup></Project>');
  fs.writeFileSync(path.join(producer, 'Checksum.cs'), 'namespace TrialDependency; public static class Checksum { public static int Normalize(short value) { int magnitude = value < 0 ? -value : value; return checked(magnitude * 37 + 211); } public static long Normalize(long value) => value % 17 + 911; }');
  function checked(executable, commandArgs) { return cp.execFileSync(executable, commandArgs, { encoding: 'utf8', windowsHide: true, timeout: 120000 }); }
  checked('dotnet', ['build', path.join(producer, 'TrialDependency.csproj'), '-c', 'Release', '--verbosity', 'quiet']);
  const parse = text => JSON.parse(text.trim().replace(/^```(?:json)?\s*/, '').replace(/\s*```$/, ''));
  const tasks = {
    config: {
      prompt: 'Read the declared TargetFramework from Sample.csproj. Return only JSON {"targetFramework":"..."}.',
      oracle: text => parse(text).targetFramework === 'net10.0'
    },
    binary: {
      prompt: 'Inspect the actual local library assembly referenced by Consumer.Calculate. The call to Normalize is at Consumer.cs line 3, column 71. For the compiler-bound overload, identify its parameter type, multiplier, additive constant, and whether the arithmetic is checked. Use actual implementation evidence, not API-name guesses. Return only JSON {"parameterType":"...","multiplier":0,"offset":0,"checked":false}.',
      oracle: text => { const answer = parse(text); return ['System.Int16', 'short'].includes(answer.parameterType) && answer.multiplier === 37 && answer.offset === 211 && answer.checked === true; }
    },
    interface: {
      prompt: 'Identify the concrete method implementing IFormatter.Format and all source callers of its string implementation, separating interface dispatch and direct calls. Return only JSON {"parameterType":"System.String","interfaceCallers":[...],"directCallers":[...]}, using Client.Method names. Do not infer runtime execution.',
      oracle: text => { const answer = parse(text); return answer.parameterType === 'System.String' && JSON.stringify([...answer.interfaceCallers].sort()) === '["Client.Text"]' && JSON.stringify([...answer.directCallers].sort()) === '["Client.Direct"]'; }
    }
  };
  if (taskNames.some(name => !tasks[name])) throw Error('Unknown task');
  const results = [];
  const report = {
    schemaVersion: 'navlyn.adoption.v1', measuredAt: new Date().toISOString(), model, reasoning,
    clientVersion: checked(client, ['--version']).trim(), cliVersion: checked(cliDll ? 'dotnet' : cli, [...(cliDll ? [path.resolve(cliDll)] : []), '--version']).trim(),
    serverVersion: checked(serverDll ? 'dotnet' : server, [...(serverDll ? [path.resolve(serverDll)] : []), '--version']).trim(),
    commit: checked('git', ['-C', repo, 'rev-parse', 'HEAD']).trim(), dirty: checked('git', ['-C', repo, 'status', '--porcelain']).trim().length > 0,
    attempts, conditions, taskNames, forcedNavlyn: false, mandatorySkillRead: false, routingSkillAvailable: routingSkill,
    preparation: 'Isolated repositories restored before timing; producer source/PDB unavailable in consumer. Timing includes client/server startup and discovery. Baseline permits .NET tools.',
    workspaceRoot, results
  };
  const save = () => fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify(report, null, 2));
  save();
  async function run(taskName, condition, attempt) {
    const id = `${taskName}-${condition}-${attempt}`;
    const dir = path.join(workspaceRoot, id);
    fs.mkdirSync(path.join(dir, 'lib'), { recursive: true });
    fs.writeFileSync(path.join(dir, 'Code.cs'), '\uFEFF' + source.replace(/\n/g, '\r\n'));
    fs.writeFileSync(path.join(dir, 'Consumer.cs'), '\uFEFF' + consumer.replace(/\n/g, '\r\n'));
    fs.copyFileSync(path.join(producer, 'bin/Release/net10.0/TrialDependency.dll'), path.join(dir, 'lib/TrialDependency.dll'));
    fs.writeFileSync(path.join(dir, 'Sample.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><Reference Include="TrialDependency"><HintPath>lib/TrialDependency.dll</HintPath></Reference></ItemGroup></Project>');
    if (routingSkill && condition !== 'ordinary') fs.cpSync(path.join(repo, '.agents/skills/navlyn-semantic-routing'), path.join(dir, '.agents/skills/navlyn-semantic-routing'), { recursive: true });
    checked('git', ['init', '-q', dir]);
    checked('dotnet', ['restore', path.join(dir, 'Sample.csproj'), '--verbosity', 'quiet']);
    const answerFile = path.join(output, id + '.answer.txt');
    const command = ['exec', '--ephemeral', '--json', '--model', model, '--dangerously-bypass-approvals-and-sandbox', '--cd', dir, '--output-last-message', answerFile, '-c', `model_reasoning_effort="${reasoning}"`];
    for (const name of [...new Set([...disabled, 'navlyn'])]) command.push('-c', `mcp_servers.${name}.enabled=false`);
    // A disabled new MCP entry still needs a valid transport in the client config.
    command.push('-c', `mcp_servers.navlyn.command=${JSON.stringify(serverDll ? 'dotnet' : server)}`);
    command.push('-c', `mcp_servers.navlyn.args=${JSON.stringify(serverDll ? [path.resolve(serverDll)] : [])}`);
    let guidance = 'Use ordinary file/search/.NET tools. Do not use Navlyn CLI, MCP or skills.';
    if (condition === 'cli') {
      const invocation = cliDll ? `dotnet "${path.resolve(cliDll)}"` : cli;
      guidance = `Ordinary tools and the optional Navlyn CLI are available. Use ordinary reads/search for directly readable facts. For uncertain binding or referenced implementation, ${invocation} read --workspace Sample.csproj --file <file> --line <line> --column <column> --view body --external-source decompiled supplies bounded static evidence. Choose the shortest sufficient route; Navlyn use is optional.`;
    }
    if (condition === 'mcp') {
      const serverCommand = serverDll ? 'dotnet' : server;
      const serverArgs = [...(serverDll ? [path.resolve(serverDll)] : []), '--workspace', path.join(dir, 'Sample.csproj'), '--working-directory', dir];
      const proxyArgs = [script, '--proxy', path.join(output, id + '.server.jsonl'), serverCommand, ...serverArgs];
      command.push('-c', 'mcp_servers.navlyn.enabled=true', '-c', `mcp_servers.navlyn.command=${JSON.stringify(process.execPath)}`, '-c', `mcp_servers.navlyn.args=${JSON.stringify(proxyArgs)}`);
      guidance = 'Ordinary tools and focused Navlyn MCP tools are available. Choose the shortest sufficient route for the requested evidence; Navlyn use is optional. Use ordinary reads/search for directly readable facts. For Navlyn access in this condition use the configured MCP server, not Navlyn CLI.';
    }
    command.push('Work only in this workspace. Do not browse or read other repositories, including the sibling producer. ' + guidance + ' ' + tasks[taskName].prompt);
    const start = performance.now();
    const child = cp.spawn(client, command, { windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    const raw = fs.createWriteStream(path.join(output, id + '.jsonl'));
    const arrivals = fs.createWriteStream(path.join(output, id + '.events.jsonl'));
    const events = [];
    const startedItems = new Map();
    const durations = [];
    const intervals = [];
    let stderr = '', timedOut = false;
    child.stdout.on('data', buffer => raw.write(buffer));
    child.stderr.on('data', buffer => { stderr += buffer.toString(); });
    readline.createInterface({ input: child.stdout }).on('line', line => {
      const elapsedMs = +(performance.now() - start).toFixed(3);
      try {
        const event = JSON.parse(line);
        events.push(event);
        arrivals.write(JSON.stringify({ elapsedMs, event }) + '\n');
        if (event.type === 'item.started') startedItems.set(event.item.id, elapsedMs);
        if (event.type === 'item.completed' && startedItems.has(event.item.id)) {
          const from = startedItems.get(event.item.id);
          durations.push({ id: event.item.id, type: event.item.type, elapsedMs: +(elapsedMs - from).toFixed(3) });
          if (['command_execution', 'mcp_tool_call'].includes(event.item.type)) intervals.push([from, elapsedMs]);
        }
      } catch { arrivals.write(JSON.stringify({ elapsedMs, nonJson: line }) + '\n'); }
    });
    const timer = setTimeout(() => {
      timedOut = true;
      if (process.platform === 'win32') cp.spawn('taskkill', ['/PID', String(child.pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore' });
      else child.kill('SIGKILL');
    }, 180000);
    let spawnError;
    await new Promise(resolve => { child.once('error', error => { spawnError = error.message; resolve(); }); child.once('close', resolve); });
    clearTimeout(timer);
    await Promise.all([new Promise(resolve => raw.end(resolve)), new Promise(resolve => arrivals.end(resolve))]);
    fs.writeFileSync(path.join(output, id + '.stderr.txt'), stderr);
    const answer = fs.existsSync(answerFile) ? fs.readFileSync(answerFile, 'utf8') : '';
    let correct = false, oracleError;
    try { correct = tasks[taskName].oracle(answer); } catch (error) { oracleError = error.message; }
    const items = events.filter(event => event.type === 'item.completed').map(event => event.item);
    const tools = items.filter(item => ['command_execution', 'mcp_tool_call'].includes(item.type));
    let toolIntervalMs = 0, intervalEnd = 0;
    for (const [from, to] of intervals.sort((a, b) => a[0] - b[0])) {
      toolIntervalMs += Math.max(0, to - Math.max(from, intervalEnd));
      intervalEnd = Math.max(intervalEnd, to);
    }
    const result = { task: taskName, condition, attempt, elapsedMs: +(performance.now() - start).toFixed(3), exitCode: child.exitCode, timedOut, spawnError, correct, oracleError,
      calls: tools.length, mcpCalls: tools.filter(item => item.type === 'mcp_tool_call').length,
      cliCalls: tools.filter(item => item.type === 'command_execution' && /\bnavlyn(?:\.exe)?(?=["'\s]|$)|\b(?:navlyn|Navlyn\.CommandLine)\.dll\b/i.test(item.command ?? '')).length,
      skillReads: tools.filter(item => item.type === 'command_execution' && /navlyn-semantic-routing[\\/]+SKILL\.md/i.test(item.command ?? '')).length,
      fullOverrides: tools.filter(item => item.type === 'mcp_tool_call' && item.arguments?.resultProfile === 'full').length,
      usage: events.find(event => event.type === 'turn.completed')?.usage, toolDurations: durations,
      toolIntervalMs: +toolIntervalMs.toFixed(3),
      intervalMethod: 'Union of observed tool started/completed event arrival intervals; remaining time includes model, client, transport and gaps, not pure inference.',
      workspace: dir, answer };
    results.push(result); save();
    console.log(JSON.stringify({ ...result, answer: undefined, toolDurations: undefined, workspace: undefined }));
    if (spawnError || (child.exitCode !== 0 && !timedOut)) throw Error('Client failed; preserve evidence and stop without retries');
  }
  for (let attempt = 1; attempt <= attempts; attempt++) {
    for (let taskIndex = 0; taskIndex < taskNames.length; taskIndex++) {
      const offset = (attempt - 1 + taskIndex) % conditions.length;
      const order = [...conditions.slice(offset), ...conditions.slice(0, offset)];
      for (const condition of order) await run(taskNames[taskIndex], condition, attempt);
    }
  }
}
