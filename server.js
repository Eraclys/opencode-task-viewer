#!/usr/bin/env node

const express = require('express');
const path = require('path');

const app = express();

const DEFAULT_PORT = 3456;
const DEFAULT_HOST = '127.0.0.1';
const explicitPort = process.env.PORT ? parseInt(process.env.PORT, 10) : null;
const explicitHost = process.env.HOST || DEFAULT_HOST;
const MAX_PORT_ATTEMPTS = 10;

const OPENCODE_URL = process.env.OPENCODE_URL || 'http://localhost:4096';
const OPENCODE_USERNAME = process.env.OPENCODE_USERNAME || 'opencode';
const OPENCODE_PASSWORD = process.env.OPENCODE_PASSWORD || '';

const SESSIONS_CACHE_TTL_MS = 1500;
const STATUS_CACHE_TTL_MS = 1000;
const TODO_CACHE_TTL_MS = 3000;
const TASKS_ALL_CACHE_TTL_MS = 1500;
const REQUEST_CONCURRENCY = 6;
const MAX_ALL_SESSIONS = 750;
const PROJECTS_CACHE_TTL_MS = 10_000;
const DIRECTORY_SESSIONS_CACHE_TTL_MS = 8_000;
const MAX_SESSIONS_PER_PROJECT = 500;
const MESSAGE_PRESENCE_CACHE_TTL_MS = 120_000;
const SESSION_RECENT_WINDOW_MS = 5 * 60 * 1000;

// Parse JSON bodies
app.use(express.json());

// Serve static files
app.use(express.static(path.join(__dirname, 'public')));

// SSE clients for live updates
const clients = new Set();

// Caches
let sessionsCache = { ts: 0, data: null, byId: new Map() };
let projectsCache = { ts: 0, data: null, byWorktree: new Map() };
const sessionsCacheByDirectory = new Map(); // directory -> {ts,data}
const statusCacheByDirectory = new Map(); // directory -> {ts,data}
const todoCache = new Map(); // `${directory}::${sessionId}` -> {ts,data}
let tasksAllCache = { ts: 0, data: null };

// sessionId -> { ts, hasAssistant }
const assistantPresenceCache = new Map();
const assistantPresenceInFlight = new Map();

// Overrides from SSE events (keeps UI reactive even if status map TTL hasn't expired)
const statusOverride = new Map(); // `${directory}::${sessionId}` -> {type, ts}

function getBasicAuthHeader() {
  if (!OPENCODE_PASSWORD) return null;
  const token = Buffer.from(`${OPENCODE_USERNAME}:${OPENCODE_PASSWORD}`, 'utf8').toString('base64');
  return `Basic ${token}`;
}

function toArrayResponse(value) {
  if (Array.isArray(value)) return value;
  if (value && Array.isArray(value.items)) return value.items;
  if (value && Array.isArray(value.sessions)) return value.sessions;
  if (value && Array.isArray(value.data)) return value.data;
  return [];
}

async function opencodeFetch(endpointPath, { method = 'GET', query = {}, directory = null, json = undefined } = {}) {
  const url = new URL(endpointPath, OPENCODE_URL);
  for (const [k, v] of Object.entries(query || {})) {
    if (v === undefined || v === null || v === '') continue;
    url.searchParams.set(k, String(v));
  }

  const headers = { 'Accept': 'application/json' };
  const auth = getBasicAuthHeader();
  if (auth) headers['Authorization'] = auth;

  // Some OpenCode endpoints accept instance selection via query param, others via header.
  // Send both for maximum compatibility.
  if (directory) {
    url.searchParams.set('directory', directory);
    headers['x-opencode-directory'] = directory;
  }

  const init = { method, headers };
  if (json !== undefined) {
    headers['Content-Type'] = 'application/json; charset=utf-8';
    init.body = JSON.stringify(json);
  }

  const res = await fetch(url, init);
  if (!res.ok) {
    const text = await res.text().catch(() => '');
    const err = new Error(`OpenCode request failed: ${res.status} ${res.statusText}`);
    err.status = res.status;
    err.body = text;
    throw err;
  }

  if (res.status === 204) return null;
  const ct = res.headers.get('content-type') || '';
  if (ct.includes('application/json')) {
    return res.json();
  }
  return res.text();
}

