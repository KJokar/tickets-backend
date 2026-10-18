# Ticketing

The backend API for the ticketing system, implemented with ASP.NET Core,
Entity Framework Core, JWT authentication, and PostgreSQL. Its project layout follows the layered style
of the `pair` project. Requests enter a shared support queue; the first agent
to accept a request owns its private conversation with the client.

## Project layout

- `TicketingApi`: HTTP endpoints, authentication, Swagger, SignalR, and startup.
- `Service`: business rules, authentication, and ticket lifecycle.
- `Repository`: database queries and access boundaries.
- `Database`: EF Core entities, configurations, enums, and migrations.
- `ViewModels`: request and response contracts.
- `Infrastructure`: JWT and password hashing.
- `TicketingApi.Tests`: workflow, security-helper, and model tests.

The Persian client and support dashboards live in the separate Next.js project
[`../ticketing-frontend`](../ticketing-frontend/README.md). Run that project
separately; this API does not serve frontend pages or static assets.

Controllers, services, and repositories follow the conventions of `pair`:

- Controllers use typed action results, call services, and map the service's
  status code to the HTTP response.
- Services apply business rules, return `ApiResultDto<T>`, and commit changes
  through `IDatabaseUnitOfWork`.
- Repositories own EF queries and DTO projections. `GetAsync` returns a
  projection without tracking; `GetEntityAsync` returns a tracked entity for
  changes through `Add` and `Modify`.
- Interfaces live beside their implementations, namespaces use braces, and
  dependency registrations are grouped by feature.

## Run with Docker

The compose project creates a dedicated PostgreSQL 17 container, database, and
named volume. Its default host port is `5434`, so it does not collide with the
reference project's PostgreSQL port.

```powershell
Copy-Item .env.example .env
# Replace every placeholder value in .env before a non-development deployment.
docker compose up --build
```

The API runs at <http://localhost:5080>; open Swagger at
<http://localhost:5080/swagger> in Development. Run the separate frontend at
<http://localhost:3000> for the application UI.

Register test accounts with either the `Client` or `Agent` role. Role selection
is intentionally available during public registration in this test project.

## Authentication

Registration, login, and refresh return `data.token`, `data.expiresAt`, `data.user`,
`data.refreshToken`, and `data.refreshTokenExpiresAt`.
New JWTs expire 30 days after issuance (`JWT:ExpirationMinutes = 43200`).
Refresh tokens expire 90 days after issuance (`JWT:RefreshTokenExpirationDays = 90`).
The user contains a `role` and `permissions` array. Each permission is also
included as a signed `permission` claim in the JWT and enforced by API policies.
Send the JWT on protected requests in the `Authorization: Bearer <token>` header.
In Swagger, copy `data.token`, click **Authorize**, and paste the token.

| Method | Endpoint | Purpose |
| --- | --- | --- |
| GET | `/api/v1/auth/roles` | Get enum values and Persian labels for the registration dropdown; no login required |
| POST | `/api/v1/auth/register` | Register with the selected role and receive a JWT |
| POST | `/api/v1/auth/login` | Sign in using country, mobile and password and receive a JWT |
| GET | `/Common/v1/Countries/List` | Country names, flags and calling prefixes, matching the `pair` country catalogue |
| POST | `/api/v1/auth/forgot-password/request` | Send a six-digit SMS code |
| POST | `/api/v1/auth/forgot-password/verify` | Exchange the code for a single-use password reset token |
| POST | `/api/v1/auth/forgot-password/reset` | Set a new password with the reset token |
| POST | `/api/v1/auth/refresh` | Exchange a refresh token for a new JWT and refresh token; no Bearer token required |

Send `{ "refreshToken": "<the latest refresh token>" }` to refresh. Store both
new tokens from the response: each refresh token can be used only once, including
concurrent requests. Only SHA-256 hashes of refresh tokens are stored in the
database. Expired, consumed, unknown tokens and inactive accounts return 401.
Each successful refresh reloads the account's current role and permissions.

Role options are generated from `UserRole` and its display attributes:
`{ "id": 1, "value": "Client", "label": "کاربر عادی" }` and
`{ "id": 2, "value": "Agent", "label": "پشتیبان" }`. The registration dropdown
loads these options from the API and sends the selected `value` as `role`.

