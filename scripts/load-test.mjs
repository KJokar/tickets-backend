import assert from 'node:assert/strict';
import http from 'node:http';
import { randomUUID } from 'node:crypto';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { freemem, totalmem, cpus } from 'node:os';
import { mkdir, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { performance, monitorEventLoopDelay } from 'node:perf_hooks';

// Use only with a separately provisioned database and Release API on port 5082.
// There are no automatic retries, throttled workers, or small connection caps.
const base = new URL(process.argv[2] || 'http://127.0.0.1:5082');
assert.equal(process.env.TICKETING_LOAD_TEST_ISOLATED, '1', 'An isolated test instance is required.');
assert.equal(base.origin, 'http://127.0.0.1:5082', 'Only the dedicated local test port is allowed.');
const output = resolve(process.argv[3] || 'TestResults/load-test.json');
const apiPid = Number(process.argv[4]);
assert.ok(Number.isSafeInteger(apiPid) && apiPid > 0, 'Pass the dedicated API process ID.');
const run = promisify(execFile);
const root = '/Tickets/v1/Tickets';
const agent = new http.Agent({ keepAlive: true, maxSockets: 10_000, maxTotalSockets: 10_000, maxFreeSockets: 10_000, scheduling: 'fifo' });
const activeRequests = new Set();
const runId = randomUUID();
const report = {
  runId, startedAt: new Date().toISOString(), base: base.origin, configuration: 'Release',
  generator: { node: process.version, sameMachineAsApi: true, logicalProcessors: cpus().length, totalMemoryMiB: Math.round(totalmem() / 1048576) },
  settings: { offeredConcurrency: 10_000, retries: 0, deadlineMs: 120_000, accounts: 64, mediaBytes: 65_536,
    mediaPattern: 'A 1x1 PNG padded with zeroes; highly compressible in PostgreSQL.', minFreeMemoryMiB: 512 },
  phases: [], samples: [],
};
let currentPhase = 'setup';
let stopReason;
let sampling = false;
const jsonHeaders = { 'Content-Type': 'application/json' };
const json = value => Buffer.from(JSON.stringify(value));
const round = value => Math.round(value * 100) / 100;

function request(path, { token, body, headers = {}, expected = 200, validate } = {}, deadline = 120_000) {
  const start = performance.now();
  return new Promise(done => {
    let ended = false, timer;
    const finish = result => {
      if (ended) return;
      ended = true; clearTimeout(timer); activeRequests.delete(req);
      done({ ...result, latencyMs: performance.now() - start, reusedSocket: req.reusedSocket === true });
    };
    const req = http.request(new URL(path, base), {
      agent, method: body ? 'POST' : 'GET',
      headers: { Accept: 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...headers, ...(body ? { 'Content-Length': body.length } : {}) },
    }, response => {
      const chunks = []; let received = 0;
      response.on('data', chunk => { received += chunk.length; chunks.push(chunk); });
      response.on('error', error => finish({ ok: false, error: error.code || 'response-error' }));
      response.on('end', () => {
        let payload;
        try { payload = JSON.parse(Buffer.concat(chunks).toString('utf8')); } catch { /* Classified below. */ }
        const ok = response.statusCode === expected && (!validate || validate(payload));
        finish({ ok, status: response.statusCode, responseBytes: received,
          ...(ok ? {} : { error: response.statusCode === expected ? 'invalid-payload' : `HTTP-${response.statusCode}` }),
          ...(payload?.data?.id ? { id: payload.data.id } : {}),
          ...(path.startsWith('/api/') ? { payload } : {}) });
      });
    });
    activeRequests.add(req);
    req.on('error', error => finish({ ok: false, error: error.code || error.message || 'request-error' }));
    timer = setTimeout(() => req.destroy(Object.assign(new Error('request-deadline'), { code: 'DEADLINE' })), deadline);
    req.end(body);
  });
}

async function sample() {
  if (sampling) return;
  sampling = true;
  try {
    const freeMiB = freemem() / 1048576;
    const observation = { at: new Date().toISOString(), phase: currentPhase, clientActive: activeRequests.size,
      freeMemoryMiB: Math.round(freeMiB), generatorRssMiB: Math.round(process.memoryUsage().rss / 1048576) };
    const command = `$taskProcess = Get-Process -Id ${apiPid} -ErrorAction SilentlyContinue; if ($taskProcess) { [PSCustomObject]@{workingSetMiB=[math]::Round($taskProcess.WorkingSet64/1MB); privateMiB=[math]::Round($taskProcess.PrivateMemorySize64/1MB); cpuSeconds=$taskProcess.CPU; threads=$taskProcess.Threads.Count} | ConvertTo-Json -Compress }`;
    const { stdout } = await run('powershell.exe', ['-NoProfile', '-Command', command], { windowsHide: true, timeout: 10_000 });
    observation.api = stdout.trim() ? JSON.parse(stdout) : { exited: true };
    report.samples.push(observation);
    if (freeMiB < 512 && !stopReason) {
      stopReason = 'Free host memory below 512 MiB; canceled the load to protect the shared machine.';
      for (const req of activeRequests) req.destroy(Object.assign(new Error('memory-guard'), { code: 'MEMORY-GUARD' }));
    }
  } finally { sampling = false; }
}
async function probe(token) {
  const result = await request(`${root}/List?page=1&perPage=20`, {
    token, validate: value => Array.isArray(value?.data),
  }, 15_000);
  return { ok: result.ok, latencyMs: round(result.latencyMs), status: result.status, error: result.error };
}
async function burst(name, count, concurrency, makeRequest, token) {
  currentPhase = name;
  console.log(`Starting ${name}: ${count} requests, concurrency ${concurrency}`);
  const loop = monitorEventLoopDelay({ resolution: 20 }); loop.enable();
  const started = performance.now();
  let next = 0, completed = 0, peak = 0;
  const results = [];
  const progress = setInterval(() => console.log(`${name}: ${completed}/${count} completed, ${activeRequests.size} pending`), 5000);
  const worker = async () => {
    while (next < count && !stopReason) {
      const index = next++;
      const result = await makeRequest(index);
      results.push(result); completed++;
    }
  };
  // At concurrency=10,000 all requests are submitted before yielding for responses.
  const workers = Array.from({ length: Math.min(count, concurrency) }, worker);
  peak = activeRequests.size;
  const submissionWindowMs = performance.now() - started;
  await Promise.all(workers);
  clearInterval(progress); loop.disable();
  const durationMs = performance.now() - started;
  const latencies = results.map(item => item.latencyMs).sort((a, b) => a - b);
  const successes = results.filter(item => item.ok);
  const successTimes = successes.map(item => item.latencyMs).sort((a, b) => a - b);
  const percentile = (values, p) => values.length ? round(values[Math.ceil(values.length * p) - 1]) : null;
  const histogram = values => values.reduce((counts, key) => { counts[key] = (counts[key] || 0) + 1; return counts; }, {});
  const phase = { name, requests: count, submitted: results.length, offeredConcurrency: concurrency,
    peakClientPending: peak, submissionWindowMs: round(submissionWindowMs), durationSeconds: round(durationMs / 1000),
    succeeded: successes.length, failed: results.length - successes.length, notSubmitted: count - results.length,
    successPercent: round(successes.length / count * 100), completedPerSecond: round(results.length * 1000 / durationMs),
    successfulPerSecond: round(successes.length * 1000 / durationMs),
    statuses: histogram(results.filter(item => item.status).map(item => item.status)),
    errors: histogram(results.filter(item => !item.ok).map(item => item.error)),
    reusedSockets: results.filter(item => item.reusedSocket).length,
    latencyMs: { p50: percentile(latencies, .5), p95: percentile(latencies, .95), p99: percentile(latencies, .99), max: latencies.length ? round(latencies.at(-1)) : null },
    successfulLatencyMs: { p50: percentile(successTimes, .5), p95: percentile(successTimes, .95), p99: percentile(successTimes, .99) },
    generatorEventLoopDelayP99Ms: round(loop.percentile(99) / 1e6),
    successfulIds: successes.filter(item => item.id).map(item => item.id),
  };
  await sample();
  currentPhase = `${name}-recovery`;
  phase.recovery = await probe(token);
  report.phases.push(phase);
  report.stopReason = stopReason;
  await writeFile(output, JSON.stringify(report, null, 2));
  console.log(JSON.stringify({ ...phase, successfulIds: undefined }));
  if (!phase.recovery.ok) throw new Error(`${name}: API did not recover within the probe deadline.`);
}

await mkdir(dirname(output), { recursive: true });
const monitoring = setInterval(() => void sample().catch(error => console.log('Resource sample unavailable:', error.message)), 5000);
try {
  await sample();
  const accounts = [];
  for (let index = 0; index < report.settings.accounts; index++) {
    const result = await request('/api/v1/auth/register', {
      body: json({ fullName: `Load User ${index}`, countryId: 105, mobile: '9'+String((BigInt('0x'+runId.replaceAll('-','').slice(0,8))+BigInt(index))%1000000000n).padStart(9,'0'), password: '1234', confirmPassword: '1234', role: 'Client' }),
      headers: jsonHeaders, expected: 201, validate: value => typeof value?.data?.token === 'string',
    }, 15_000);
    assert.ok(result.ok, `Fixture registration failed: ${result.status || result.error}`);
    accounts.push(result.payload.data);
  }
  const token = index => accounts[index % accounts.length].token;
  const list = index => request(`${root}/List?page=1&perPage=20`, { token: token(index), validate: value => Array.isArray(value?.data) });
  const create = index => request(`${root}/Create`, { token: token(index), body: json({ title: `Load ${runId} ${index}`, description: 'Load test first message.' }), headers: jsonHeaders, expected: 201, validate: value => Number.isSafeInteger(value?.data?.id) });
  for (let index = 0; index < accounts.length * 2; index++) assert.ok((await create(index)).ok, 'Could not seed the test data.');
  report.seedTickets = accounts.length * 2;
  const image = Buffer.alloc(report.settings.mediaBytes);
  Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aZ0cAAAAASUVORK5CYII=', 'base64').copy(image);
  const boundary = `ticketing-load-${runId}`;
  const mediaBody = Buffer.concat([
    Buffer.from(`--${boundary}\r\nContent-Disposition: form-data; name="title"\r\n\r\nLoad media request\r\n--${boundary}\r\nContent-Disposition: form-data; name="description"\r\n\r\nLoad test first message with image.\r\n--${boundary}\r\nContent-Disposition: form-data; name="files"; filename="load.png"\r\nContent-Type: image/png\r\n\r\n`),
    image, Buffer.from(`\r\n--${boundary}--\r\n`),
  ]);
  const media = index => request(`${root}/CreateWithMedia`, { token: token(index), body: mediaBody,
    headers: { 'Content-Type': `multipart/form-data; boundary=${boundary}` }, expected: 201, validate: value => Number.isSafeInteger(value?.data?.id) });
  for (const [name, count, concurrency, workload] of [
    ['list-100', 100, 100, list], ['list-1000', 1000, 1000, list],
    ['list-10000', 10000, 10000, list], ['create-10000', 10000, 10000, create],
    ['media-64KiB-10000', 10000, 10000, media], ['populated-list-1000', 1000, 1000, list],
  ]) {
    if (stopReason) break;
    await burst(name, count, concurrency, workload, token(0));
  }
  report.finalRecovery = await probe(token(0));
} catch (error) {
  report.fatal = error.message;
  console.error('Load test stopped:', error.message);
  process.exitCode = 1;
} finally {
  clearInterval(monitoring);
  for (const req of activeRequests) req.destroy();
  agent.destroy();
  report.finishedAt = new Date().toISOString(); report.stopReason = stopReason;
  report.peakApiWorkingSetMiB = Math.max(0, ...report.samples.map(item => item.api?.workingSetMiB || 0));
  report.minimumFreeMemoryMiB = Math.min(...report.samples.map(item => item.freeMemoryMiB));
  await writeFile(output, JSON.stringify(report, null, 2));
  console.log(`Results written to ${output}`);
}