function normalizeTodoStatus(raw) {
  if (!raw) return 'pending';
  const s = String(raw).trim().toLowerCase();
  if (!s) return 'pending';
  const compact = s.replace(/[\s-]+/g, '_');
  if (compact === 'inprogress' || compact === 'in_progress') return 'in_progress';
  if (compact === 'done' || compact === 'complete' || compact === 'completed') return 'completed';
  if (compact === 'canceled' || compact === 'cancelled') return 'cancelled';
  if (compact === 'pending' || compact === 'todo') return 'pending';
  if (compact === 'idle') return 'pending';
  return compact;
}

function normalizeTodoPriority(raw) {
  if (raw === undefined || raw === null) return null;
  const s = String(raw).trim().toLowerCase();
  if (!s) return null;
  if (s === 'p0' || s === '0' || s === 'urgent') return 'high';
  if (s === 'p1' || s === '1') return 'high';
  if (s === 'p2' || s === '2') return 'medium';
  if (s === 'p3' || s === '3') return 'low';
  if (s === 'high' || s === 'medium' || s === 'low') return s;
  return s;
}

function normalizeTodo(todo) {
  const t = todo && typeof todo === 'object' ? todo : {};
  const content = t.content ?? t.text ?? t.title ?? '';
  return {
    content: typeof content === 'string' ? content : String(content ?? ''),
    status: normalizeTodoStatus(t.status ?? t.state),
    priority: normalizeTodoPriority(t.priority)
  };
}

function getSessionDirectory(sessionInfo) {
  return sessionInfo?.directory || sessionInfo?.project?.worktree || null;
}

function getProjectDisplayPath(sessionInfo) {
  return sessionInfo?.project?.worktree || sessionInfo?.projectWorktree || sessionInfo?.directory || null;
}

function normalizeDirectory(value) {
  if (!value) return null;
  const s = String(value).trim();
  if (!s) return null;
  // OpenCode's per-directory endpoints accept Windows paths, but the /session endpoint
  // directory filter only works reliably with forward slashes.
  return s.replace(/\\/g, '/').replace(/\/+$/g, '');
}

function parseTime(value) {
  if (!value) return null;
  try {
    const d = new Date(value);
    if (Number.isNaN(d.getTime())) return null;
    return d.toISOString();
  } catch {
    return null;
  }
}

function buildOpenCodeSessionUrl({ sessionId, directory }) {
  const sid = String(sessionId || '').trim();
  if (!sid) return null;

  const base = String(OPENCODE_URL || '').trim().replace(/\/+$/g, '');
  if (!base) return null;

  const dir = normalizeDirectory(directory);
  if (!dir) return `${base}/session/${encodeURIComponent(sid)}`;

  const workspaceSlug = Buffer
    .from(dir, 'utf8')
    .toString('base64')
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/g, '');

  return `${base}/${workspaceSlug}/session/${encodeURIComponent(sid)}`;
}

function getMessageRole(message) {
  return String(
    message?.info?.role
    || message?.role
    || message?.author?.role
    || ''
  ).trim().toLowerCase();
}

function extractTextFragment(value, depth = 0) {
  if (depth > 5 || value === null || value === undefined) return '';

  if (typeof value === 'string') return value.trim();
  if (typeof value === 'number' || typeof value === 'boolean') return String(value);

  if (Array.isArray(value)) {
    const parts = value
      .map(v => extractTextFragment(v, depth + 1))
      .filter(Boolean);
    return parts.join('\n').trim();
  }

  if (typeof value !== 'object') return '';

  const preferred = ['text', 'content', 'message', 'body', 'value', 'markdown'];
  for (const key of preferred) {
    const out = extractTextFragment(value[key], depth + 1);
    if (out) return out;
  }

  // Common OpenCode-like part shapes.
  if (Array.isArray(value.parts)) {
    const out = extractTextFragment(value.parts, depth + 1);
    if (out) return out;
  }

  if (value.type === 'text' && typeof value.text === 'string') {
    return value.text.trim();
  }

  return '';
}