Password recovery at `/forgot-password` follows `pair` and `pair-web`: choose
country/mobile, request an SMS code, verify it, then enter and confirm a new
password. Codes and reset tokens each expire after five minutes. Codes allow
five incorrect attempts; successful verification consumes the code and grants
a single-use reset token. Sending a fresh code invalidates an earlier token.
Only hashes are stored. Updating the password also revokes existing refresh
tokens. Existing stateless access JWTs retain their original expiry.
Configure `SmsIr:ApiKey`, `SmsIr:Username`, `SmsIr:LineNumber` and `SmsIr:BaseUrl`
using user secrets or the equivalent environment variables. The sender and
recipient normalization match the reference project; HTTP client URL logging is
disabled because the provider requires credentials in its request URL.

`MobileIdentityAndPasswordRecovery` replaces email identity with country/mobile
and seeds the reference country catalogue. Clear legacy test users before
applying this migration; it does not invent phone numbers for email accounts.

Logout is handled by deleting both tokens on the client. There is no logout
endpoint; issued JWTs retain their claims and remain valid until expiry.
Frontend session storage and automatic token renewal are documented in
[`../ticketing-frontend/README.md`](../ticketing-frontend/README.md).

## Requests, acceptance, and private chat

Clients submit only a title and description. Department has been removed.
Priority starts unset and is selected by the agent when accepting the request.
The assigned agent can change it later.
Priority is internal support information: client API responses and live events
omit it, and the client dashboard has no priority column, badge, or filter.
Priority filtering is available only to agents; client attempts return 403.

1. A new request is `Open` and appears in every agent's shared queue.
2. An agent sends `POST /Tickets/v1/Tickets/{id}/Accept` with
   `{ "priority": "Low|Medium|High" }`.
3. Acceptance sets the assigned agent, acceptance time, priority, and status
   `InProgress` in one database commit. Optimistic concurrency checks on
   assignment and status ensure that simultaneous attempts have one winner;
   subsequent attempts return 409.
4. The client and assigned agent can read and send messages in their private
   conversation. Other agents cannot read its details, message it, or change
   its priority/status. They receive only a queue update containing the request ID.
5. Clients can read and send consecutive messages from the moment they submit
   the request, including while it is still unassigned. These messages keep the
   request `Open`. Agents can preview the full message history of unassigned
   requests and must accept once before sending replies. Acceptance preserves
   all earlier client messages and restricts access to the assigned agent. Closing prevents new messages. Senders can still change their own unread
   messages; seen messages remain immutable.

Clients can `PATCH /Tickets/v1/Tickets/{id}` with title only or
`DELETE /Tickets/v1/Tickets/{id}` until acceptance. Only the owner may do this.
The assigned agent can hand off an active conversation with
`POST /Tickets/v1/Tickets/{id}/Transfer` and `{ "agentId": 123 }`, or return it to
the shared queue with `POST /Tickets/v1/Tickets/{id}/Release`. Transfer assigns
an active, different agent and sets `InProgress`; release removes the assignee
and sets `Open`. Both preserve messages, attachments, priority, and the first
acceptance time. Returning to the queue does not restore customer title/deletion
rights for an established conversation. The customer can keep messaging.
`GET /Tickets/v1/Tickets/{id}/TransferAgents?search=...&page=1&perPage=10` lists
eligible agents by name or mobile number and returns both for disambiguation. Closed conversations
cannot be handed off. Concurrent handoffs compete on the ticket revision.
`TicketAccessChanged` invalidates the former agent's open chat; private updates
and attachment access follow the new assignment immediately. No schema migration
is required for handoffs.

