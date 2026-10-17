'use strict';

(() => {
  const $ = id => document.getElementById(id);
  const storageKey = 'ticketing.auth';
  const base = '/Tickets/v1/Tickets';
  const permissions = { create: 'tickets.create', queue: 'tickets.queue.read', accept: 'tickets.accept', chat: 'tickets.chat', priority: 'tickets.priority.change', status: 'tickets.status.change' };
  const statuses = { Open: 'باز', InProgress: 'در جریان', Answered: 'پاسخ داده شده', Closed: 'بسته شده' };
  const priorities = { Low: 'کم', Medium: 'متوسط', High: 'زیاد' };
  const number = value => new Intl.NumberFormat('fa-IR').format(value);
  const state = { auth: null, mode: 'login', view: 'overview', page: 1, perPage: 6, ticket: null, listVersion: 0, expiryTimer: null, toastTimer: null };
  let roleOptionsRequest = null;
  let roleOptionsLoaded = false;
  let refreshRequest = null;
  let connection = null;
  let connectionAuth = null;
  let reconnectTimer = null;
  let liveRefreshTimer = null;
  let messages = [];
  let pendingMessage = null;
  let currentReport = null;
  let mediaUrls = [];
  const can = permission => state.auth?.user.permissions.includes(permission) === true;
  const isAgent = () => state.auth?.user.role === 'Agent';

  function element(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
  }

  function icon(name) {
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    const use = document.createElementNS('http://www.w3.org/2000/svg', 'use');
    use.setAttribute('href', '#icon-' + name);
    svg.append(use);
    svg.setAttribute('aria-hidden', 'true');
    return svg;
  }

  function date(value, withTime = false) {
    const parsed = new Date(value);
    if (Number.isNaN(parsed.getTime())) return '—';
    return new Intl.DateTimeFormat('fa-IR', {
      dateStyle: 'medium', ...(withTime ? { timeStyle: 'short' } : {}), timeZone: 'Asia/Tehran'
    }).format(parsed);
  }

  function showError(id, message = '') {
    $(id).textContent = message;
    $(id).hidden = !message;
  }

  function toast(message, error = false) {
    clearTimeout(state.toastTimer);
    $('toast').textContent = message;
    $('toast').classList.toggle('error', error);
    $('toast').hidden = false;
    state.toastTimer = setTimeout(() => { $('toast').hidden = true; }, 5000);
  }

  function busy(button, value) {
    button.disabled = value;
    button.setAttribute('aria-busy', String(value));
  }

  function errorMessage(message, status) {
    const messages = {
      'Full name must contain at least two non-padding characters.': 'نام و نام خانوادگی را کامل وارد کن.',
      'A closed ticket cannot receive new replies.': 'این تیکت بسته شده و امکان ارسال پاسخ ندارد.',
      'Ticket was not found.': 'این تیکت پیدا نشد یا به آن دسترسی نداری.',
      'This request has already been accepted or closed.': 'این درخواست قبلاً توسط پشتیبان دیگری پذیرفته یا بسته شده است.',
      'Chat starts after an agent accepts the request.': 'گفت‌وگو پس از پذیرش پشتیبان شروع می‌شود.',
      'This conversation is closed.': 'این گفتگو بسته شده است.',
      'The ticket changed concurrently. Reload it and retry.': 'این تیکت هم‌زمان تغییر کرده است. آن را دوباره باز کن.'
    };
    if (messages[message]) return messages[message];
    if (status === 401) return 'برای ادامه، دوباره وارد حسابت شو.';
    if (status === 403) return 'حساب شما به این بخش دسترسی ندارد.';
    if (status === 429) return 'تعداد تلاش‌ها زیاد شده؛ کمی بعد دوباره امتحان کن.';
    if (status === 400) return 'اطلاعات واردشده معتبر نیست. فیلدهای فرم را بررسی کن.';
    if (status === 409) return 'این تغییر در وضعیت فعلی تیکت امکان‌پذیر نیست.';
    return 'دریافت اطلاعات انجام نشد. کمی بعد دوباره تلاش کن.';
  }

  async function refreshAuth() {
    const auth = state.auth;
    if (!auth?.refreshToken) { signOut(); throw new Error(errorMessage('', 401)); }
    if (refreshRequest?.auth === auth) return refreshRequest.promise;
    const request = { auth, promise: null };
    request.promise = (async () => {
      let response;
      try {
        response = await fetch('/api/v1/auth/refresh', {
          method: 'POST',
          headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
          body: JSON.stringify({ refreshToken: auth.refreshToken })
        });
      } catch { throw new Error(errorMessage('', 503)); }
      const payload = await response.json().catch(() => null);
      if (state.auth !== auth) throw new Error(errorMessage('', 401));
      if (!response.ok) {
        if (response.status === 401 || response.status === 400) signOut(errorMessage('', 401));
        throw new Error(errorMessage(payload?.message, response.status));
      }
      // Preserve the identity used by pending requests to detect a real sign-out.
      Object.assign(auth, payload.data);
      enterApp(auth, false);
      return auth;
    })().finally(() => { if (refreshRequest === request) refreshRequest = null; });
    refreshRequest = request;
    return request.promise;
  }

  async function api(path, { method = 'GET', body, binary = false } = {}, retry = true) {
    const auth = state.auth;
    const requestToken = auth?.token;
    const headers = { Accept: 'application/json' };
    if (body !== undefined && !(body instanceof FormData)) headers['Content-Type'] = 'application/json';
    if (state.auth) headers.Authorization = 'Bearer ' + state.auth.token;
    let response;
    try {
      response = await fetch(path, { method, headers, ...(body === undefined ? {} : { body: body instanceof FormData ? body : JSON.stringify(body) }) });
    } catch {
      throw new Error('ارتباط با سرور برقرار نشد. اتصال را بررسی و دوباره تلاش کن.');
    }
    if (response.ok && binary) return response.blob();
    const payload = await response.json().catch(() => null);
    if (!response.ok) {
      if (response.status === 401 && auth && state.auth === auth && retry && auth.refreshToken) {
        if (auth.token === requestToken) await refreshAuth();
        return api(path, { method, body, binary }, false);
      }
      if (response.status === 401 && auth && state.auth === auth) signOut('زمان ورودت تمام شده؛ لطفاً دوباره وارد شو.');
      throw new Error(errorMessage(payload?.message, response.status));
    }
    return payload;
  }

  async function uploadCdn(files, ticketId) {
    if (!files.length) return [];
    if (files.length > 5 || files.some(file => !file.size) || files.reduce((sum, file) => sum + file.size, 0) > 20 * 1024 * 1024)
      throw new Error('حداکثر ۵ فایل با مجموع حجم ۲۰ مگابایت انتخاب کنید.');
    const grant = (await api('/Tickets/v1/Media/Uploads', { method: 'POST', body: { category: 'MessageAttachment', ...(ticketId ? { ticketId } : {}) } })).data;
    const form = new FormData(); files.forEach(file => form.append('files', file, file.name));
    const response = await fetch(grant.uploadUrl, { method: 'POST', headers: { Authorization: 'Bearer ' + grant.token }, body: form });
    const payload = await response.json().catch(() => null);
    if (!response.ok || payload?.status !== 0) throw new Error(payload?.message || 'آپلود فایل انجام نشد. دوباره تلاش کنید.');
    return payload.result.map(file => ({ receipt: file.receipt }));
  }

  function loadRoleOptions() {
    if (roleOptionsLoaded) return Promise.resolve();
    if (roleOptionsRequest) return roleOptionsRequest;
    $('role').disabled = true;
    if (state.mode === 'register') $('auth-submit').disabled = true;
    roleOptionsRequest = api('/api/v1/auth/roles').then(response => {
      const roles = response.data;
      if (!Array.isArray(roles) || !roles.length) throw new Error('نقش‌های کاربری در دسترس نیستند.');
      $('role').replaceChildren(...roles.map(role => new Option(role.label, role.value)));
      $('role').disabled = false;
      roleOptionsLoaded = true;
    }).finally(() => {
      roleOptionsRequest = null;
      if (state.mode === 'register') $('auth-submit').disabled = !roleOptionsLoaded;
    });
    return roleOptionsRequest;
  }

  function setAuthMode(mode) {
    state.mode = mode;
    const register = mode === 'register';
    document.querySelectorAll('.register-field').forEach(node => { node.hidden = !register; });
    $('full-name').required = register;
    $('confirm-password').required = register;
    $('password').autocomplete = register ? 'new-password' : 'current-password';
    $('auth-title').textContent = register ? 'حساب خودت را بساز' : 'وارد حساب خودت شو';
    $('auth-subtitle').textContent = register ? 'حسابت را بساز و گفت‌وگو را از همین‌جا شروع کن.' : 'برای پیگیری درخواست‌ها، اطلاعات حسابت را وارد کن.';
    $('auth-submit').querySelector('span').textContent = register ? 'ساخت حساب و ورود' : 'ورود به حساب';
    $('login-tab').setAttribute('aria-selected', String(!register));
    $('register-tab').setAttribute('aria-selected', String(register));
    showError('auth-error');
    $('auth-submit').disabled = register && !roleOptionsLoaded;
    if (register) loadRoleOptions().catch(error => showError('auth-error', error.message));
  }

  function signOut(message = '') {
    clearTimeout(state.expiryTimer);
    stopRealtime();
    state.auth = null;
    state.ticket = null;
    state.listVersion++;
    try { sessionStorage.removeItem(storageKey); } catch { /* Memory-only mode remains available. */ }
    if ($('ticket-dialog').open) $('ticket-dialog').close();
    $('app-screen').hidden = true;
    $('auth-screen').hidden = false;
    $('auth-form').reset();
    setAuthMode('login');
    showError('auth-error', message);
    document.title = 'پیگیری | سامانه پشتیبانی';
  }

  function scheduleExpiry() {
    clearTimeout(state.expiryTimer);
    if (!state.auth) return;
    const remaining = Date.parse(state.auth.expiresAt) - Date.now() - (state.auth.refreshToken ? 60000 : 0);
    if (remaining <= 0) {
      if (state.auth.refreshToken) {
        refreshAuth().catch(error => {
          if (!state.auth) return;
          toast(error.message, true);
          state.expiryTimer = setTimeout(scheduleExpiry, 60000);
        });
        return;
      }
      signOut('زمان ورودت تمام شده؛ لطفاً دوباره وارد شو.');
      return;
    }
    // Browser timers cannot wait longer than about 24.8 days. Recheck daily.
    state.expiryTimer = setTimeout(scheduleExpiry, Math.min(remaining, 24 * 60 * 60 * 1000));
  }

  function enterApp(auth, resetView = true) {
    if (!auth?.token || !['Client', 'Agent'].includes(auth.user?.role) || !Array.isArray(auth.user?.permissions)) {
      signOut('اطلاعات ورود معتبر نیست. دوباره وارد حسابت شو.');
      return;
    }
    const remaining = Date.parse(auth.expiresAt) - Date.now();
    if (!(remaining > 0)) { signOut('زمان ورودت تمام شده؛ لطفاً دوباره وارد شو.'); return; }
    state.auth = auth;
    scheduleExpiry();
    try { sessionStorage.setItem(storageKey, JSON.stringify(auth)); } catch { /* Keep the active login in memory. */ }
    $('auth-screen').hidden = true;
    $('app-screen').hidden = false;
    $('create-nav').hidden = !can(permissions.create);
    $('new-ticket').hidden = !can(permissions.create);
    $('pending-nav').hidden = !can(permissions.queue);
    reportNav.hidden = !isAgent();
    $('filter-priority').hidden = !isAgent();
    $('priority-column').hidden = !isAgent();
    $('search').placeholder = isAgent() ? 'جستجو در عنوان و متن تیکت…' : 'جستجو با موضوع و نام پشتیبان…';
    $('search').setAttribute('aria-label', isAgent() ? 'جستجوی تیکت' : 'جستجوی موضوع و نام پشتیبان');
    $('tickets-nav-label').textContent = isAgent() ? 'گفت‌وگوهای من' : 'تیکت‌های من';
    $('role-label').textContent = isAgent() ? 'پشتیبانی' : 'کاربری';
    $('user-role').textContent = isAgent() ? 'کارشناس پشتیبانی' : 'کاربر';
    $('user-name').textContent = auth.user.fullName;
    $('user-avatar').textContent = auth.user.fullName.trim().split(/\s+/).slice(0, 2).map(part => part[0]).join(' ');
    $('greeting').textContent = 'سلام، ' + auth.user.fullName.trim().split(/\s+/)[0] + ' 👋';
    $('today').textContent = new Intl.DateTimeFormat('fa-IR', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric', timeZone: 'Asia/Tehran' }).format(new Date());
    $('footer-scope').textContent = isAgent() ? 'صف درخواست‌ها و گفت‌وگوهای پذیرفته‌شدهٔ شما' : 'فضای اختصاصی درخواست‌های شما';
    ensureRealtime();
    if (resetView) setView('overview');
  }

  function setView(view) {
    if (!state.auth) return;
    if (view === 'create' && !can(permissions.create)) return toast('امکان ثبت تیکت برای این حساب فعال نیست.', true);
    if (view === 'pending' && !can(permissions.queue)) return;
    if (view === 'reports' && !isAgent()) return;
    state.view = view;
    state.page = 1;
    state.perPage = view === 'overview' ? 6 : 10;
    $('search').value = '';
    $('filter-priority').value = '';
    $('filter-status').value = view === 'pending' ? 'Open' : '';
    $('filter-status').disabled = view === 'pending';
    $('create-panel').hidden = view !== 'create';
    $('tickets-panel').hidden = view === 'create' || view === 'reports';
    $('summary').hidden = view === 'create' || view === 'reports';
    $('reports-panel').hidden = view !== 'reports';
    $('new-ticket').hidden = !can(permissions.create) || view === 'create';
    const labels = { overview: 'نمای کلی', create: 'ثبت تیکت جدید', tickets: isAgent() ? 'گفت‌وگوهای من' : 'تیکت‌های من', pending: 'صف درخواست‌ها', reports: 'گزارش‌ها' };
    $('breadcrumb-current').textContent = labels[view];
    $('page-title').textContent = view === 'overview' ? (isAgent() ? 'مرکز رسیدگی به درخواست‌ها' : 'درخواست‌هایت را دنبال کن') : labels[view];
    $('page-description').textContent = view === 'create' ? 'یک گفت‌وگوی تازه برای رسیدن به پاسخ شروع کن.' : isAgent() ? 'درخواست‌های کاربران را بررسی کن و گفت‌وگوها را پیش ببر.' : 'هر چیزی که برای ثبت و پیگیری درخواست‌ها نیاز داری، همین‌جاست.';
    $('list-title').textContent = view === 'overview' ? 'آخرین تیکت‌ها' : labels[view];
    document.querySelectorAll('[data-view]').forEach(button => button.classList.toggle('active', button.dataset.view === view));
    document.title = labels[view] + ' | پیگیری';
    if (view === 'reports') loadReport();
    else if (view !== 'create') refresh();
    else { showError('create-error'); $('ticket-title').focus(); }
  }

  async function loadSummary() {
    const auth = state.auth;
    const response = await api(base + '/Dashboard');
    if (state.auth !== auth) return;
    const data = response.data;
    $('stat-total').textContent = number(data.total);
    $('stat-active').textContent = number(data.open + data.inProgress);
    $('stat-answered').textContent = number(data.answered);
    $('stat-closed').textContent = number(data.closed);
  }

  async function loadReport() {
    const auth = state.auth;
    $('report-print').disabled = true;
    $('report-download').disabled = true;
    currentReport = null;
    $('report-results').hidden = true;
    showError('report-error');
    try {
      for (const id of ['report-from', 'report-to']) {
        const parts = [...$(id + '-parts').querySelectorAll('select')];
        if (parts.some(part => part.value) && (!parts.every(part => part.value) || !$(id).value)) throw new Error('تاریخ شمسی را کامل و معتبر انتخاب کنید.');
      }
      const query = new URLSearchParams();
      if ($('report-from').value) query.set('from', $('report-from').value);
      if ($('report-to').value) query.set('to', $('report-to').value);
      const { data } = await api(base + '/Reports?' + query);
      if (state.auth !== auth) return;
      const { formatPersianDate } = await import('/lib/report-export/persian-date.js');
      $('report-period').textContent = formatPersianDate(data.from) + ' تا ' + formatPersianDate(data.to) + ' · فقط پذیرفته‌شده‌های من · ساعت تهران';
      $('report-summary').replaceChildren(...Object.entries({ 'کل درخواست‌ها': data.summary.total, 'باز': data.summary.open, 'در جریان': data.summary.inProgress, 'پاسخ داده‌شده': data.summary.answered, 'بسته‌شده': data.summary.closed }).map(([label, count]) => element('span', '', label + ': ' + number(count))));
      const maximum = Math.max(1, ...data.dailyCreated.map(day => day.count));
      $('report-bars').replaceChildren(...data.dailyCreated.map(day => {
        const bar = element('div', 'report-bar');
        bar.style.height = (day.count / maximum * 100) + '%';
        bar.title = day.date + ': ' + number(day.count) + ' درخواست';
        bar.setAttribute('aria-label', bar.title);
        return bar;
      }));
      const colors = ['#3174cc', '#d49b32', '#9274ca', '#30a18a'];
      const counts = ['open', 'inProgress', 'answered', 'closed'].map(key => data.summary[key]);
      let start = 0;
      const segments = counts.map((count, index) => { const end = start + (data.summary.total ? count / data.summary.total * 100 : 0); const segment = colors[index] + ' ' + start + '% ' + end + '%'; start = end; return segment; });
      $('report-pie').style.background = data.summary.total ? 'conic-gradient(' + segments.join(',') + ')' : '#e8eef4';
      $('report-legend').replaceChildren(...Object.values(statuses).map((label, index) => element('li', '', label + ': ' + number(counts[index]))));
      $('report-generated').textContent = 'زمان تهیه: ' + date(data.generatedAt, true) + (data.summary.total ? '' : ' · در این بازه درخواستی وجود ندارد.');
      $('report-results').hidden = false;
      $('report-print').disabled = false;
      $('report-download').disabled = false;
      currentReport = data;
    } catch (error) { showError('report-error', error.message); }
  }

  function statusBadge(status) { return element('span', 'badge status-' + status, statuses[status] || status); }
  function priorityBadge(priority) { return element('span', priority ? 'priority ' + priority : 'priority-unset', priorities[priority] || 'هنوز تعیین نشده'); }

  function renderRows(tickets) {
    const rows = document.createDocumentFragment();
    for (const ticket of tickets) {
      const row = element('tr');
      row.dataset.ticketId = String(ticket.id);
      const titleCell = element('td');
      const content = element('div', 'ticket-cell');
      const mark = element('span', 'ticket-table-icon');
      mark.append(icon('ticket'));
      const text = element('div');
      const title = element('button', 'ticket-title-button', ticket.title);
      title.type = 'button';
      title.addEventListener('click', () => openTicket(ticket.id));
      text.append(title, element('span', 'ticket-id', '#' + number(ticket.id) + ' · ' + (isAgent() ? ticket.owner.displayName : (ticket.assignedTo ? 'پشتیبان: ' + ticket.assignedTo.displayName : 'در انتظار پذیرش'))));
      content.append(mark, text);
      titleCell.append(content);
      const priorityCell = element('td');
      if (isAgent()) priorityCell.append(priorityBadge(ticket.priority));
      else priorityCell.hidden = true;
      const statusCell = element('td');
      statusCell.append(statusBadge(ticket.status));
      const updatedCell = element('td', 'table-date', date(ticket.updatedAt || ticket.createdAt));
      const actionCell = element('td');
      const action = element('button', 'detail-button');
      action.type = 'button';
      action.setAttribute('aria-label', 'مشاهده‌ی ' + ticket.title);
      action.append(icon('arrow'));
      action.addEventListener('click', () => openTicket(ticket.id));
      actionCell.append(action);
      row.append(titleCell, priorityCell, statusCell, updatedCell, actionCell);
      rows.append(row);
    }
    $('ticket-rows').replaceChildren(rows);
  }

  async function loadList() {
    const version = ++state.listVersion;
    $('list-loading').hidden = false;
    $('table-container').hidden = true;
    $('empty-state').hidden = true;
    $('pagination').hidden = true;
    showError('list-error');
    const query = new URLSearchParams({ page: state.page, perPage: state.perPage });
    if (state.view === 'pending') query.set('unassignedOnly', 'true');
    if (state.view === 'tickets' && isAgent()) query.set('mineOnly', 'true');
    for (const [key, id] of [['search', 'search'], ['status', 'filter-status'], ['priority', 'filter-priority']]) {
      if ((key !== 'priority' || isAgent()) && $(id).value.trim()) query.set(key, $(id).value.trim());
    }
    try {
      const response = await api(base + '/List?' + query);
      if (version !== state.listVersion || !state.auth) return;
      if (!response.data.length && state.page > 1 && response.pagination.totalItems > 0) {
        state.page = Math.max(1, response.pagination.totalPages);
        return loadList();
      }
      renderRows(response.data);
      $('table-container').hidden = !response.data.length;
      $('empty-state').hidden = !!response.data.length;
      const filtered = query.has('search') || query.has('status') || query.has('priority');
      $('empty-title').textContent = filtered ? 'درخواستی با این مشخصات پیدا نشد' : isAgent() ? 'هنوز درخواستی برای رسیدگی نیست' : 'هنوز تیکتی ثبت نکرده‌ای';
      $('empty-description').textContent = filtered ? 'فیلترها را تغییر بده و دوباره جستجو کن.' : isAgent() ? 'درخواست‌های جدید کاربران در این بخش نمایش داده می‌شوند.' : 'اولین درخواستت را ثبت کن؛ ما برای پاسخ‌دادن اینجاییم.';
      $('empty-create').hidden = !can(permissions.create) || filtered;
      $('pagination').hidden = !response.pagination.totalItems;
      const start = (state.page - 1) * state.perPage + 1;
      const end = start + response.data.length - 1;
      $('list-count').textContent = 'نمایش ' + number(start) + ' تا ' + number(end) + ' از ' + number(response.pagination.totalItems) + ' تیکت';
      $('page-number').textContent = number(state.page) + ' / ' + number(Math.max(1, response.pagination.totalPages));
      $('previous-page').disabled = state.page <= 1;
      $('next-page').disabled = state.page >= response.pagination.totalPages;
    } catch (error) {
      if (version === state.listVersion && state.auth) showError('list-error', error.message);
    } finally {
      if (version === state.listVersion) $('list-loading').hidden = true;
    }
  }

  async function refresh() {
    const results = await Promise.allSettled([loadSummary(), loadList()]);
    if (results[0].status === 'rejected' && state.auth) toast(results[0].reason.message, true);
  }

  function renderMessages() {
    mediaUrls.forEach(url => URL.revokeObjectURL(url));
    mediaUrls = [];
    const fragment = document.createDocumentFragment();
    const visibleMessages = messages.filter(message => !message.deletedAt);
    for (const message of visibleMessages) {
      const article = element('article', 'chat-message' + (message.author.role === 'Agent' ? ' agent' : '') + (message.author.id === state.auth?.user.id ? ' mine' : ''));
      article.dataset.messageId = String(message.id);
      const header = element('div', 'message-heading');
      const author = element('b', '', message.author.displayName);
      author.append(element('span', 'author-role', message.author.role === 'Agent' ? 'پشتیبان' : 'کاربر'));
      header.append(author, element('time', '', date(message.createdAt, true)));
      article.append(header, element('p', '', message.body));
      for (const file of message.attachments || []) {
        const card = element('div', 'message-media');
        const caption = element('span', '', file.fileName);
        const loading = element('small', '', 'در حال دریافت پیوست…');
        card.append(caption, loading); article.append(card);
        api(`${base}/${message.ticketId}/Messages/${message.id}/Attachments/${file.id}`, { binary: true }).then(blob => {
          if (!card.isConnected) return;
          if (blob.size !== file.size) throw new Error('Incomplete attachment.');
          const url = URL.createObjectURL(blob); mediaUrls.push(url);
          const tag = file.contentType.startsWith('image/') && !file.contentType.startsWith('image/hei') ? 'img' : file.contentType.startsWith('video/') ? 'video' : file.contentType.startsWith('audio/') ? 'audio' : null;
          if (tag) {
            const preview = document.createElement(tag); preview.src = url;
            if (tag === 'img') preview.alt = file.fileName;
            else { preview.controls = true; preview.preload = 'metadata'; }
            card.prepend(preview);
          }
          const link = element('a', '', 'دانلود'); link.href = url; link.download = file.fileName;
          loading.replaceWith(link);
        }).catch(() => { if (card.isConnected) loading.textContent = 'دریافت پیوست ناموفق بود. گفت‌وگو را به‌روز کنید.'; });
      }
      if (message.editedAt) article.append(element('small', '', 'ویرایش‌شده'));
      if (message.author.id === state.auth?.user.id && !message.isRead) {
        const controls = element('div', 'message-actions');
        for (const [action, label] of [['edit', 'ویرایش'], ['delete', 'حذف']]) {
          const button = element('button', 'button secondary', label);
          button.type = 'button';
          button.dataset.messageAction = action;
          button.dataset.messageId = String(message.id);
          controls.append(button);
        }
        article.append(controls);
      }
      fragment.append(article);
    }
    if (!visibleMessages.length) fragment.append(element('p', 'conversation-empty', 'گفت‌وگو آماده است. پیام خودت را بنویس.'));
    const list = $('chat-messages');
    const nearEnd = list.scrollHeight - list.scrollTop - list.clientHeight < 100;
    list.replaceChildren(fragment);
    if (nearEnd) list.scrollTop = list.scrollHeight;
    $('message-count').textContent = number(visibleMessages.length) + ' پیام';
  }

  function mergeMessages(incoming) {
    const all = new Map(messages.map(message => [message.id, message]));
    for (const message of incoming) {
      const previous = all.get(message.id);
      const latest = previous?.deletedAt ? previous : message.deletedAt ? message
        : previous && (previous.version || 0) > (message.version || 0) ? previous : message;
      all.set(message.id, previous?.isRead || message.isRead ? { ...latest, isRead: true } : latest);
    }
    messages = [...all.values()].sort((a, b) => Date.parse(a.createdAt) - Date.parse(b.createdAt) || a.id - b.id);
    renderMessages();
  }

  function renderDetail(ticket) {
    if (state.ticket?.id !== ticket.id) { messages = []; pendingMessage = null; }
    state.ticket = ticket;
    $('detail-number').textContent = 'درخواست شماره ' + number(ticket.id);
    $('detail-title').textContent = ticket.title;
    $('detail-meta').replaceChildren(statusBadge(ticket.status), ...(isAgent() ? [priorityBadge(ticket.priority)] : []));
    const accepted = !!ticket.assignedToId && !!ticket.acceptedAt;
    const closed = ticket.status === 'Closed';
    const assignedAgent = isAgent() && ticket.assignedToId === state.auth.user.id;
    const editable = !isAgent() && ticket.createdById === state.auth.user.id && !accepted && !closed;
    $('ticket-actions').hidden = !editable;
    if (!editable) $('edit-request-form').hidden = true;
    $('waiting-note').hidden = accepted || isAgent() || closed;
    $('accept-form').hidden = accepted || closed || !can(permissions.accept);
    $('conversation-panel').hidden = !can(permissions.chat);
    $('chat-form').hidden = (isAgent() && !accepted) || closed || !can(permissions.chat);
    $('closed-note').hidden = !closed;
    $('close-form').hidden = !assignedAgent || closed || !can(permissions.status);
    $('priority-form').hidden = !assignedAgent || closed;
    $('agent-priority').value = ticket.priority || 'Medium';
    $('participants-label').textContent = accepted ? ticket.owner.displayName + ' و ' + ticket.assignedTo.displayName
      : isAgent() ? 'پیام‌های مشتری پیش از پذیرش' : 'پیام‌های شما برای پشتیبانی';
    renderMessages();
  }

  async function loadConversation() {
    const ticket = state.ticket;
    const auth = state.auth;
    if (!ticket || !can(permissions.chat)) return;
    const response = await api(base + '/' + ticket.id + '/Conversation?includeDeleted=true');
    if (state.auth !== auth || state.ticket?.id !== ticket.id) return;
    mergeMessages(response.data.messages);
    acknowledgeVisibleMessages();
  }

  async function acknowledgeVisibleMessages() {
    if (!state.ticket || !$('ticket-dialog').open || document.hidden || !document.hasFocus()) return;
    const list = $('chat-messages').getBoundingClientRect();
    const incoming = messages.filter(message => !message.deletedAt && message.author.id !== state.auth?.user.id && !message.isRead &&
      [...$('chat-messages').querySelectorAll('article[data-message-id]')].some(article => {
        if (Number(article.dataset.messageId) !== message.id) return false;
        const rect = article.getBoundingClientRect();
        return rect.bottom > Math.max(list.top, 0) && rect.top < Math.min(list.bottom, innerHeight);
      }));
    if (!incoming.length) return;
    const ticketId = state.ticket.id;
    try {
      for (const message of incoming) await api(base + '/' + ticketId + '/Read', { method: 'POST', body: { lastReadMessageId: message.id, version: message.version } });
    } catch { /* Retried on the next focus, scroll, or history refresh. */ }
  }

  async function openTicket(id) {
    try {
      const auth = state.auth;
      const response = await api(base + '/Select/' + id);
      if (!state.auth || state.auth !== auth) return;
      renderDetail(response.data);
      $('chat-body').value = '';
      showError('detail-error');
      showError('chat-error');
      if (!$('ticket-dialog').open) $('ticket-dialog').showModal();
      await loadConversation();
    } catch (error) { if (state.auth) toast(error.message, true); }
  }

  function connectionStatus(text, connected = false) {
    $('connection-status').textContent = text;
    $('connection-status').classList.toggle('connected', connected);
  }

  function stopRealtime() {
    clearTimeout(reconnectTimer);
    clearTimeout(liveRefreshTimer);
    const previous = connection;
    connection = null;
    connectionAuth = null;
    previous?.stop().catch(() => {});
    connectionStatus('قطع ارتباط');
  }

  function refreshLiveLists() {
    clearTimeout(liveRefreshTimer);
    liveRefreshTimer = setTimeout(() => { if (state.auth && state.view !== 'create') refresh(); }, 100);
  }

  function ensureRealtime() {
    const auth = state.auth;
    if (!auth || connectionAuth === auth) return;
    stopRealtime();
    connectionAuth = auth;
    const current = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/tickets', { accessTokenFactory: async () => {
        if (state.auth !== auth) throw new Error('Signed out');
        if (Date.parse(auth.expiresAt) - Date.now() < 60000 && auth.refreshToken) await refreshAuth();
        return auth.token;
      } })
      .withAutomaticReconnect([0, 2000, 10000, 30000])
      .configureLogging(signalR.LogLevel.None).build();
    connection = current;
    connectionStatus('در حال اتصال…');
    current.on('QueueChanged', async change => {
      if (state.auth !== auth) return;
      refreshLiveLists();
      if (state.ticket?.id === change.ticketId && !state.ticket.assignedToId) {
        try {
          const result = await api(base + '/Select/' + change.ticketId);
          if (state.auth !== auth || state.ticket?.id !== change.ticketId) return;
          renderDetail(result.data);
          await loadConversation();
        } catch {
          if (state.auth === auth && state.ticket?.id === change.ticketId) {
            $('ticket-dialog').close();
            toast('پشتیبان دیگری این درخواست را پذیرفت.');
          }
        }
      }
    });
    current.on('TicketChanged', async ticket => {
      if (state.auth !== auth) return;
      refreshLiveLists();
      if (state.ticket?.id === ticket.id) {
        renderDetail(ticket);
        try { await loadConversation(); } catch (error) { toast(error.message, true); }
      }
    });
    current.on('MessageReceived', message => {
      if (state.auth !== auth) return;
      if (state.ticket?.id === message.ticketId) mergeMessages([message]);
      else toast('پیام جدید برای درخواست شماره ' + number(message.ticketId));
      refreshLiveLists();
      acknowledgeVisibleMessages();
    });
    current.on('MessagesRead', receipt => {
      if (state.auth !== auth || state.ticket?.id !== receipt.ticketId) return;
      messages = messages.map(message => message.author.id !== receipt.userId && message.id === receipt.messageId ? { ...message, isRead: true } : message);
      renderMessages();
    });
    current.on('TicketDeleted', change => {
      if (state.auth !== auth) return;
      if (state.ticket?.id === change.ticketId) $('ticket-dialog').close();
      refreshLiveLists();
    });
    current.onreconnecting(() => { if (state.auth === auth) connectionStatus('در حال اتصال دوباره…'); });
    current.onreconnected(async () => {
      if (state.auth !== auth) return;
      connectionStatus('متصل', true);
      refreshLiveLists();
      if (state.ticket) {
        const id = state.ticket.id;
        try {
          const result = await api(base + '/Select/' + id);
          if (state.auth !== auth || state.ticket?.id !== id) return;
          renderDetail(result.data);
          await loadConversation();
        } catch (error) { toast(error.message, true); }
      }
    });
    const retry = () => {
      if (state.auth !== auth || connection !== current) return;
      connectionStatus('ارتباط قطع شد؛ تلاش دوباره…');
      connectionAuth = null;
      reconnectTimer = setTimeout(ensureRealtime, 5000);
    };
    current.onclose(retry);
    current.start().then(() => {
      if (state.auth !== auth || connection !== current) return;
      connectionStatus('متصل', true);
      refreshLiveLists();
      if (state.ticket?.assignedToId) loadConversation().catch(error => toast(error.message, true));
    }).catch(retry);
  }

  const reportNav = element('button', 'nav-item', 'گزارش‌ها');
  reportNav.dataset.view = 'reports';
  document.querySelector('.sidebar nav').append(reportNav);
  $('report-filters').addEventListener('submit', event => { event.preventDefault(); loadReport(); });
  $('report-print').addEventListener('click', () => window.print());
  $('report-download').addEventListener('click', async () => {
    if (!currentReport || !isAgent() || $('report-download').disabled) return;
    const button = $('report-download'); busy(button, true);
    try {
      const { downloadReportPdf } = await import('/lib/report-export/report-pdf.js');
      await downloadReportPdf(currentReport, state.auth.user.fullName);
    } catch { showError('report-error', 'ساخت PDF ناموفق بود. دوباره تلاش کنید.'); }
    finally { busy(button, false); }
  });
  import('/lib/report-export/persian-date.js').then(({ fromPersianDate, persianMonthLength, persianMonths }) => {
    for (const id of ['report-from', 'report-to']) {
      const group = $(id + '-parts');
      const selects = ['روز', 'ماه', 'سال'].map(label => {
        const select = element('select'); select.setAttribute('aria-label', label + ' ' + (id === 'report-from' ? 'شروع' : 'پایان'));
        const empty = element('option', '', label); empty.value = ''; select.append(empty); group.append(select); return select;
      });
      const [day, month, year] = selects;
      const option = (value, label) => { const item = element('option', '', label); item.value = String(value); return item; };
      for (let value = 1; value <= 31; value++) day.append(option(value, number(value)));
      persianMonths.forEach((name, index) => month.append(option(index + 1, name)));
      for (let value = 1378; value <= 1479; value++) year.append(option(value, value.toLocaleString('fa-IR', { useGrouping: false })));
      for (const select of selects) select.addEventListener('change', () => {
        const maximum = year.value && month.value ? persianMonthLength(Number(year.value), Number(month.value)) : 31;
        [...day.options].forEach(item => { item.disabled = Number(item.value) > maximum; });
        if (Number(day.value) > maximum) day.value = '';
        $(id).value = selects.every(item => item.value) ? fromPersianDate(Number(year.value), Number(month.value), Number(day.value)) : '';
      });
    }
  }).catch(() => showError('report-error', 'تقویم بارگذاری نشد. صفحه را تازه کنید.'));
  $('login-tab').addEventListener('click', () => setAuthMode('login'));
  $('register-tab').addEventListener('click', () => setAuthMode('register'));
  $('auth-form').addEventListener('submit', async event => {
    event.preventDefault();
    const button = $('auth-submit');
    if (button.disabled) return;
    showError('auth-error');
    const body = { countryId: Number($('countryId').value), mobile: $('mobile').value.trim(), password: $('password').value };
    if (state.mode === 'register') {
      if (!roleOptionsLoaded || !$('role').value) return showError('auth-error', 'ابتدا نقش کاربری را انتخاب کن.');
      body.fullName = $('full-name').value.trim();
      body.confirmPassword = $('confirm-password').value;
      body.role = $('role').value;
      if (body.password !== body.confirmPassword) return showError('auth-error', 'رمز عبور و تکرار آن یکسان نیستند.');
    }
    busy(button, true);
    try {
      const response = await api('/api/v1/auth/' + (state.mode === 'register' ? 'register' : 'login'), { method: 'POST', body });
      $('password').value = '';
      $('confirm-password').value = '';
      enterApp(response.data);
    } catch (error) { showError('auth-error', error.message); }
    finally { busy(button, false); }
  });

  document.querySelectorAll('[data-view]').forEach(button => button.addEventListener('click', () => setView(button.dataset.view)));
  for (const id of ['new-ticket', 'empty-create']) $(id).addEventListener('click', () => setView('create'));
  $('cancel-create').addEventListener('click', () => setView('tickets'));
  $('logout').addEventListener('click', () => signOut());
  $('refresh').addEventListener('click', () => refresh());
  $('filters').addEventListener('submit', event => { event.preventDefault(); state.page = 1; loadList(); });
  $('previous-page').addEventListener('click', () => { if (state.page > 1) { state.page--; loadList(); } });
  $('next-page').addEventListener('click', () => { state.page++; loadList(); });
  $('close-dialog').addEventListener('click', () => $('ticket-dialog').close());
  $('ticket-dialog').addEventListener('close', () => {
    state.ticket = null; $('edit-request-form').hidden = true;
    mediaUrls.forEach(url => URL.revokeObjectURL(url)); mediaUrls = [];
    $('chat-messages').replaceChildren();
  });
  $('ticket-dialog').addEventListener('click', event => { if (event.target === $('ticket-dialog')) $('ticket-dialog').close(); });
  window.addEventListener('focus', acknowledgeVisibleMessages);
  document.addEventListener('visibilitychange', acknowledgeVisibleMessages);
  $('chat-messages').addEventListener('scroll', acknowledgeVisibleMessages);

  $('edit-request').addEventListener('click', () => {
    if (!state.ticket) return;
    $('edit-request-title').value = state.ticket.title;
    $('edit-request-form').hidden = false;
    $('edit-request-title').focus();
  });
  $('cancel-edit-request').addEventListener('click', () => { $('edit-request-form').hidden = true; });
  $('edit-request-form').addEventListener('submit', async event => {
    event.preventDefault();
    const ticketId = state.ticket?.id;
    if (!ticketId) return;
    const button = event.currentTarget.querySelector('[type=submit]');
    if (button.disabled) return;
    busy(button, true);
    try {
      const result = await api(base + '/' + ticketId, { method: 'PATCH', body: { title: $('edit-request-title').value.trim() } });
      if (state.ticket?.id !== ticketId) return;
      renderDetail(result.data);
      $('edit-request-form').hidden = true;
      refreshLiveLists();
    } catch (error) { showError('detail-error', error.message); }
    finally { busy(button, false); }
  });
  $('delete-request').addEventListener('click', async () => {
    const ticketId = state.ticket?.id;
    if (!ticketId || !confirm('درخواست و تمام پیام‌های آن حذف شوند؟')) return;
    try {
      await api(base + '/' + ticketId, { method: 'DELETE' });
      if (state.ticket?.id === ticketId) $('ticket-dialog').close();
      refreshLiveLists();
    } catch (error) { showError('detail-error', error.message); }
  });
  $('chat-messages').addEventListener('click', async event => {
    const button = event.target.closest('[data-message-action]');
    const ticketId = state.ticket?.id;
    if (!button || !ticketId || button.disabled) return;
    const message = messages.find(item => item.id === Number(button.dataset.messageId));
    if (!message) return;
    const deleting = button.dataset.messageAction === 'delete';
    const body = deleting ? null : prompt('ویرایش پیام', message.body);
    if (deleting ? !confirm('پیام حذف شود؟') : !body?.trim()) return;
    button.disabled = true;
    try {
      const response = await api(base + '/' + ticketId + '/Messages/' + message.id, { method: deleting ? 'DELETE' : 'PATCH', ...(deleting ? {} : { body: { body: body.trim() } }) });
      if (state.ticket?.id === ticketId) mergeMessages([response.data]);
    } catch (error) { showError('chat-error', error.message); await loadConversation(); }
    finally { button.disabled = false; }
  });

  $('ticket-form').addEventListener('submit', async event => {
    event.preventDefault();
    if (!can(permissions.create)) return;
    const button = event.currentTarget.querySelector('[type=submit]');
    if (button.disabled) return;
    busy(button, true);
    showError('create-error');
    try {
      let body = { title: $('ticket-title').value.trim(), description: $('ticket-description').value.trim() };
      const files = Array.from($('ticket-files').files);
      if (files.length > 5 || files.some(file => !file.size) || files.reduce((total, file) => total + file.size, 0) > 20 * 1024 * 1024)
        throw new Error('حداکثر ۵ فایل غیرخالی با مجموع حجم ۲۰ مگابایت انتخاب کنید.');
      if (files.length) {
        body.attachments = await uploadCdn(files);
      }
      const response = await api(base + '/Create', { method: 'POST', body });
      $('ticket-form').reset();
      setView('tickets');
      renderDetail(response.data);
      $('chat-body').value = '';
      $('ticket-dialog').showModal();
      await loadConversation();
      toast('درخواستت با موفقیت ثبت شد.');
    } catch (error) { showError('create-error', error.message); }
    finally { busy(button, false); }
  });

  $('accept-form').addEventListener('submit', async event => {
    event.preventDefault();
    if (!state.ticket || !can(permissions.accept)) return;
    const button = event.currentTarget.querySelector('[type=submit]');
    if (button.disabled) return;
    busy(button, true);
    showError('detail-error');
    try {
      const result = await api(base + '/' + state.ticket.id + '/Accept', { method: 'POST', body: { priority: $('accept-priority').value } });
      renderDetail(result.data);
      await loadConversation();
      refreshLiveLists();
      $('chat-body').focus();
    } catch (error) { showError('detail-error', error.message); }
    finally { busy(button, false); }
  });

  $('priority-form').addEventListener('submit', async event => {
    event.preventDefault();
    if (!state.ticket || !can(permissions.priority)) return;
    const button = event.currentTarget.querySelector('[type=submit]');
    if (button.disabled) return;
    busy(button, true);
    showError('detail-error');
    try {
      const result = await api(base + '/' + state.ticket.id + '/Priority', { method: 'PATCH', body: { priority: $('agent-priority').value } });
      renderDetail(result.data);
      refreshLiveLists();
    } catch (error) { showError('detail-error', error.message); }
    finally { busy(button, false); }
  });

  $('chat-form').addEventListener('submit', async event => {
    event.preventDefault();
    const ticketId = state.ticket?.id;
    const auth = state.auth;
    const body = $('chat-body').value.trim();
    const files = Array.from($('chat-files').files);
    const fileKey = files.map(file => file.name + ':' + file.size + ':' + file.lastModified).join('|');
    if (!ticketId || !body && !files.length) return;
    const button = event.currentTarget.querySelector('[type=submit]');
    if (button.disabled) return;
    busy(button, true);
    showError('chat-error');
    if (!pendingMessage || pendingMessage.ticketId !== ticketId || pendingMessage.body !== body || pendingMessage.fileKey !== fileKey)
      pendingMessage = { ticketId, body, fileKey, clientMessageId: crypto.randomUUID() };
    try {
      if (!pendingMessage.attachments) pendingMessage.attachments = await uploadCdn(files, ticketId);
      const request = { body, clientMessageId: pendingMessage.clientMessageId, ...(pendingMessage.attachments.length ? { attachments: pendingMessage.attachments } : {}) };
      const response = connection?.state === signalR.HubConnectionState.Connected
        ? await connection.invoke('SendMessage', ticketId, request)
        : await api(base + '/' + ticketId + '/Messages', { method: 'POST', body: request });
      if (response.statusCode >= 400) throw new Error(errorMessage(response.message, response.statusCode));
      if (state.auth !== auth || state.ticket?.id !== ticketId) return;
      mergeMessages([response.data]);
      if ($('chat-body').value.trim() === body) $('chat-body').value = '';
      $('chat-files').value = '';
      pendingMessage = null;
      refreshLiveLists();
    } catch (error) {
      if (state.auth === auth && state.ticket?.id === ticketId) showError('chat-error', error.message?.startsWith('Failed') ? 'پیام ارسال نشد. دوباره تلاش کن.' : error.message);
    } finally { busy(button, false); }
  });

  $('close-form').addEventListener('submit', async event => {
    event.preventDefault();
    if (!state.ticket || !can(permissions.status) || state.ticket.status === 'Closed') return;
    const button = event.currentTarget.querySelector('[type=submit]');
    if (button.disabled) return;
    busy(button, true);
    try {
      const response = await api(base + '/' + state.ticket.id + '/Status', { method: 'PATCH', body: { status: 'Closed' } });
      renderDetail(response.data);
      refresh();
      toast('درخواست بسته شد.');
    } catch (error) { toast(error.message, true); }
    finally { busy(button, false); }
  });

  setAuthMode('login');
  loadRoleOptions().catch(error => {
    if (state.mode === 'register') showError('auth-error', error.message);
  });
  async function restoreLogin() {
    try {
      const saved = JSON.parse(sessionStorage.getItem(storageKey) || 'null');
      if (!saved) return;
      if (!saved.user?.mobile) { sessionStorage.removeItem(storageKey); return; }
      if (saved.refreshToken && (Date.parse(saved.expiresAt) <= Date.now() + 60000 || !saved.user?.permissions?.includes(permissions.chat))) {
        state.auth = saved;
        await refreshAuth();
        enterApp(state.auth);
      } else enterApp(saved);
    } catch (error) {
      state.auth = null;
      showError('auth-error', error.message || errorMessage('', 401));
    }
  }
  fetch('/Common/v1/Countries/List').then(response => response.json()).then(payload => {
    if (!Array.isArray(payload.data)) return;
    $('countryId').replaceChildren(...payload.data.map(country => {
      const option = element('option', '', (country.isoCode === 'IR' ? 'ایران' : country.name) + ' (' + country.phoneCode + ')');
      option.value = String(country.id); return option;
    }));
    $('countryId').value = '105';
  }).catch(() => {});
  restoreLogin();
})();
