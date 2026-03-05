#!/usr/bin/env node

const http = require('http');
const { URL } = require('url');

const HOST = process.env.HOST || '127.0.0.1';
const PORT = process.env.PORT ? parseInt(process.env.PORT, 10) : 0;

function nowIso() {
  return new Date().toISOString();
}

function buildDefaultState() {
  const now = Date.now();
  const iso = (ms) => new Date(ms).toISOString();
  const baseCreated = iso(now - 60 * 60 * 1000);

  const alphaWorktree = 'C:\\Work\\Alpha';
  const betaWorktree = 'C:\\Work\\Beta';
  const gammaWorktree = 'C:\\Work\\Gamma';

  const alphaDir = 'C:/Work/Alpha';
  const betaDir = 'C:/Work/Beta';
  const gammaDir = 'C:/Work/Gamma';

  return {
    projects: [
      { id: 'global', worktree: '/', time: { created: baseCreated, updated: iso(now - 5 * 1000) } },
      { id: 'p-alpha', worktree: alphaWorktree, time: { created: baseCreated, updated: iso(now - 5 * 1000) } },
      { id: 'p-beta', worktree: betaWorktree, time: { created: baseCreated, updated: iso(now - 5 * 1000) } },
      { id: 'p-gamma', worktree: gammaWorktree, time: { created: baseCreated, updated: iso(now - 5 * 1000) } }
    ],
    sessions: [
      {
        id: 'sess-busy',
        title: 'Busy Session',
        directory: betaDir,
        project: { worktree: betaWorktree },
        time: { created: baseCreated, updated: iso(now - 20 * 1000) }
      },
      {
        id: 'sess-retry',
        title: 'Retrying Session',
        directory: alphaDir,
        project: { worktree: alphaWorktree },
        time: { created: baseCreated, updated: iso(now - 45 * 1000) }
      },
      {
        id: 'sess-recent',
        title: 'Recently Updated',
        directory: gammaDir,
        project: { worktree: gammaWorktree },
        time: { created: baseCreated, updated: iso(now - 2 * 60 * 1000) }
      },
      {
        id: 'sess-stale',
        title: 'Stale Session',
        directory: gammaDir,
        project: { worktree: gammaWorktree },
        time: { created: baseCreated, updated: iso(now - 10 * 60 * 1000) }
      },
      {
        id: 'sess-archived',
        title: 'Archived Session (Should Not Show)',
        directory: gammaDir,
        project: { worktree: gammaWorktree },
        time: { created: baseCreated, updated: iso(now - 30 * 60 * 1000), archived: now - 25 * 60 * 1000 }
      }
    ],
    todosBySessionId: {
      // Todos are currently unused by the viewer UI, but keep endpoint behavior.
      'sess-busy': [],
      'sess-retry': [],
      'sess-recent': [],
      'sess-stale': []
    },
    messagesBySessionId: {
      // Chronological (oldest -> newest). Viewer uses this to detect if an assistant ever responded.
      'sess-busy': [
        { info: { id: 'm1', role: 'user', time: { created: now - 30_000 } }, content: [{ type: 'text', text: 'Run the worker.' }] },
        { info: { id: 'm2', role: 'assistant', time: { created: now - 29_000 } }, content: [{ type: 'text', text: 'Worker is running now.' }] }
      ],
      'sess-retry': [
        { info: { id: 'm3', role: 'user', time: { created: now - 60_000 } }, text: 'Try the migration again.' },
        { info: { id: 'm4', role: 'assistant', time: { created: now - 59_000 } }, text: 'Retrying migration with backoff.' }
      ],
      // No assistant response yet
      'sess-recent': [
        { info: { id: 'm5', role: 'user', time: { created: now - 120_000 } }, text: 'Can you inspect this issue?' }
      ],
      // Assistant responded at least once
      'sess-stale': [
        { info: { id: 'm6', role: 'user', time: { created: now - 3_600_000 } }, text: 'Please summarize the diagnostics.' },
        { info: { id: 'm7', role: 'assistant', time: { created: now - 3_599_000 } }, text: 'Diagnostics complete; all checks passed.' }
      ]
    },
    statusByDirectory: {
      [alphaDir]: { 'sess-retry': { type: 'retry' } },
      [betaDir]: { 'sess-busy': { type: 'busy' } },
      [gammaDir]: {}
    }
  };
}