function extractAssistantMessageText(message) {
  const candidates = [
    message?.content,
    message?.text,
    message?.message,
    message?.body,
    message?.output,
    message?.response,
    message?.parts,
    message?.data,
    message?.info?.content,
    message?.info?.text
  ];

  for (const candidate of candidates) {
    const out = extractTextFragment(candidate);
    if (out) return out;
  }

  return '';
}

function extractMessageCreatedAt(message) {
  const candidates = [
    message?.info?.time?.created,
    message?.time?.created,
    message?.createdAt,
    message?.timestamp
  ];

  for (const c of candidates) {
    const t = parseTime(c);
    if (t) return t;
  }

  return null;
}

function findLastAssistantMessage(messages) {
  const list = Array.isArray(messages) ? messages : [];
  for (let i = list.length - 1; i >= 0; i--) {
    const m = list[i];
    if (getMessageRole(m) !== 'assistant') continue;

    return {
      message: extractAssistantMessageText(m) || null,
      createdAt: extractMessageCreatedAt(m)
    };
  }

  return null;
}

async function getLastAssistantMessage(sessionId) {
  if (!sessionId) return null;

  const tailLimit = 400;
  const tail = await opencodeFetch(`/session/${encodeURIComponent(sessionId)}/message`, {
    query: { limit: tailLimit }
  });
  const tailArr = Array.isArray(tail) ? tail : [];

  const tailMatch = findLastAssistantMessage(tailArr);
  if (tailMatch) return tailMatch;
  if (tailArr.length < tailLimit) return null;

  const all = await opencodeFetch(`/session/${encodeURIComponent(sessionId)}/message`);
  const allArr = Array.isArray(all) ? all : [];
  return findLastAssistantMessage(allArr);
}

async function mapLimit(items, limit, fn) {
  const results = new Array(items.length);
  let idx = 0;
  const workers = new Array(Math.min(limit, items.length)).fill(null).map(async () => {
    while (true) {
      const current = idx++;
      if (current >= items.length) return;
      results[current] = await fn(items[current], current);
    }
  });
  await Promise.all(workers);
  return results;
}

async function listGlobalSessions(limitParam) {
  const now = Date.now();
  if (sessionsCache.data && (now - sessionsCache.ts) < SESSIONS_CACHE_TTL_MS) {
    return sessionsCache.data;
  }

  const limit = limitParam === 'all'
    ? MAX_ALL_SESSIONS
    : Math.max(1, Math.min(parseInt(limitParam || '20', 10) || 20, MAX_ALL_SESSIONS));

  // In this OpenCode build, archived sessions are only reliably detectable via
  // `time.archived` on the per-directory `/session` listing. Build the cross-project
  // list by enumerating projects and listing sessions per worktree.
  const projects = await listProjects();
  const projectWorktrees = (projects || [])
    .map(p => p?.worktree)
    .filter(Boolean)
    .filter(wt => String(wt) !== '/');

  const perDirLimit = limitParam === 'all'
    ? MAX_SESSIONS_PER_PROJECT
    : Math.max(120, Math.min(MAX_SESSIONS_PER_PROJECT, limit * 8));

  const perProjectSessions = await mapLimit(projectWorktrees, Math.min(REQUEST_CONCURRENCY, 4), async (worktree) => {
    const directory = normalizeDirectory(worktree);
    if (!directory) return [];
    return listSessionsForDirectory({ directory, projectWorktree: worktree, limit: perDirLimit });
  });

  let sessions = perProjectSessions.flat();

  sessions.sort((a, b) => {
    const at = new Date(a?.time?.updated || a?.time?.created || 0).getTime();
    const bt = new Date(b?.time?.updated || b?.time?.created || 0).getTime();
    return bt - at;
  });

  if (limitParam !== 'all') sessions = sessions.slice(0, limit);

  const byId = new Map();
  for (const s of sessions) {
    if (s && s.id) byId.set(s.id, s);
  }

  sessionsCache = { ts: now, data: sessions, byId };
  return sessions;
}

async function listProjects() {
  const now = Date.now();
  if (projectsCache.data && (now - projectsCache.ts) < PROJECTS_CACHE_TTL_MS) {
    return projectsCache.data;
  }

  const data = await opencodeFetch('/project');
  const projects = toArrayResponse(data);
  const byWorktree = new Map();
  for (const p of projects) {
    const wt = p?.worktree;
    if (wt) byWorktree.set(String(wt), p);
  }
  projectsCache = { ts: now, data: projects, byWorktree };
  return projects;
}