Assigned agents receive reply reminders after reading a customer message while
the ticket is `InProgress`. `TicketingApi/Workers/TicketReplyReminderWorker.cs`
follows the reference TaskManager worker pattern: it is registered with
`AddHostedService<TicketReplyReminderWorker>()` inside the API, creates a fresh
dependency-injection scope per run, and delegates to `IReplyRemindersService`.
It checks durable `TicketReplyReminders` state immediately on startup and every
15 seconds afterward; the default reminder delay is 300 seconds. Startup,
shutdown, failures, and raised-reminder counts are logged. Stopping the API also
stops the worker; no separate Windows Service installation is required.
Repeated reads do not postpone the deadline. Dismissing an alert snoozes it for
another 300 seconds; otherwise it repeats until a reply, closure, release, or
transfer. A new assignee gets their own deadline after reading. The worker also
initializes existing accepted/read conversations after deployment. Atomic claims
prevent duplicate processing across worker instances.
`GET /Tickets/v1/Tickets/ReplyReminders` returns only the caller's active alerts.
`POST /Tickets/v1/Tickets/{id}/ReplyReminder/Dismiss` accepts `{ "notificationId": "..." }`.
`ReplyRemindersChanged` invalidates all tabs of that agent; reconnect/focus also
reloads alerts. Apply the `AddTicketReplyReminders` migration before running.

Senders can `PATCH /Tickets/v1/Tickets/{id}/Messages/{messageId}` with `{ "body": "..." }`
or delete that URL until another participant has read the message. Previewing agents
can acknowledge visible messages before acceptance. Only read messages are immutable; closure prevents sending new messages but
does not lock existing unread messages. Message deletion removes the body and preserves
the message UUID to prevent retries from recreating it. History excludes deleted
messages. Ticket revisions make read/edit/delete and acceptance races return 409.

| Role | Dashboard | Access |
| --- | --- | --- |
| `Client` | Overview, submit request, my requests, conversation before and after acceptance | Own requests and conversations |
| `Agent` | Overview, shared queue, my conversations, priority and status controls | Preview and accept unassigned requests; work only on personally accepted requests |

The agent dashboard counts only the shared queue and that agent's accepted
requests. `List?mineOnly=true` shows their accepted requests; `Unassigned` or
`List?unassignedOnly=true` shows the open queue. Lists support status, priority,
search, and pagination. Another client's request or another agent's accepted
request returns 404. Client attempts to accept a request or set priority/status
return 403. Removed `Replies` endpoints return 404.

Client search matches the request's subject and assigned agent's name in one
field, ignoring case and extra whitespace. All entered words must match, and
they can span the two fields (for example, a subject word plus an agent name).
Unassigned requests remain searchable by subject. Agent search continues to
match subjects and descriptions. Filters and pagination apply on the server.

| Method | Endpoint | Purpose |
| --- | --- | --- |
| POST | `/Tickets/v1/Tickets/Create` | Submit title and description |
| POST | `/Tickets/v1/Tickets/{id}/Accept` | Agent accepts and selects priority |
| GET | `/Tickets/v1/Tickets/{id}/Conversation` | Participants and persisted message history |
| POST | `/Tickets/v1/Tickets/{id}/Messages` | Send a message using HTTP, including when live transport is disconnected |
| PATCH | `/Tickets/v1/Tickets/{id}/Priority` | Assigned agent changes priority |
| PATCH | `/Tickets/v1/Tickets/{id}/Status` | Assigned agent closes the conversation with `{ "status": "Closed" }`; other changes return 409 |

### Live transport

SignalR connects at `/hubs/tickets` using JWT authentication. The separate Next.js
frontend uses the `@microsoft/signalr` npm package. `access_token` query tokens
are accepted only on the hub path. Connections close at JWT expiry and reconnect
using the current token. Reconnect reloads persisted history.

- `SendMessage(ticketId, { body, clientMessageId })` sends a chat message;
  `clientMessageId` is a nonempty UUID retained across retries.
- `MessageReceived` delivers the persisted `ChatMessageDto` only to the two participants.
- `TicketChanged` delivers the updated request only to its client and assigned agent.
- `QueueChanged` delivers only `{ ticketId }` to agents so their queue refreshes.
- `GetPresence(ticketId)` returns the authorized conversation peer's connection
  state. `PresenceChanged` invalidates that snapshot only for assigned peers.
  The Next.js chat shows green for an online peer and red for an offline peer;
  unknown state is gray, and an unassigned support team has no presence dot.
  Open tabs and devices are counted separately, so only the last disconnection
  marks a user offline. Network failures follow SignalR's disconnect timeout.
  The active chat also rechecks every 10 seconds and on focus/reconnect.