let state = buildDefaultState();

/** @type {Set<import('http').ServerResponse>} */
const sseClients = new Set();

function normalizeDir(value) {
  if (!value) return '';
  return String(value).trim().replace(/\\/g, '/').replace(/\/+$/g, '');
}

function writeJson(res, statusCode, body) {
  const json = JSON.stringify(body);
  res.writeHead(statusCode, {
    'Content-Type': 'application/json; charset=utf-8',
    'Cache-Control': 'no-store'
  });
  res.end(json);
}

function notFound(res) {
  writeJson(res, 404, { error: 'Not found' });
}

function readBody(req) {
  return new Promise((resolve, reject) => {
    let data = '';
    req.on('data', (chunk) => {
      data += chunk;
      if (data.length > 2_000_000) {
        reject(new Error('Body too large'));
        req.destroy();
      }
    });
    req.on('end', () => {
      if (!data) return resolve(null);
      try {
        resolve(JSON.parse(data));
      } catch (e) {
        reject(e);
      }
    });
    req.on('error', reject);
  });
}

function getSessionById(sessionId) {
  return (state.sessions || []).find(s => s && s.id === sessionId) || null;
}

function broadcast(evt) {
  const payload = `data: ${JSON.stringify(evt)}\n\n`;
  for (const client of sseClients) {
    try {
      client.write(payload);
    } catch {
      // Ignore broken clients
    }
  }
}

function updateSessionTime(sessionId) {
  const s = state.sessions.find(x => x.id === sessionId);
  if (!s) return;
  s.time = s.time || {};
  s.time.updated = nowIso();
}