async function listSessionsForDirectory({ directory, projectWorktree, limit }) {
  const dir = normalizeDirectory(directory);
  if (!dir) return [];

  const now = Date.now();
  const cached = sessionsCacheByDirectory.get(dir);
  if (cached && (now - cached.ts) < DIRECTORY_SESSIONS_CACHE_TTL_MS) {
    return cached.data;
  }

  const data = await opencodeFetch('/session', {
    query: {
      roots: 'true',
      limit: Math.max(1, Math.min(parseInt(String(limit || '200'), 10) || 200, MAX_SESSIONS_PER_PROJECT))
    },
    directory: dir
  });

  const sessions = toArrayResponse(data)
    .filter(s => s && s.id)
    .filter(s => !s?.time?.archived)
    .map(s => ({
      ...s,
      directory: dir,
      projectWorktree: projectWorktree || null,
      project: projectWorktree ? { worktree: projectWorktree } : s.project
    }));

  sessionsCacheByDirectory.set(dir, { ts: now, data: sessions });
  return sessions;
}

async function getStatusMapForDirectory(directory) {
  if (!directory) return {};
  const now = Date.now();
  const cached = statusCacheByDirectory.get(directory);
  if (cached && (now - cached.ts) < STATUS_CACHE_TTL_MS) {
    return cached.data;
  }

  let statusMap = {};
  try {
    statusMap = await opencodeFetch('/session/status', { directory });
  } catch (e) {
    // Treat as empty (everything idle) if status fetch fails.
    statusMap = {};
  }

  statusCacheByDirectory.set(directory, { ts: now, data: statusMap || {} });
  return statusMap || {};
}

async function getTodosForSession({ sessionId, directory }) {
  if (!sessionId) return [];
  const cacheKey = `${directory || ''}::${sessionId}`;
  const now = Date.now();
  const cached = todoCache.get(cacheKey);
  if (cached && (now - cached.ts) < TODO_CACHE_TTL_MS) {
    return cached.data;
  }

  const data = await opencodeFetch(`/session/${encodeURIComponent(sessionId)}/todo`, {
    directory
  });
  const rawTodos = Array.isArray(data)
    ? data
    : (Array.isArray(data?.todos)
      ? data.todos
      : (Array.isArray(data?.items) ? data.items : []));
  const todos = rawTodos.map(normalizeTodo);
  todoCache.set(cacheKey, { ts: now, data: todos });
  return todos;
}

function normalizeRuntimeStatus({ directory, sessionId, statusMap }) {
  const overrideKey = `${directory || ''}::${sessionId}`;
  const override = statusOverride.get(overrideKey);
  if (override && (Date.now() - override.ts) < 60_000) {
    return { type: override.type || 'idle' };
  }

  const s = statusMap?.[sessionId];
  if (!s || !s.type) return { type: 'idle' };
  return { type: s.type };
}

function countTodos(todos) {
  const counts = { pending: 0, inProgress: 0, completed: 0, cancelled: 0 };
  for (const t of todos || []) {
    if (!t || !t.status) continue;
    if (t.status === 'pending') counts.pending++;
    else if (t.status === 'in_progress') counts.inProgress++;
    else if (t.status === 'completed') counts.completed++;
    else if (t.status === 'cancelled') counts.cancelled++;
  }
  return counts;
}

function isRuntimeRunning(type) {
  if (!type) return false;
  const t = String(type).trim().toLowerCase();
  return t === 'busy' || t === 'retry' || t === 'running';
}

function deriveSessionKanbanStatus({ runtimeStatus, modifiedAt, hasAssistantResponse }) {
  if (isRuntimeRunning(runtimeStatus?.type)) return 'in_progress';

  if (hasAssistantResponse === true) return 'completed';
  if (hasAssistantResponse === false) return 'pending';

  const ts = modifiedAt ? new Date(modifiedAt).getTime() : NaN;
  if (!Number.isFinite(ts)) return 'pending';

  const ageMs = Date.now() - ts;
  if (ageMs <= SESSION_RECENT_WINDOW_MS) return 'pending';
  return 'completed';
}

