import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdir, mkdtemp, readFile, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { basename, dirname, join, resolve } from 'node:path';
import { randomUUID } from 'node:crypto';

// Run against an isolated development database: this creates test users and a ticket.
const baseUrl = process.argv[2] || 'http://localhost:5081';
const artifacts = resolve(process.argv[3] || join(tmpdir(), 'ticketing-dashboard-preview'));
await mkdir(artifacts, { recursive: true });
const profile = await mkdtemp(join(tmpdir(), 'ticketing-dashboard-browser-'));
const browserPath = process.env.EDGE_PATH || 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const browser = spawn(browserPath, [
  '--headless', '--disable-gpu', '--no-first-run', '--remote-debugging-port=0',
  `--user-data-dir=${profile}`, 'about:blank'
], { stdio: 'ignore', windowsHide: true });
const pause = ms => new Promise(done => setTimeout(done, ms));
let socket;
let send;

try {
  let port;
  for (let attempt = 0; attempt < 100; attempt++) {
    try { port = (await readFile(join(profile, 'DevToolsActivePort'), 'utf8')).split('\n')[0]; break; }
    catch { await pause(100); }
  }
  assert.ok(port, 'Headless browser must start');
  const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
  const target = targets.find(item => item.type === 'page');
  assert.ok(target);
  socket = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((done, reject) => {
    socket.addEventListener('open', done, { once: true });
    socket.addEventListener('error', reject, { once: true });
  });
  let sequence = 0;
  const pending = new Map();
  const exceptions = [];
  socket.addEventListener('message', event => {
    const message = JSON.parse(event.data);
    if (message.method === 'Runtime.exceptionThrown') exceptions.push(message.params.exceptionDetails.text);
    if (message.id && pending.has(message.id)) {
      const { done, reject, timer } = pending.get(message.id);
      pending.delete(message.id);
      clearTimeout(timer);
      if (message.error) reject(new Error(message.error.message));
      else done(message.result);
    }
  });
  send = (method, params = {}) => new Promise((done, reject) => {
    const id = ++sequence;
    const timer = setTimeout(() => { pending.delete(id); reject(new Error(`Browser command timed out: ${method}`)); }, 20000);
    pending.set(id, { done, reject, timer });
    socket.send(JSON.stringify({ id, method, params }));
  });
  const evaluate = async expression => {
    const result = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    assert.ok(!result.exceptionDetails, result.exceptionDetails?.exception?.description || 'Browser evaluation failed');
    return result.result.value;
  };
  const waitFor = expression => evaluate(`(async () => {
    const deadline = Date.now() + 12000;
    while (!(${expression})) {
      if (Date.now() > deadline) throw new Error(${JSON.stringify('Timed out waiting for: ' + expression)});
      await new Promise(done => setTimeout(done, 80));
    }
    return true;
  })()`);
  const click = selector => evaluate(`document.querySelector(${JSON.stringify(selector)}).click()`);
  const fill = (selector, value) => evaluate(`(() => {
    const field = document.querySelector(${JSON.stringify(selector)});
    field.value = ${JSON.stringify(value)};
    field.dispatchEvent(new Event('input', { bubbles: true }));
    field.dispatchEvent(new Event('change', { bubbles: true }));
  })()`);
  const submit = selector => evaluate(`document.querySelector(${JSON.stringify(selector)}).requestSubmit()`);
  const screenshot = async name => {
    const result = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
    await writeFile(join(artifacts, name), Buffer.from(result.data, 'base64'));
  };
  const request = async (path, token, method = 'GET', body) => {
    const response = await fetch(new URL(path, baseUrl), {
      method, headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
      ...(body === undefined ? {} : { body: JSON.stringify(body) })
    });
    return { status: response.status, body: await response.json().catch(() => null) };
  };
  await Promise.all([send('Runtime.enable'), send('Page.enable')]);
  await send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 1000, deviceScaleFactor: 1, mobile: false });
  await send('Page.navigate', { url: new URL('/', baseUrl).href });
  await waitFor("document.getElementById('auth-form') && document.readyState === 'complete'");
  await screenshot('01-login.png');
  const password = '1234';
  const clientMobile = '912' + String(Math.floor(Math.random()*1e7)).padStart(7,'0');
    async function register(mobile, role, name) {
    await click('#register-tab');
    await waitFor("!document.getElementById('role').disabled && document.getElementById('role').options.length > 1");
    await fill('#full-name', name);
    await fill('#mobile', mobile);
    await fill('#password', password);
    await fill('#confirm-password', password);
    await fill('#role', role);
    await submit('#auth-form');
    await waitFor("!document.getElementById('app-screen').hidden && document.getElementById('stat-total').textContent !== '—' && document.getElementById('list-loading').hidden");
    assert.equal(await evaluate("document.getElementById('auth-error').hidden"), true);
    return evaluate("JSON.parse(sessionStorage.getItem('ticketing.auth'))");
  }
  async function login(mobile) {
    await fill('#mobile', mobile);
    await fill('#password', password);
    await submit('#auth-form');
    await waitFor("!document.getElementById('app-screen').hidden && document.getElementById('list-loading').hidden");
  }
  const client = await register(clientMobile, 'Client', 'آیدا جوکار');
  await evaluate(`(() => {
    const auth = JSON.parse(sessionStorage.getItem('ticketing.auth'));
    auth.expiresAt = new Date(Date.now() - 60000).toISOString();
    sessionStorage.setItem('ticketing.auth', JSON.stringify(auth));
  })()`);
  await send('Page.navigate', { url: new URL('/?refresh-check=1', baseUrl).href });
  await waitFor("location.search === '?refresh-check=1' && document.readyState === 'complete' && document.getElementById('app-screen') && !document.getElementById('app-screen').hidden && document.getElementById('list-loading').hidden");
  const refreshedClient = await evaluate("JSON.parse(sessionStorage.getItem('ticketing.auth'))");
  assert.notEqual(refreshedClient.refreshToken, client.refreshToken);
  assert.equal((await request('/api/v1/auth/refresh', null, 'POST', { refreshToken: client.refreshToken })).status, 401);
  await evaluate(`(() => {
    const auth = JSON.parse(sessionStorage.getItem('ticketing.auth'));
    auth.token = 'invalid-access-token';
    sessionStorage.setItem('ticketing.auth', JSON.stringify(auth));
  })()`);
  await send('Page.navigate', { url: new URL('/?retry-check=1', baseUrl).href });
  await waitFor("location.search === '?retry-check=1' && document.readyState === 'complete' && document.getElementById('app-screen') && !document.getElementById('app-screen').hidden && document.getElementById('list-loading').hidden && document.getElementById('stat-total').textContent !== '—'");
  const retriedClient = await evaluate("JSON.parse(sessionStorage.getItem('ticketing.auth'))");
  assert.notEqual(retriedClient.refreshToken, refreshedClient.refreshToken);
  assert.notEqual(retriedClient.token, 'invalid-access-token');
  await waitFor("document.getElementById('connection-status').classList.contains('connected')");
  await evaluate(`(async () => {
    window.__qaClientTickets = [];
    window.__qaClientHub = new signalR.HubConnectionBuilder().withUrl('/hubs/tickets', { accessTokenFactory: () => JSON.parse(sessionStorage.getItem('ticketing.auth')).token }).configureLogging(signalR.LogLevel.None).build();
    __qaClientHub.on('TicketChanged', ticket => __qaClientTickets.push(ticket));
    await __qaClientHub.start();
  })()`);
  assert.equal(await evaluate("document.getElementById('filter-priority').hidden && document.getElementById('priority-column').hidden"), true);

  const childSockets = [];
  async function createPage() {
    const { targetId } = await send('Target.createTarget', { url: 'about:blank' });
    const pages = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
    const pageSocket = new WebSocket(pages.find(item => item.id === targetId).webSocketDebuggerUrl);
    childSockets.push(pageSocket);
    await new Promise((done, reject) => {
      pageSocket.addEventListener('open', done, { once: true });
      pageSocket.addEventListener('error', reject, { once: true });
    });
    let id = 0;
    const pending = new Map();
    pageSocket.addEventListener('message', event => {
      const message = JSON.parse(event.data);
      if (message.method === 'Runtime.exceptionThrown') exceptions.push(message.params.exceptionDetails.text);
      if (!pending.has(message.id)) return;
      const task = pending.get(message.id);
      pending.delete(message.id);
      clearTimeout(task.timer);
      if (message.error) task.reject(new Error(message.error.message));
      else task.done(message.result);
    });
    const command = (method, params = {}) => new Promise((done, reject) => {
      const commandId = ++id;
      const timer = setTimeout(() => { pending.delete(commandId); reject(new Error('Page command timed out: ' + method)); }, 20000);
      pending.set(commandId, { done, reject, timer });
      pageSocket.send(JSON.stringify({ id: commandId, method, params }));
    });
    const run = async expression => {
      const result = await command('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
      assert.ok(!result.exceptionDetails, result.exceptionDetails?.exception?.description || 'Page evaluation failed');
      return result.result.value;
    };
    const wait = expression => run(`(async () => {
      const deadline = Date.now() + 12000;
      while (!(${expression})) {
        if (Date.now() > deadline) throw new Error(${JSON.stringify('Timed out waiting for: ')} + ${JSON.stringify(expression)});
        await new Promise(done => setTimeout(done, 80));
      }
      return true;
    })()`);
    const click = selector => run(`document.querySelector(${JSON.stringify(selector)}).click()`);
    const fill = (selector, value) => run(`document.querySelector(${JSON.stringify(selector)}).value = ${JSON.stringify(value)}`);
    const submit = selector => run(`document.querySelector(${JSON.stringify(selector)}).requestSubmit()`);
    const screenshot = async name => {
      const result = await command('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
      await writeFile(join(artifacts, name), Buffer.from(result.data, 'base64'));
    };
    await Promise.all([command('Runtime.enable'), command('Page.enable')]);
    await command('Emulation.setDeviceMetricsOverride', { width: 1440, height: 1000, deviceScaleFactor: 1, mobile: false });
    await command('Page.navigate', { url: new URL('/', baseUrl).href });
    await wait("document.getElementById('auth-form') && document.readyState === 'complete'");
    return { command, run, wait, click, fill, submit, screenshot };
  }
  async function registerAgent(page, name) {
    await page.click('#register-tab');
    await page.wait("!document.getElementById('role').disabled");
    await page.fill('#full-name', name);
    await page.fill('#mobile', '912' + String(Math.floor(Math.random()*1e7)).padStart(7,'0'));
    await page.fill('#password', password);
    await page.fill('#confirm-password', password);
    await page.fill('#role', 'Agent');
    await page.submit('#auth-form');
    await page.wait("!document.getElementById('app-screen').hidden && document.getElementById('connection-status').classList.contains('connected') && document.getElementById('list-loading').hidden");
    return page.run("JSON.parse(sessionStorage.getItem('ticketing.auth'))");
  }
  const agentPage = await createPage();
  const otherPage = await createPage();
  const agent = await registerAgent(agentPage, 'پشتیبان اول');
  const otherAgent = await registerAgent(otherPage, 'پشتیبان دوم');
  await otherPage.run(`(async () => {
    window.__qaEvents = [];
    window.__qaHub = new signalR.HubConnectionBuilder().withUrl('/hubs/tickets', { accessTokenFactory: () => JSON.parse(sessionStorage.getItem('ticketing.auth')).token }).configureLogging(signalR.LogLevel.None).build();
    for (const event of ['MessageReceived', 'TicketChanged', 'QueueChanged']) __qaHub.on(event, data => __qaEvents.push({ event, data }));
    await __qaHub.start();
  })()`);
  assert.equal(await evaluate("document.getElementById('create-nav').hidden"), false);
  assert.equal(await evaluate("document.getElementById('pending-nav').hidden"), true);
  assert.equal(await evaluate("document.getElementById('ticket-department')"), null);
  assert.equal(await evaluate("document.getElementById('ticket-priority')"), null);
  const title = 'درخواست پشتیبانی ' + randomUUID().slice(0, 8);
  for (const page of [agentPage, otherPage]) {
    await page.click('#pending-nav');
    await page.fill('#search', title);
    await page.submit('#filters');
    await page.wait("document.getElementById('list-loading').hidden");
  }
  await click('#new-ticket');
  await fill('#ticket-title', title);
  await fill('#ticket-description', 'برای ورود به حساب کاربری به راهنمایی نیاز دارم.');
  await submit('#ticket-form');
  await waitFor("document.getElementById('ticket-dialog').open && !document.getElementById('waiting-note').hidden");
  assert.equal(await evaluate("document.getElementById('conversation-panel').hidden"), false);
  assert.equal(await evaluate("document.getElementById('chat-form').hidden"), false);
  assert.equal(await evaluate("document.getElementById('accept-form').hidden"), true);
  await waitFor("document.querySelector('#ticket-rows tr') && document.getElementById('list-loading').hidden");
  const ticketId = await evaluate("Number(document.querySelector('#ticket-rows tr').dataset.ticketId)");
  for (const text of ['سلام', 'یک توضیح دیگر دربارهٔ درخواست']) {
    await fill('#chat-body', text);
    await submit('#chat-form');
    await waitFor("document.getElementById('chat-body').value === '' && !document.getElementById('chat-body').disabled");
  }
  assert.equal(await evaluate("document.querySelectorAll('#chat-messages .chat-message').length"), 3);
  assert.equal((await request(`/Tickets/v1/Tickets/Select/${ticketId}`, retriedClient.token)).body.data.status, 'Open');
  assert.equal((await request(`/Tickets/v1/Tickets/${ticketId}/Messages`, agent.token, 'POST', { body: 'Before acceptance', clientMessageId: randomUUID() })).status, 409);
  await screenshot('02-client-waiting.png');
  for (const page of [agentPage, otherPage]) {
    await page.wait(`document.querySelector('[data-ticket-id="${ticketId}"]') && document.getElementById('list-loading').hidden`);
    await page.click(`[data-ticket-id="${ticketId}"] .ticket-title-button`);
    await page.wait("document.getElementById('ticket-dialog').open && !document.getElementById('accept-form').hidden");
    await page.wait("!document.getElementById('conversation-panel').hidden && document.querySelectorAll('#chat-messages .chat-message').length === 3");
    assert.equal(await page.run("document.getElementById('chat-form').hidden"), true);
  }
  await agentPage.fill('#accept-priority', 'High');
  await agentPage.submit('#accept-form');
  await agentPage.wait("!document.getElementById('conversation-panel').hidden && !document.getElementById('chat-form').hidden");
  await waitFor("!document.getElementById('conversation-panel').hidden && !document.getElementById('chat-form').hidden");
  await otherPage.wait("!document.getElementById('ticket-dialog').open");
  await otherPage.wait(`!document.querySelector('[data-ticket-id="${ticketId}"]') && document.getElementById('list-loading').hidden`);
  assert.equal(await evaluate("document.getElementById('priority-form').hidden"), true);
  assert.equal(await evaluate("document.querySelector('#detail-meta .priority, #detail-meta .priority-unset')"), null);
  assert.equal(await agentPage.run("document.getElementById('priority-form').hidden"), false);
  const attackResult = await otherPage.run(`__qaHub.invoke('SendMessage', ${ticketId}, { body: 'Intruder message', clientMessageId: crypto.randomUUID() })`);
  assert.equal(attackResult.statusCode, 404);
  assert.equal((await request(`/Tickets/v1/Tickets/${ticketId}/Conversation`, otherAgent.token)).status, 404);
  assert.equal((await request(`/Tickets/v1/Tickets/${ticketId}/Priority`, otherAgent.token, 'PATCH', { priority: 'Low' })).status, 404);

  const literal = '<img src=x onerror="window.__qaXss=true"> سلام پشتیبان';
  await fill('#chat-body', literal);
  await submit('#chat-form');
  await agentPage.wait("document.querySelectorAll('#chat-messages .chat-message').length === 4");
  assert.equal(await agentPage.run("document.querySelector('#chat-messages .chat-message:last-child p').textContent"), literal);
  assert.equal(await agentPage.run("Boolean(window.__qaXss) || !!document.querySelector('#chat-messages img')"), false);
  await agentPage.fill('#chat-body', 'سلام، درخواست شما را بررسی می‌کنم.');
  await agentPage.submit('#chat-form');
  await waitFor("document.querySelectorAll('#chat-messages .chat-message').length === 5");
  await agentPage.wait("document.querySelectorAll('#chat-messages .chat-message').length === 5");
  assert.equal(await otherPage.run(`__qaEvents.filter(item => item.data.ticketId === ${ticketId} && item.event !== 'QueueChanged').length`), 0);
  assert.equal(await otherPage.run("__qaEvents.filter(item => item.event === 'QueueChanged').every(item => Object.keys(item.data).length === 1 && 'ticketId' in item.data)"), true);
  await agentPage.screenshot('03-agent-private-chat.png');
  await screenshot('04-client-private-chat.png');

  await agentPage.fill('#agent-priority', 'Low');
  await agentPage.submit('#priority-form');
  await agentPage.wait("document.querySelector('#detail-meta .priority').classList.contains('Low')");
  await waitFor("__qaClientTickets.length >= 4");
  assert.equal(await evaluate("__qaClientTickets.every(ticket => !Object.hasOwn(ticket, 'priority'))"), true);
  assert.equal(await evaluate("document.querySelector('#detail-meta .priority, #detail-meta .priority-unset')"), null);
  const customerDetails = await request(`/Tickets/v1/Tickets/Select/${ticketId}`, retriedClient.token);
  assert.equal(Object.hasOwn(customerDetails.body.data, 'priority'), false);
  assert.equal((await request('/Tickets/v1/Tickets/List?priority=Low', retriedClient.token)).status, 403);
  assert.equal(await agentPage.run("document.getElementById('status-select')"), null);
  assert.equal(await evaluate("document.getElementById('close-form').hidden"), true);
  await waitFor("document.querySelector('#detail-meta .badge').classList.contains('status-Answered')");
  await fill('#chat-body', 'هنوز یک سؤال دیگر دارم.');
  await submit('#chat-form');
  await agentPage.wait("document.querySelectorAll('#chat-messages .chat-message').length === 6 && document.querySelector('#detail-meta .badge').classList.contains('status-InProgress')");

  // A message sent while the client is disconnected must survive in history.
  await send('Page.navigate', { url: 'about:blank' });
  await agentPage.fill('#chat-body', 'پیامی که هنگام قطع ارتباط ارسال شده است.');
  await agentPage.submit('#chat-form');
  await agentPage.wait("document.querySelectorAll('#chat-messages .chat-message').length === 7");
  await send('Page.navigate', { url: new URL('/?history-check=1', baseUrl).href });
  await waitFor("location.search === '?history-check=1' && document.readyState === 'complete' && document.getElementById('app-screen') && !document.getElementById('app-screen').hidden && document.getElementById('list-loading').hidden");
  await click(`[data-ticket-id="${ticketId}"] .ticket-title-button`);
  await waitFor("document.querySelectorAll('#chat-messages .chat-message').length === 7");
  await agentPage.submit('#close-form');
  await waitFor("!document.getElementById('closed-note').hidden && document.getElementById('chat-form').hidden");
  assert.equal((await request(`/Tickets/v1/Tickets/${ticketId}/Messages`, retriedClient.token, 'POST', { body: 'Closed', clientMessageId: randomUUID() })).status, 409);
  await click('#close-dialog');
  await send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true });
  await send('Page.navigate', { url: new URL('/?mobile-check=1', baseUrl).href });
  await waitFor("location.search === '?mobile-check=1' && document.readyState === 'complete' && document.getElementById('app-screen') && !document.getElementById('app-screen').hidden && document.getElementById('list-loading').hidden");
  const dimensions = await evaluate('({ width: window.innerWidth, contentWidth: document.documentElement.scrollWidth })');
  assert.equal(dimensions.width, 390);
  assert.ok(dimensions.contentWidth <= 390, 'Mobile layout must not overflow');
  await screenshot('05-mobile-dashboard.png');

  const raceTicket = await request('/Tickets/v1/Tickets/Create', retriedClient.token, 'POST', { title: 'Concurrent acceptance QA', description: 'Two agents competing for one request' });
  assert.equal(raceTicket.status, 201);
  const raceId = raceTicket.body.data.id;
  const race = await Promise.all([agent, otherAgent].map(user => request(`/Tickets/v1/Tickets/${raceId}/Accept`, user.token, 'POST', { priority: 'Medium' })));
  assert.deepEqual(race.map(result => result.status).sort(), [200, 409]);
  const outsider = await request('/api/v1/auth/register', null, 'POST', { fullName: 'Other Client', countryId: 105, mobile: '912' + String(Math.floor(Math.random()*1e7)).padStart(7,'0'), password, confirmPassword: password, role: 'Client' });
  assert.equal(outsider.status, 201);
  assert.equal((await request(`/Tickets/v1/Tickets/${ticketId}/Conversation`, outsider.body.data.token)).status, 404);
  assert.equal((await request('/Tickets/v1/Tickets/Create', agent.token, 'POST', { title: 'Forbidden', description: 'Agent cannot submit requests' })).status, 403);
  assert.deepEqual(exceptions, [], 'The browser must not report JavaScript exceptions');
  for (const child of childSockets) child.close();
  console.log('Browser checks passed: consecutive client messages before and after acceptance, live agent queue, exclusive acceptance, private SignalR chat, agent priority, closed chat, offline history, token renewal, mobile layout, and PostgreSQL acceptance race.');
  console.log(`Screenshots: ${artifacts}`);
} finally {
  if (send && socket?.readyState === WebSocket.OPEN) await send('Browser.close').catch(() => {});
  socket?.close();
  browser.kill();
  // Delete only the unique browser profile created by this script in the temp directory.
  if (dirname(resolve(profile)) === resolve(tmpdir()) && basename(profile).startsWith('ticketing-dashboard-browser-')) {
    await rm(profile, { recursive: true, force: true, maxRetries: 5, retryDelay: 100 }).catch(() => {});
  }
}