Presence currently uses the single API process's in-memory `IUserPresence`
registry. Multiple API instances require a shared presence implementation along
with a SignalR backplane; a backplane alone does not share the connection registry.

The server assigns user groups from authenticated claims. Clients cannot join
arbitrary conversations or select message recipients/authors. Both HTTP and hub
sends go through the same service permission checks and database commit. Messages
are stored before notification; unique message IDs prevent duplicate sends after
an interrupted acknowledgement. Offline users retrieve history when reconnecting.

`ExclusiveSupportChat` renames the old message table and preserves its rows.
Existing assigned requests receive an acceptance time; unassigned requests have
priority cleared. The department column is removed.

Registration body:

```json
{
  "fullName": "Example Client",
  "countryId": 105,
  "mobile": "9121234567",
  "password": "Client-Password-123!",
  "confirmPassword": "Client-Password-123!",
  "role": "Client"
}
```

`role` is required and accepts `Client` or `Agent`. Passwords must be nonempty
and at most 128 characters; confirmation must match. Mobile numbers are stored
in E.164 format and globally unique across roles and countries. National numbers,
Persian/Arabic digits and matching international prefixes normalize to the same
identity. Login takes `countryId`, `mobile` and `password`; the saved role controls access.

Invalid input returns 400, incorrect credentials return 401, duplicate mobile
returns 409, and excessive authentication attempts return 429. Suspended
accounts cannot log in. Expired tokens and tokens with invalid signatures are
rejected by protected endpoints.

## Run tests

```powershell
dotnet restore .\Ticketing.slnx
dotnet test .\Ticketing.slnx --no-restore
```

Authentication integration tests run the HTTP pipeline against an isolated
in-memory SQLite database; they do not require Docker or a running PostgreSQL
server.

With the API running, check Swagger's actual JavaScript initialization and
authentication endpoints using Node.js (no extra packages required):

```powershell
node .\scripts\check-swagger.mjs
```

Run frontend validation from `../ticketing-frontend`; see that project's README
for its test, lint, and build commands.

## Ticket lifecycle

```text
Open -- agent accepts + selects priority --> InProgress
InProgress -- agent sends message --> Answered
Answered -- client sends message --> InProgress
InProgress / Answered -- assigned agent closes chat --> Closed
```

Only acceptance takes a request out of the queue. Every successfully saved agent
message sets status to `Answered`; a new client message on an accepted request
sets it to `InProgress`. Client messages before acceptance keep it `Open`.
These changes share the message's database commit. Retrying an existing message
does not replay its old status. Agents have no status selector: only the assigned
agent can close a conversation using the close button inside its chat. API calls
attempting to manually set `Open`, `InProgress`, or `Answered` return 409.
Closed conversations reject new messages and priority/status changes. Sending a
client message never clears the assigned agent or requires another acceptance.

Sample requests are available in `TicketingApi/TicketingApi.http`.

### Media on new requests

The standalone file service is the sibling project `../TicketingCdn` on the
Desktop, following the `TaskManagerCdn` reference's controllers, storage service,
folder categories and response envelope. Run it on port 5083 alongside this API
on 5080 and the Next.js frontend on 3000. Shared `Media:SigningKey` configuration
is stored in each project's user secrets. See `../TicketingCdn/README.md` for
setup, storage paths and deployment configuration.

The frontend now uploads new-ticket files, chat attachments and recorded voice
directly to the file service. Both client and agent profile pages support avatar
upload/replacement/removal. New CDN attachments store signed, owner-bound file
references instead of file bytes in PostgreSQL. The nullable `Content` column
continues to support existing binary attachments and the legacy multipart API.
Apply `CdnMediaAndProfileAvatars` before running this version.

`POST /Tickets/v1/Media/Uploads` issues a scoped, single-use upload grant.
`GET /Tickets/v1/Profile`, `PUT /Tickets/v1/Profile/Avatar` and
`DELETE /Tickets/v1/Profile/Avatar` manage the signed-in user's own profile image.
The attachment `/Access` endpoint issues short-lived CDN URLs. Every CDN
attachment request rechecks current API permissions, including after exclusive
acceptance or message deletion. Only avatars are publicly served as static files.