async function getHasAssistantResponse(sessionId) {
  if (!sessionId) return null;
  const cached = assistantPresenceCache.get(sessionId);
  const now = Date.now();
  if (cached && (now - cached.ts) < MESSAGE_PRESENCE_CACHE_TTL_MS) {
    return cached.hasAssistant;
  }

  const inflight = assistantPresenceInFlight.get(sessionId);
  if (inflight) return inflight;

  const promise = (async () => {
    try {
      // Fast path: look at the last N messages.
      const tailLimit = 200;
      const tail = await opencodeFetch(`/session/${encodeURIComponent(sessionId)}/message`, {
        query: { limit: tailLimit }
      });
      const tailArr = Array.isArray(tail) ? tail : [];
      const hasAssistantInTail = tailArr.some(m => String(m?.info?.role || '').toLowerCase() === 'assistant');
      if (hasAssistantInTail) return true;

      // If the server returned fewer than tailLimit messages, we already saw the whole history.
      if (tailArr.length < tailLimit) return false;

      // Slow path: fetch full message history to be certain.
      const all = await opencodeFetch(`/session/${encodeURIComponent(sessionId)}/message`);
      const allArr = Array.isArray(all) ? all : [];
      return allArr.some(m => String(m?.info?.role || '').toLowerCase() === 'assistant');
    } catch {
      return null;
    }
  })();

  assistantPresenceInFlight.set(sessionId, promise);
  const hasAssistant = await promise;
  assistantPresenceInFlight.delete(sessionId);
  if (hasAssistant !== null) {
    assistantPresenceCache.set(sessionId, { ts: Date.now(), hasAssistant });
  }
  return hasAssistant;
}

// OpenCode sometimes keeps todos as `pending` even while the session is actively running.
// To keep the board useful, infer a single in-progress todo when the session runtime is busy.
function inferInProgressTodoFromRuntime(todos, runtimeStatus) {
  const list = Array.isArray(todos) ? todos : [];
  if (!isRuntimeRunning(runtimeStatus?.type)) return list;

  // If OpenCode already marks something in progress, don't override.
  if (list.some(t => t && t.status === 'in_progress')) return list;

  const firstPendingIdx = list.findIndex(t => t && t.status === 'pending');
  if (firstPendingIdx === -1) return list;

  // Avoid mutating cached todo objects.
  const copy = list.map(t => ({ ...t }));
  copy[firstPendingIdx].status = 'in_progress';
  return copy;
}

function mapTodosToViewerTasks(todos) {
  const tasks = [];
  const list = Array.isArray(todos) ? todos : [];
  for (let i = 0; i < list.length; i++) {
    const todo = list[i] || {};
    tasks.push({
      id: String(i + 1),
      subject: todo.content || '',
      status: todo.status || 'pending',
      priority: todo.priority || null
    });
  }
  return tasks;
}

// API: List sessions (cross-project) with derived kanban status
app.get('/api/sessions', async (req, res) => {
  res.setHeader('Cache-Control', 'no-store, no-cache, must-revalidate, private');
  res.setHeader('Pragma', 'no-cache');
  res.setHeader('Expires', '0');

  try {
    const limitParam = req.query.limit || '20';
    const globalSessions = await listGlobalSessions(limitParam);

    const directories = Array.from(new Set(globalSessions.map(getSessionDirectory).filter(Boolean)));
    const statusMaps = await mapLimit(directories, Math.min(REQUEST_CONCURRENCY, 4), async (dir) => {
      const m = await getStatusMapForDirectory(dir);
      return [dir, m];
    });
    const statusByDir = new Map(statusMaps);

    const summaries = await mapLimit(globalSessions, REQUEST_CONCURRENCY, async (session) => {
      const sessionId = session?.id;
      const directory = getSessionDirectory(session);
      const statusMap = statusByDir.get(directory) || {};
      const runtimeStatus = normalizeRuntimeStatus({ directory, sessionId, statusMap });

      const createdAt = parseTime(session?.time?.created) || null;
      const modifiedAt = parseTime(session?.time?.updated) || createdAt || new Date().toISOString();
      const hasAssistantResponse = isRuntimeRunning(runtimeStatus?.type)
        ? null
        : await getHasAssistantResponse(sessionId);
      const status = deriveSessionKanbanStatus({ runtimeStatus, modifiedAt, hasAssistantResponse });

      return {
        id: sessionId,
        name: session?.title || session?.name || null,
        project: getProjectDisplayPath(session),
        description: null,
        gitBranch: null,
        createdAt,
        modifiedAt,
        runtimeStatus,
        status,
        hasAssistantResponse,
        openCodeUrl: buildOpenCodeSessionUrl({ sessionId, directory })
      };
    });

    // Sort newest first (OpenCode usually already does, but keep deterministic)
    summaries.sort((a, b) => new Date(b.modifiedAt) - new Date(a.modifiedAt));

    res.json(summaries);
  } catch (error) {
    console.error('Error listing sessions:', error);
    res.status(502).json({ error: 'Failed to list sessions from OpenCode' });
  }
});