const server = http.createServer(async (req, res) => {
  try {
    const url = new URL(req.url, `http://${req.headers.host}`);
    const pathname = url.pathname;

    // --- Test-only endpoints ---
    if (pathname === '/__test__/health' && req.method === 'GET') {
      return writeJson(res, 200, { ok: true });
    }

    if (pathname === '/__test__/reset' && req.method === 'POST') {
      state = buildDefaultState();
      return writeJson(res, 200, { ok: true });
    }

    if (pathname === '/__test__/setTodos' && req.method === 'POST') {
      const body = await readBody(req);
      const sessionId = body?.sessionId;
      const todos = body?.todos;
      if (!sessionId || !Array.isArray(todos)) {
        return writeJson(res, 400, { error: 'Expected { sessionId, todos: [] }' });
      }
      state.todosBySessionId[sessionId] = todos;
      updateSessionTime(sessionId);
      return writeJson(res, 200, { ok: true });
    }

    if (pathname === '/__test__/setStatus' && req.method === 'POST') {
      const body = await readBody(req);
      const directory = normalizeDir(body?.directory);
      const sessionId = body?.sessionId;
      const type = body?.type;
      if (!directory || !sessionId || !type) {
        return writeJson(res, 400, { error: 'Expected { directory, sessionId, type }' });
      }
      state.statusByDirectory[directory] = state.statusByDirectory[directory] || {};
      state.statusByDirectory[directory][sessionId] = { type };
      return writeJson(res, 200, { ok: true });
    }

    if (pathname === '/__test__/emit' && req.method === 'POST') {
      const body = await readBody(req);
      const directory = body?.directory;
      const type = body?.type;
      const properties = body?.properties || {};
      if (!directory || !type) {
        return writeJson(res, 400, { error: 'Expected { directory, type, properties? }' });
      }
      broadcast({ directory, payload: { type, properties } });
      return writeJson(res, 200, { ok: true });
    }

    // --- OpenCode-like endpoints used by the viewer ---
    if (pathname === '/project' && req.method === 'GET') {
      return writeJson(res, 200, state.projects || []);
    }

    if (pathname === '/session' && req.method === 'GET') {
      const directory = normalizeDir(url.searchParams.get('directory'));
      const limit = url.searchParams.get('limit');

      let sessions = (state.sessions || []).slice();
      if (directory) {
        sessions = sessions.filter(s => normalizeDir(s?.directory) === directory);
      }

      sessions.sort((a, b) => {
        const at = new Date(a?.time?.updated || a?.time?.created || 0).getTime();
        const bt = new Date(b?.time?.updated || b?.time?.created || 0).getTime();
        return bt - at;
      });

      if (limit) {
        const n = parseInt(limit, 10);
        if (!Number.isNaN(n) && n > 0) sessions = sessions.slice(0, n);
      }

      return writeJson(res, 200, sessions);
    }

    if (pathname === '/experimental/session' && req.method === 'GET') {
      const limit = url.searchParams.get('limit');
      const search = url.searchParams.get('search');

      let sessions = state.sessions.slice();
      // Sort newest first
      sessions.sort((a, b) => {
        const at = new Date(a?.time?.updated || a?.time?.created || 0).getTime();
        const bt = new Date(b?.time?.updated || b?.time?.created || 0).getTime();
        return bt - at;
      });

      if (search) {
        const q = search.toLowerCase();
        sessions = sessions.filter(s => (s.id || '').toLowerCase().includes(q) || (s.title || '').toLowerCase().includes(q));
      }

      if (limit) {
        const n = parseInt(limit, 10);
        if (!Number.isNaN(n) && n > 0) sessions = sessions.slice(0, n);
      }

      return writeJson(res, 200, sessions);
    }

    if (pathname === '/session/status' && req.method === 'GET') {
      const directory = normalizeDir(url.searchParams.get('directory'));
      if (!directory) return writeJson(res, 400, { error: 'Missing directory' });
      return writeJson(res, 200, state.statusByDirectory[directory] || {});
    }

    if (pathname.startsWith('/session/') && req.method === 'GET' && !pathname.endsWith('/todo') && !pathname.endsWith('/message')) {
      const parts = pathname.split('/').filter(Boolean);
      const sessionId = parts[1];
      if (!sessionId) return writeJson(res, 400, { error: 'Missing session id' });
      const s = getSessionById(sessionId);
      if (!s) return writeJson(res, 404, { error: 'Session not found' });
      return writeJson(res, 200, s);
    }

    if (pathname.startsWith('/session/') && pathname.endsWith('/todo') && req.method === 'GET') {
      const parts = pathname.split('/').filter(Boolean);
      const sessionId = parts[1];
      if (!sessionId) return writeJson(res, 400, { error: 'Missing session id' });
      const todos = state.todosBySessionId[sessionId] || [];
      return writeJson(res, 200, todos);
    }

    if (pathname.startsWith('/session/') && req.method === 'PATCH') {
      const parts = pathname.split('/').filter(Boolean);
      const sessionId = parts[1];
      if (!sessionId) return writeJson(res, 400, { error: 'Missing session id' });
      const s = getSessionById(sessionId);
      if (!s) return writeJson(res, 404, { error: 'Session not found' });
      const body = await readBody(req);
      const now = Date.now();
      s.time = s.time || {};
      if (body?.time?.archived) {
        s.time.archived = body.time.archived;
      } else if (body?.archived === true) {
        s.time.archived = now;
      }
      s.time.updated = nowIso();
      return writeJson(res, 200, s);
    }

    if (pathname.startsWith('/session/') && pathname.endsWith('/message') && req.method === 'GET') {
      const parts = pathname.split('/').filter(Boolean);
      const sessionId = parts[1];
      if (!sessionId) return writeJson(res, 400, { error: 'Missing session id' });
      const messages = state.messagesBySessionId[sessionId] || [];
      const limit = url.searchParams.get('limit');
      let result = messages;
      if (limit) {
        const n = parseInt(limit, 10);
        if (!Number.isNaN(n) && n > 0) result = messages.slice(-n);
      }
      return writeJson(res, 200, result);
    }

    if (pathname === '/global/event' && req.method === 'GET') {
      res.writeHead(200, {
        'Content-Type': 'text/event-stream; charset=utf-8',
        'Cache-Control': 'no-cache',
        'Connection': 'keep-alive'
      });
      res.write(': connected\n\n');
      sseClients.add(res);
      req.on('close', () => {
        sseClients.delete(res);
      });
      return;
    }

    return notFound(res);
  } catch (e) {
    return writeJson(res, 500, { error: String(e?.message || e) });
  }
});

server.listen(PORT, HOST, () => {
  const addr = server.address();
  const port = typeof addr === 'object' && addr ? addr.port : PORT;
  const baseUrl = `http://${HOST}:${port}`;
  console.log(`Mock OpenCode listening on ${baseUrl}`);
  console.log(`MOCK_OPENCODE_URL=${baseUrl}`);
});