`node scripts/check-cdn.mjs` verifies the real API/CDN integration using dedicated
QA instances on 5081 and 5084. API tests also cover receipt forgery, ownership,
expiry, media-only messages, UUID retries, both profile roles and URL revocation.

The new-request form accepts photos, video, audio and PDF attachments, with
previews and removal before submission. Up to five files can be attached, with
a combined limit of 20 MiB. The upload completes on the CDN first; its attachment
references and the first message are then saved in one database transaction.
JSON `POST /Tickets/v1/Tickets/Create` accepts `title`, `description` and optional
`attachments: [{ receipt: "..." }]`. The compatibility endpoint
`POST /Tickets/v1/Tickets/CreateWithMedia` still accepts multipart fields `title`,
`description` and repeated `files`; first-party frontends use the CDN flow.
Supported formats include JPEG, PNG, GIF, WebP, AVIF,
HEIC/HEIF, MP4, MOV, WebM, MP3, M4A, WAV, Ogg and PDF; the server derives the MIME
type from the file signature instead of trusting the submitted content type.

Conversation messages include attachment metadata only. Authorized clients use
`GET /Tickets/v1/Tickets/{ticketId}/Messages/{messageId}/Attachments/{attachmentId}/Access`
to obtain a scoped CDN URL. Playback and downloads use the CDN directly, with
HTTP Range support. The older binary endpoint still serves legacy bytes or
redirects CDN attachments. Queue agents can view attachments until acceptance; afterward only
the owner and assigned agent can retrieve them. Deleted messages no longer
expose their files, and deleting an unaccepted ticket cascades its attachment
data. Apply all migrations, including `CdnMediaAndProfileAvatars`, before running the updated API.


### Initial messages and reports

Creating a ticket saves its `description` input as the first `ChatMessage` in the
same transaction. Ticket responses no longer contain `description`; ticket edits
accept only `{ "title": "..." }`. The first message follows the same read, edit and
delete rules as all other messages. Existing descriptions are migrated into chat
without changing existing message IDs. Read acknowledgements target the specific
visible message (`messageId` in `MessagesRead`), so backfilled older messages do
not accidentally mark newer messages read.

`GET /Tickets/v1/Tickets/Reports?from=2026-10-01&to=2026-10-30` is agent-only.
It returns totals and daily creation counts for tickets accepted by the current
agent. The queue and other agents' accepted tickets are excluded regardless of
query parameters. Clients receive 403 and have no reports page or navigation.
Dates are inclusive Asia/Tehran dates, with a maximum range of 366 days.

`GET /Tickets/v1/Tickets/Reports/Tickets?from=2026-10-01&to=2026-10-30&page=1&perPage=20`
returns the same agent's accepted requests in that date range, with `id`,
`customerName`, and `title`, plus pagination metadata. Pages contain at most 100
rows and sort by creation date and ID descending. Date validation and access
scope are shared with the aggregate report.

The separate frontend's report page is `/support/reports`. Its Jalali calendar
displays Persian months, years and digits; API date values are converted internally. Reports
include bar/donut charts, totals, and a separate paginated list of customer names
and request titles. Titles link to the corresponding conversations. There is no
chart-data table or scope selector.
**دانلود PDF** downloads the Persian charts followed by request-list pages;
**پرینت** opens the browser print dialog. Both exports retrieve every page of the
filtered list, including rows beyond the currently displayed page. Both buttons
are together above the report. The PDF is
rendered locally with the current authorized report data; no report data is sent
to an external service.

PDF rendering, the Jalali calendar, and the `pdf-lib` dependency belong to the
separate frontend project.


Message `version` is a per-message concurrency token. Unrelated ticket activity
cannot reject an unread message edit. Read acknowledgements optionally send the
version actually displayed; acknowledging an older version cannot lock newly
edited text. Conversation sync uses `includeDeleted=true` to retrieve cleared
versioned deletion metadata. Clients hide deleted entries and merge updates by
version, preserving seen/deleted states even if notifications arrive out of order.