async function findSessionInfo(sessionId) {
  if (!sessionId) return null;
  if (sessionsCache.byId && sessionsCache.byId.has(sessionId)) {
    return sessionsCache.byId.get(sessionId);
  }

  // Best-effort: refresh the global session list and try again.
  try {
    await listGlobalSessions('200');
    if (sessionsCache.byId && sessionsCache.byId.has(sessionId)) {
      return sessionsCache.byId.get(sessionId);
    }
  } catch {
    // Ignore
  }

  return sessionsCache.byId?.get(sessionId) || null;
}

async function archiveSessionOnOpenCode({ sessionId, directory }) {
  const now = Date.now();
  const sid = String(sessionId || '').trim();
  if (!sid) throw new Error('Missing sessionId');

  const attempts = [
    async () => opencodeFetch(`/session/${encodeURIComponent(sid)}`, {
      method: 'PATCH',
      directory,
      json: { time: { archived: now } }
    }),
    async () => opencodeFetch(`/session/${encodeURIComponent(sid)}`, {
      method: 'PATCH',
      directory,
      json: { archived: true }
    }),
    async () => opencodeFetch(`/session/${encodeURIComponent(sid)}/archive`, {
      method: 'POST',
      directory
    })
  ];

  let lastErr = null;
  for (const attempt of attempts) {
    try {
      await attempt();
      const updated = await opencodeFetch(`/session/${encodeURIComponent(sid)}`, { directory });
      if (updated && updated.time && updated.time.archived) {
        return { archivedAt: updated.time.archived };
      }
      lastErr = new Error('Archive request succeeded but session did not report archived time');
    } catch (e) {
      lastErr = e;
    }
  }

  throw lastErr || new Error('Failed to archive session');
}

// API: Get tasks (todos) for a session
app.get('/api/sessions/:sessionId', async (req, res) => {
  try {
    const sessionId = req.params.sessionId;
    const info = await findSessionInfo(sessionId);
    if (!info) {
      return res.status(404).json({ error: 'Session not found' });
    }

    const directory = getSessionDirectory(info);
    const todos = await getTodosForSession({ sessionId, directory });
    const tasks = mapTodosToViewerTasks(todos);
    res.json(tasks);
  } catch (error) {
    console.error('Error getting session todos:', error);
    res.status(502).json({ error: 'Failed to load session todos from OpenCode' });
  }
});

// API: Get the last assistant message for a session
app.get('/api/sessions/:sessionId/last-assistant-message', async (req, res) => {
  try {
    const sessionId = req.params.sessionId;
    const info = await findSessionInfo(sessionId);
    if (!info) {
      return res.status(404).json({ error: 'Session not found' });
    }

    const last = await getLastAssistantMessage(sessionId);
    res.json({
      sessionId,
      message: last?.message || null,
      createdAt: last?.createdAt || null
    });
  } catch (error) {
    console.error('Error getting last assistant message:', error);
    res.status(502).json({ error: 'Failed to load session messages from OpenCode' });
  }
});

// API: Archive a session
app.post('/api/sessions/:sessionId/archive', async (req, res) => {
  try {
    const sessionId = req.params.sessionId;
    const info = await findSessionInfo(sessionId);
    if (!info) return res.status(404).json({ error: 'Session not found' });

    const directory = getSessionDirectory(info);
    const result = await archiveSessionOnOpenCode({ sessionId, directory });
    invalidateAllCaches();
    broadcast({ type: 'update' });
    res.json({ ok: true, archivedAt: result?.archivedAt || null });
  } catch (error) {
    console.error('Error archiving session:', error);
    res.status(502).json({ error: 'Failed to archive session in OpenCode' });
  }
});

