import assert from 'node:assert/strict';
import { runInNewContext } from 'node:vm';

// Execute the actual initialization script served by Swagger, including its
// JSON parsing, without installing browser packages.
const baseUrl = process.argv[2] || 'http://localhost:5080';
const pageUrl = new URL('/swagger/index.html', baseUrl);
const page = await fetch(pageUrl, { signal: AbortSignal.timeout(10000) });
assert.equal(page.status, 200);
const html = await page.text();
const startupScript = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)]
  .map(match => match[1])
  .find(script => script.includes('window.onload'));
assert.ok(startupScript, 'Swagger must serve its initialization script');

let config;
const sandbox = {
  window: { location: { href: pageUrl.href } },
  URL,
  SwaggerUIBundle: Object.assign(options => {
    config = options;
    return { initOAuth() {} };
  }, { presets: { apis: {} } }),
  SwaggerUIStandalonePreset: {}
};

runInNewContext(startupScript, sandbox, { timeout: 5000 });
sandbox.window.onload();
assert.ok(config, 'Swagger must initialize without a JavaScript exception');
assert.equal(config.dom_id, '#swagger-ui');

assert.equal(config.requestInterceptor, undefined);

const schemaUrl = new URL(config.urls[0].url, pageUrl);
const schema = await fetch(schemaUrl, { signal: AbortSignal.timeout(10000) });
assert.equal(schema.status, 200);
const document = await schema.json();
assert.ok(document.paths['/api/v1/auth/register']);
assert.ok(document.paths['/api/v1/auth/login']);
assert.ok(document.paths['/Tickets/v1/Tickets/List']);
assert.ok(document.paths['/Tickets/v1/Tickets/{id}/Accept']);
assert.ok(document.paths['/Tickets/v1/Tickets/{id}/TransferAgents']);
assert.ok(document.paths['/Tickets/v1/Tickets/{id}/Transfer']);
assert.ok(document.paths['/Tickets/v1/Tickets/{id}/Release']);
assert.ok(document.paths['/Tickets/v1/Tickets/ReplyReminders']);
assert.ok(document.paths['/Tickets/v1/Tickets/{id}/ReplyReminder/Dismiss']);
assert.ok(document.paths['/Tickets/v1/Tickets/{id}/Conversation']);
assert.ok(document.paths['/Tickets/v1/Tickets/{id}/Messages']);
assert.ok(document.paths['/Tickets/v1/Tickets/{id}/Priority']);
assert.equal(document.paths['/Tickets/v1/Tickets/{id}/Replies'], undefined);
assert.deepEqual(Object.keys(document.components.schemas.CreateTicketRequest.properties).sort(), ['attachments', 'description', 'title']);
assert.ok(document.paths['/Tickets/v1/Media/Uploads']);
assert.ok(document.paths['/Tickets/v1/Profile']);
assert.ok(document.paths['/Tickets/v1/Profile/Avatar']);
assert.ok(document.paths['/Tickets/v1/Tickets/{id}/Messages/{messageId}/Attachments/{attachmentId}/Access']);
assert.equal(document.components.schemas.TicketDto.properties.department, undefined);
const authPaths = Object.keys(document.paths).filter(path => path.startsWith('/api/v1/auth/')).sort();
assert.deepEqual(authPaths, ['/api/v1/auth/forgot-password/request', '/api/v1/auth/forgot-password/reset', '/api/v1/auth/forgot-password/verify', '/api/v1/auth/login', '/api/v1/auth/refresh', '/api/v1/auth/register', '/api/v1/auth/roles']);
assert.equal(document.paths['/health'], undefined);
assert.equal(document.components.securitySchemes.Bearer.scheme, 'bearer');
assert.equal(document.components.securitySchemes.Session, undefined);
console.log(`Swagger initializes and documents ${Object.keys(document.paths).length} paths; Bearer authentication checks passed.`);
