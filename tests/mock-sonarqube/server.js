#!/usr/bin/env node

const http = require('http');
const { URL } = require('url');

const HOST = process.env.HOST || '127.0.0.1';
const PORT = process.env.PORT ? parseInt(process.env.PORT, 10) : 0;

function writeJson(res, statusCode, body) {
  res.writeHead(statusCode, {
    'Content-Type': 'application/json; charset=utf-8',
    'Cache-Control': 'no-store'
  });
  res.end(JSON.stringify(body));
}

function notFound(res) {
  writeJson(res, 404, { error: 'Not found' });
}

function buildDefaultState() {
  return {
    issues: [
      {
        key: 'sq-gamma-001',
        component: 'gamma-key:src/worker.js',
        line: 42,
        rule: 'javascript:S1126',
        severity: 'MAJOR',
        type: 'CODE_SMELL',
        status: 'OPEN',
        message: 'Remove this redundant assignment.'
      },
      {
        key: 'sq-gamma-002',
        component: 'gamma-key:src/server.js',
        line: 17,
        rule: 'javascript:S3776',
        severity: 'CRITICAL',
        type: 'CODE_SMELL',
        status: 'CONFIRMED',
        message: 'Refactor this function to reduce Cognitive Complexity.'
      },
      {
        key: 'sq-gamma-003',
        component: 'gamma-key:src/auth.js',
        line: 10,
        rule: 'javascript:S5144',
        severity: 'BLOCKER',
        type: 'VULNERABILITY',
        status: 'OPEN',
        message: 'Review this URL construction for SSRF risk.'
      },
      {
        key: 'sq-alpha-001',
        component: 'alpha-key:src/index.js',
        line: 7,
        rule: 'javascript:S1481',
        severity: 'MINOR',
        type: 'CODE_SMELL',
        status: 'OPEN',
        message: 'Remove this unused local variable.'
      }
    ]
  };
}

let state = buildDefaultState();

const server = http.createServer((req, res) => {
  try {
    const url = new URL(req.url, `http://${req.headers.host}`);
    const pathname = url.pathname;

    if (pathname === '/__test__/health' && req.method === 'GET') {
      return writeJson(res, 200, { ok: true });
    }

    if (pathname === '/__test__/reset' && req.method === 'POST') {
      state = buildDefaultState();
      return writeJson(res, 200, { ok: true });
    }

    if (pathname === '/api/issues/search' && req.method === 'GET') {
      const componentKeys = String(url.searchParams.get('componentKeys') || '').trim();
      const typesRaw = String(url.searchParams.get('types') || '').trim();
      const statusesRaw = String(url.searchParams.get('statuses') || '').trim();

      const pageIndex = Math.max(1, parseInt(String(url.searchParams.get('p') || '1'), 10) || 1);
      const pageSize = Math.max(1, Math.min(500, parseInt(String(url.searchParams.get('ps') || '100'), 10) || 100));

      const typeSet = new Set(typesRaw
        ? typesRaw.split(',').map(x => x.trim().toUpperCase()).filter(Boolean)
        : []);

      const statusSet = new Set(statusesRaw
        ? statusesRaw.split(',').map(x => x.trim().toUpperCase()).filter(Boolean)
        : []);

      let issues = state.issues.slice();
      if (componentKeys) {
        const keys = new Set(componentKeys.split(',').map(x => x.trim()).filter(Boolean));
        issues = issues.filter(issue => {
          const raw = String(issue.component || '');
          const idx = raw.indexOf(':');
          const key = idx > -1 ? raw.slice(0, idx) : raw;
          return keys.has(key);
        });
      }

      if (typeSet.size > 0) {
        issues = issues.filter(issue => typeSet.has(String(issue.type || '').toUpperCase()));
      }

      if (statusSet.size > 0) {
        issues = issues.filter(issue => statusSet.has(String(issue.status || '').toUpperCase()));
      }

      const total = issues.length;
      const start = (pageIndex - 1) * pageSize;
      const paged = issues.slice(start, start + pageSize);

      return writeJson(res, 200, {
        total,
        p: pageIndex,
        ps: pageSize,
        paging: {
          pageIndex,
          pageSize,
          total
        },
        issues: paged
      });
    }

    return notFound(res);
  } catch (error) {
    return writeJson(res, 500, { error: String(error?.message || error) });
  }
});

server.listen(PORT, HOST, () => {
  const addr = server.address();
  const port = typeof addr === 'object' && addr ? addr.port : PORT;
  const baseUrl = `http://${HOST}:${port}`;
  console.log(`Mock SonarQube listening on ${baseUrl}`);
  console.log(`MOCK_SONAR_URL=${baseUrl}`);
});