// API: Get all tasks across sessions currently known (used for Live Updates + search)
app.get('/api/tasks/all', async (req, res) => {
  res.setHeader('Cache-Control', 'no-store, no-cache, must-revalidate, private');
  res.setHeader('Pragma', 'no-cache');
  res.setHeader('Expires', '0');

  try {
    const now = Date.now();
    if (tasksAllCache.data && (now - tasksAllCache.ts) < TASKS_ALL_CACHE_TTL_MS) {
      return res.json(tasksAllCache.data);
    }

    // Prefer the cached session list (populated by /api/sessions). If the UI never
    // calls /api/sessions (e.g. no sidebar), we still want a complete board.
    const sessions = sessionsCache.data || await listGlobalSessions('all');

    const directories = Array.from(new Set(sessions.map(getSessionDirectory).filter(Boolean)));
    const statusMaps = await mapLimit(directories, Math.min(REQUEST_CONCURRENCY, 4), async (dir) => {
      const m = await getStatusMapForDirectory(dir);
      return [dir, m];
    });
    const statusByDir = new Map(statusMaps);

    const allTasks = [];
    await mapLimit(sessions, REQUEST_CONCURRENCY, async (session) => {
      const sessionId = session?.id;
      if (!sessionId) return;

      const directory = getSessionDirectory(session);
      const statusMap = statusByDir.get(directory) || {};
      const runtimeStatus = normalizeRuntimeStatus({ directory, sessionId, statusMap });

      let todos = [];
      try {
        todos = await getTodosForSession({ sessionId, directory });
      } catch {
        todos = [];
      }

      const inferredTodos = inferInProgressTodoFromRuntime(todos, runtimeStatus);

      for (let i = 0; i < (inferredTodos || []).length; i++) {
        const todo = inferredTodos[i] || {};
        allTasks.push({
          id: String(i + 1),
          subject: todo.content || '',
          status: todo.status || 'pending',
          priority: todo.priority || null,
          sessionId,
          sessionName: session?.title || session?.name || null,
          project: getProjectDisplayPath(session)
        });
      }
    });

    tasksAllCache = { ts: now, data: allTasks };
    res.json(allTasks);
  } catch (error) {
    console.error('Error getting all tasks:', error);
    res.status(502).json({ error: 'Failed to load tasks from OpenCode' });
  }
});

// Claude-only mutations are not supported in OpenCode viewer
app.post('/api/tasks/:sessionId/:taskId/note', (req, res) => {
  res.status(501).json({ error: 'Not implemented for OpenCode todos' });
});

app.delete('/api/tasks/:sessionId/:taskId', (req, res) => {
  res.status(501).json({ error: 'Not implemented for OpenCode todos' });
});

// SSE endpoint for live updates (proxied from OpenCode global event stream)
app.get('/api/events', (req, res) => {
  res.setHeader('Content-Type', 'text/event-stream');
  res.setHeader('Cache-Control', 'no-cache');
  res.setHeader('Connection', 'keep-alive');
  res.flushHeaders?.();

  clients.add(res);
  req.on('close', () => {
    clients.delete(res);
  });

  res.write('data: {"type":"connected"}\n\n');
});

function broadcast(data) {
  const message = `data: ${JSON.stringify(data)}\n\n`;
  for (const client of clients) {
    try {
      client.write(message);
    } catch {
      // Ignore broken connections
    }
  }
}

function invalidateAllCaches() {
  sessionsCache.ts = 0;
  tasksAllCache.ts = 0;
  todoCache.clear();
  projectsCache.ts = 0;
  projectsCache.data = null;
  projectsCache.byWorktree = new Map();
  sessionsCacheByDirectory.clear();
  statusCacheByDirectory.clear();
  statusOverride.clear();
  assistantPresenceCache.clear();
  assistantPresenceInFlight.clear();
}

function invalidateTodos(directory, sessionId) {
  tasksAllCache.ts = 0;
  const key = `${directory || ''}::${sessionId}`;
  todoCache.delete(key);
}

