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
    rules: {
      'javascript:S1126': 'Assignments should not be redundant',
      'javascript:S3776': 'Cognitive Complexity of functions should not be too high',
      'javascript:S5144': 'Constructing URLs from user input is security-sensitive',
      'javascript:S1481': 'Unused local variables should be removed'
    },
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
        key: 'sq-gamma-004',
        component: 'gamma-key:src/jobs.js',
        line: 91,
        rule: 'javascript:S3776',
        severity: 'MAJOR',
        type: 'CODE_SMELL',
        status: 'OPEN',
        message: 'Reduce the Cognitive Complexity of this function.'
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
      const rulesRaw = String(url.searchParams.get('rules') || '').trim();

      const pageIndex = Math.max(1, parseInt(String(url.searchParams.get('p') || '1'), 10) || 1);
      const pageSize = Math.max(1, Math.min(500, parseInt(String(url.searchParams.get('ps') || '100'), 10) || 100));

      const typeSet = new Set(typesRaw
        ? typesRaw.split(',').map(x => x.trim().toUpperCase()).filter(Boolean)
        : []);

      const statusSet = new Set(statusesRaw
        ? statusesRaw.split(',').map(x => x.trim().toUpperCase()).filter(Boolean)
        : []);

      const ruleSet = new Set(rulesRaw
        ? rulesRaw.split(',').map(x => x.trim()).filter(Boolean)
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

      if (ruleSet.size > 0) {
        issues = issues.filter(issue => ruleSet.has(String(issue.rule || '').trim()));
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

    if (pathname === '/api/rules/show' && req.method === 'GET') {
      const key = String(url.searchParams.get('key') || '').trim();
      if (!key) return writeJson(res, 400, { error: 'Missing rule key' });
      const name = state.rules[key];
      if (!name) return writeJson(res, 404, { error: 'Rule not found' });
      return writeJson(res, 200, {
        rule: {
          key,
          name
        }
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
