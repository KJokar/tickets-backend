import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';

// Creates fixtures only in the dedicated QA API/CDN instances.
const apiBase = process.argv[2] || 'http://localhost:5081';
const cdnBase = process.argv[3] || 'http://localhost:5084';
assert.equal(new URL(apiBase).port, '5081');
assert.equal(new URL(cdnBase).port, '5084');
assert.ok([apiBase, cdnBase].every(value => ['localhost', '127.0.0.1'].includes(new URL(value).hostname)));
const endpoint = '/Tickets/v1/Tickets';
const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aZ0cAAAAASUVORK5CYII=', 'base64');
const pdf = Buffer.from('%PDF-1.7\nCDN test');
const audio = Buffer.from('RIFF0000WAVEfmt ');
async function api(path, account, method = 'GET', body) {
  const response = await fetch(apiBase + path, { method, headers: { 'Content-Type': 'application/json', ...(account ? { Authorization: 'Bearer ' + account.token } : {}) }, ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
  return { status: response.status, payload: await response.json().catch(() => null) };
}
async function register(role) {
  const mobile = '912' + String(Math.floor(Math.random()*1e7)).padStart(7,'0');
  const response = await api('/api/v1/auth/register', null, 'POST', { fullName: 'CDN Integration ' + role, countryId: 105, mobile, password: '1234', confirmPassword: '1234', role });
  assert.equal(response.status, 201); return { ...response.payload.data, mobile };
}
async function upload(account, files, category = 'MessageAttachment', ticketId) {
  const grant = await api('/Tickets/v1/Media/Uploads', account, 'POST', { category, ...(ticketId ? { ticketId } : {}) });
  assert.equal(grant.status, 200);
  const form = new FormData();
  for (const [name, bytes, type = 'image/png'] of files) form.append('files', new Blob([bytes], { type }), name);
  const response = await fetch(cdnBase + new URL(grant.payload.data.uploadUrl).pathname, { method: 'POST', headers: { Authorization: 'Bearer ' + grant.payload.data.token }, body: form });
  const data = await response.json();
  return { status: response.status, result: data.result, message: data.message, grant: grant.payload.data };
}
async function getAccess(account, ticket, message, file) {
  const response = await api(`${endpoint}/${ticket}/Messages/${message}/Attachments/${file}/Access`, account);
  assert.equal(response.status, 200);
  const url = new URL(response.payload.data.url);
  return cdnBase + url.pathname + url.search;
}

const client = await register('Client'), agent = await register('Agent'), other = await register('Agent');
for (const account of [client, agent]) {
  const avatar = await upload(account, [['avatar.png', png]], 'UserAvatar');
  assert.equal(avatar.status, 200);
  const saved = await api('/Tickets/v1/Profile/Avatar', account, 'PUT', { receipt: avatar.result[0].receipt });
  assert.equal(saved.status, 200);
  const path = saved.payload.data.avatarUrl;
  assert.ok(path.startsWith('/users/avatars/'));
  assert.deepEqual(Buffer.from(await (await fetch(cdnBase + path)).arrayBuffer()), png);
  const restored = await api('/api/v1/auth/login', null, 'POST', { countryId: 105, mobile: account.mobile, password: '1234' });
  assert.equal(restored.payload.data.user.avatarUrl, path);
  const stolen = await api('/Tickets/v1/Profile/Avatar', other, 'PUT', { receipt: avatar.result[0].receipt });
  assert.equal(stolen.status, 400);
}
const uploaded = await upload(client, [['image.png', png], ['guide.pdf', pdf, 'application/pdf'], ['voice.wav', audio, 'audio/wav']]);
assert.equal(uploaded.status, 200); assert.equal(uploaded.result.length, 3);
const [image, document, voice] = uploaded.result;
const created = await api(endpoint + '/Create', client, 'POST', { title: 'CDN integration request', description: 'First message with image', attachments: [{ receipt: image.receipt }] });
assert.equal(created.status, 201); const id = created.payload.data.id;
const history = (await api(`${endpoint}/${id}/Conversation`, client)).payload.data;
assert.equal(history.messages.length, 1);
const initial = history.messages[0];
assert.ok(initial.attachments[0].storageKey);
const ownerUrl = await getAccess(client, id, initial.id, image.id);
assert.deepEqual(Buffer.from(await (await fetch(ownerUrl)).arrayBuffer()), png);
const range = await fetch(ownerUrl, { headers: { Range: 'bytes=0-7' } });
assert.equal(range.status, 206); assert.deepEqual(Buffer.from(await range.arrayBuffer()), png.subarray(0, 8));
const oldQueueUrl = await getAccess(other, id, initial.id, image.id);
assert.equal((await fetch(oldQueueUrl)).status, 200);
const messageBody = { body: '', clientMessageId: randomUUID(), attachments: [{ receipt: document.receipt }, { receipt: voice.receipt }] };
const message = await api(`${endpoint}/${id}/Messages`, client, 'POST', messageBody);
assert.equal(message.status, 200); assert.equal(message.payload.data.body, '');
const retry = await api(`${endpoint}/${id}/Messages`, client, 'POST', messageBody);
assert.equal(retry.status, 200); assert.equal(retry.payload.data.id, message.payload.data.id);
const documentUrl = await getAccess(client, id, message.payload.data.id, document.id);
assert.deepEqual(Buffer.from(await (await fetch(documentUrl)).arrayBuffer()), pdf);
const voiceUrl = await getAccess(client, id, message.payload.data.id, voice.id);
assert.deepEqual(Buffer.from(await (await fetch(voiceUrl)).arrayBuffer()), audio);
assert.equal((await api(`${endpoint}/${id}/Accept`, agent, 'POST', { priority: 'Medium' })).status, 200);
assert.equal((await fetch(oldQueueUrl)).status, 404, 'Existing URL must lose access immediately after another agent accepts');
assert.equal((await fetch(await getAccess(agent, id, initial.id, image.id))).status, 200);
const answerFiles = await upload(agent, [['answer.png', png]], 'MessageAttachment', id);
assert.equal(answerFiles.status, 200);
const answer = await api(`${endpoint}/${id}/Messages`, agent, 'POST', { body: '', clientMessageId: randomUUID(), attachments: [{ receipt: answerFiles.result[0].receipt }] });
assert.equal(answer.status, 200);
const answerUrl = await getAccess(client, id, answer.payload.data.id, answerFiles.result[0].id);
assert.equal((await fetch(answerUrl)).status, 200);
assert.equal((await api(`${endpoint}/${id}/Messages/${answer.payload.data.id}`, agent, 'DELETE')).status, 200);
assert.equal((await fetch(answerUrl)).status, 404);
assert.equal((await fetch(cdnBase + '/messages/attachments/' + image.id)).status, 404);
assert.equal((await fetch(cdnBase + '/Storage/messages/attachments/' + image.id.replaceAll('-', ''))).status, 404);
assert.equal((await fetch(cdnBase + '/api/V1/Files/Content/' + image.id)).status, 401);
const invalid = await upload(client, [['fake.png', Buffer.from('<script>bad</script>')]]);
assert.equal(invalid.status, 400);
const fakeAvatar = await upload(client, [['video.mp4', Buffer.from('0000ftypisom')]], 'UserAvatar');
assert.equal(fakeAvatar.status, 400);
const form = new FormData(); form.append('files', new Blob([png]), 'image.png');
assert.equal((await fetch(cdnBase + '/api/V1/Files/MessageAttachment', { method: 'POST', body: form, headers: { Authorization: 'Bearer ' + client.token } })).status, 401);
assert.equal((await api(`/Tickets/v1/Media/Uploads`, other, 'POST', { category: 'MessageAttachment', ticketId: id })).status, 404);
assert.equal((await api(`${endpoint}/${id}/Status`, agent, 'PATCH', { status: 'Closed' })).status, 200);
assert.equal((await api(`/Tickets/v1/Media/Uploads`, client, 'POST', { category: 'MessageAttachment', ticketId: id })).status, 409);
assert.equal((await api('/Tickets/v1/Profile/Avatar', client, 'DELETE')).status, 200);
assert.equal((await api('/Tickets/v1/Profile', client)).payload.data.avatarUrl, undefined);
console.log(JSON.stringify({ ok: true, avatars: 'both roles, persisted through login', uploads: 'image/audio/PDF, direct to CDN', range: true, mediaOnlyMessages: true, idempotentRetry: true, immediatePermissionRevocation: true, deletedMessageRevocation: true, validation: true, privateStorage: true, ticketId: id }));