function noteStatusOverride(directory, sessionId, type) {
  const key = `${directory || ''}::${sessionId}`;
  statusOverride.set(key, { type, ts: Date.now() });
}

async function startUpstreamSse() {
  const auth = getBasicAuthHeader();
  const url = new URL('/global/event', OPENCODE_URL);

  let retryDelay = 1000;

  async function connect() {
    try {
      const headers = { 'Accept': 'text/event-stream' };
      if (auth) headers['Authorization'] = auth;
      const res = await fetch(url, { headers });
      if (!res.ok || !res.body) {
        throw new Error(`Upstream SSE failed: ${res.status} ${res.statusText}`);
      }

      retryDelay = 1000;
      let buffer = '';
      const reader = res.body.getReader();
      while (true) {
        const { value, done } = await reader.read();
        if (done) break;
        buffer += Buffer.from(value).toString('utf8');
        // Normalize CRLF to LF for SSE framing.
        buffer = buffer.replace(/\r\n/g, '\n');

        let idx;
        while ((idx = buffer.indexOf('\n\n')) !== -1) {
          const raw = buffer.slice(0, idx);
          buffer = buffer.slice(idx + 2);
          const dataLines = raw.split('\n').filter(l => l.startsWith('data:'));
          if (dataLines.length === 0) continue;
          const dataStr = dataLines.map(l => l.slice(5).trim()).join('\n');
          if (!dataStr) continue;
          let evt;
          try {
            evt = JSON.parse(dataStr);
          } catch {
            continue;
          }

          handleUpstreamEvent(evt);
        }
      }
    } catch (e) {
      // Reconnect
      setTimeout(connect, retryDelay);
      retryDelay = Math.min(retryDelay * 2, 30000);
    }
  }

  function handleUpstreamEvent(evt) {
    const directory = normalizeDirectory(evt?.directory) || evt?.directory || null;
    const type = evt?.payload?.type || null;
    const props = evt?.payload?.properties || {};

    if (!type) return;

    if (type === 'todo.updated') {
      const sessionId = props.sessionID || props.sessionId;
      if (sessionId) {
        invalidateTodos(directory, sessionId);
        broadcast({ type: 'update', sessionId });
      } else {
        invalidateAllCaches();
        broadcast({ type: 'update' });
      }
      return;
    }

    if (type === 'session.status') {
      const sessionId = props.sessionID || props.sessionId;
      const statusType = props.status?.type || props.type;
      if (sessionId && statusType) {
        noteStatusOverride(directory, sessionId, statusType);
        sessionsCache.ts = 0;
        tasksAllCache.ts = 0;
        broadcast({ type: 'update', sessionId });
      } else {
        broadcast({ type: 'update' });
      }
      return;
    }

    if (type === 'session.created' || type === 'session.updated' || type === 'session.deleted') {
      invalidateAllCaches();
      broadcast({ type: 'update' });
      return;
    }

    if (type.startsWith('message.')) {
      // Message activity can flip `hasAssistantResponse` from false -> true.
      // The message events don't always include a stable session id; clear our cached presence.
      assistantPresenceCache.clear();
      sessionsCache.ts = 0;
      broadcast({ type: 'update' });
      return;
    }
  }

  connect();
}

// Start server with auto port discovery
function startServer(port, attempt = 0) {
  const server = app.listen(port, explicitHost, () => {
    const actualPort = server.address()?.port || port;
    const viewerUrl = `http://${explicitHost}:${actualPort}`;
    console.log(`OpenCode Task Viewer running at ${viewerUrl}`);
    console.log(`VIEWER_URL=${viewerUrl}`);
    console.log(`Using OpenCode server: ${OPENCODE_URL}`);

    if (process.argv.includes('--open')) {
      import('open').then(open => open.default(viewerUrl));
    }
  });

  server.on('error', (err) => {
    if (err.code === 'EADDRINUSE' && !explicitPort && attempt < MAX_PORT_ATTEMPTS) {
      console.log(`Port ${port} is in use, trying ${port + 1}...`);
      startServer(port + 1, attempt + 1);
    } else {
      console.error(`Failed to start server on ${explicitHost}:${port}: ${err.message}`);
      process.exit(1);
    }
  });
}

startUpstreamSse();
startServer(explicitPort || DEFAULT_PORT);
