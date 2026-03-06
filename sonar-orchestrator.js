const fs = require('fs');
const path = require('path');

const initSqlJs = require('sql.js');

const DEFAULT_MAX_ACTIVE = 3;
const DEFAULT_POLL_MS = 3000;
const DEFAULT_MAX_ATTEMPTS = 3;
const MAX_ENQUEUE_BATCH = 1000;

function nowIso() {
  return new Date().toISOString();
}

function parseIntSafe(value, fallback) {
  const n = parseInt(String(value ?? ''), 10);
  if (!Number.isFinite(n)) return fallback;
  return n;
}

function normalizeIssueType(value) {
  const v = String(value || '').trim().toUpperCase();
  if (!v) return null;
  return v;
}

function normalizeQueueStateList(states) {
  const allowed = new Set(['queued', 'dispatching', 'session_created', 'done', 'failed', 'cancelled']);
  const list = [];
  const arr = Array.isArray(states)
    ? states
    : String(states || '').split(',').map(x => x.trim()).filter(Boolean);
  for (const s of arr) {
    const v = String(s || '').trim().toLowerCase();
    if (!v || !allowed.has(v)) continue;
    list.push(v);
  }
  return Array.from(new Set(list));
}

function makeBackoffMs(attempt) {
  const n = Math.max(1, parseIntSafe(attempt, 1));
  const base = 2500;
  const max = 60_000;
  return Math.min(max, base * (2 ** (n - 1)));
}

function normalizeIssueForQueue(issue, mapping) {
  const raw = issue && typeof issue === 'object' ? issue : {};
  const key = String(raw.key || raw.issueKey || '').trim();
  if (!key) return null;

  const type = normalizeIssueType(raw.type || raw.issueType || 'CODE_SMELL') || 'CODE_SMELL';
  const severity = String(raw.severity || '').trim().toUpperCase() || null;
  const rule = String(raw.rule || '').trim() || null;
  const message = String(raw.message || '').trim() || null;
  const line = Number.isFinite(raw.line) ? raw.line : parseIntSafe(raw.line, null);
  const status = String(raw.status || '').trim() || null;
  const component = String(raw.component || raw.file || '').trim() || null;

  const projectKey = String(mapping?.sonarProjectKey || '').trim();
  const relativePath = component
    ? (projectKey && component.startsWith(`${projectKey}:`)
      ? component.slice(projectKey.length + 1)
      : (component.includes(':') ? component.slice(component.indexOf(':') + 1) : component))
    : null;

  const directory = String(mapping?.directory || '').trim();
  const normalizedRelative = relativePath
    ? relativePath.replace(/\\/g, '/').replace(/^\/+/, '')
    : null;
  const absolutePath = normalizedRelative && directory
    ? `${directory.replace(/\/+$/g, '')}/${normalizedRelative}`
    : null;

  return {
    key,
    type,
    severity,
    rule,
    message,
    line: Number.isFinite(line) ? line : null,
    status,
    component,
    relativePath: normalizedRelative,
    absolutePath
  };
}

class SonarOrchestrator {
  constructor(options) {
    this.sonarUrl = String(options?.sonarUrl || '').trim();
    this.sonarToken = String(options?.sonarToken || '').trim();
    this.maxActive = Math.max(1, parseIntSafe(options?.maxActive, DEFAULT_MAX_ACTIVE));
    this.pollMs = Math.max(1000, parseIntSafe(options?.pollMs, DEFAULT_POLL_MS));
    this.maxAttempts = Math.max(1, parseIntSafe(options?.maxAttempts, DEFAULT_MAX_ATTEMPTS));
    this.onChange = typeof options?.onChange === 'function' ? options.onChange : () => {};

    this.opencodeFetch = options?.opencodeFetch;
    this.normalizeDirectory = options?.normalizeDirectory;
    this.buildOpenCodeSessionUrl = options?.buildOpenCodeSessionUrl;

    this.dbPath = String(options?.dbPath || '').trim()
      || path.join(process.cwd(), 'data', 'orchestrator.sqlite');

    const dbDir = path.dirname(this.dbPath);
    fs.mkdirSync(dbDir, { recursive: true });

    this.SQL = null;
    this.db = null;
    this.timer = null;
    this.inFlight = new Set();
    this.tickRunning = false;

    this.readyPromise = this.initialize();
  }

  async initialize() {
    const wasmPath = require.resolve('sql.js/dist/sql-wasm.wasm');
    this.SQL = await initSqlJs({
      locateFile: (file) => (file === 'sql-wasm.wasm' ? wasmPath : file)
    });

    if (fs.existsSync(this.dbPath)) {
      const data = fs.readFileSync(this.dbPath);
      this.db = new this.SQL.Database(data);
    } else {
      this.db = new this.SQL.Database();
    }

    this.db.run(`
      CREATE TABLE IF NOT EXISTS project_mappings (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        sonar_project_key TEXT NOT NULL UNIQUE,
        directory TEXT NOT NULL,
        branch TEXT,
        enabled INTEGER NOT NULL DEFAULT 1,
        created_at TEXT NOT NULL,
        updated_at TEXT NOT NULL
      );

      CREATE TABLE IF NOT EXISTS instruction_profiles (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        mapping_id INTEGER NOT NULL,
        issue_type TEXT NOT NULL,
        instructions TEXT NOT NULL,
        created_at TEXT NOT NULL,
        updated_at TEXT NOT NULL,
        UNIQUE(mapping_id, issue_type)
      );

      CREATE TABLE IF NOT EXISTS queue_items (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        issue_key TEXT NOT NULL,
        mapping_id INTEGER NOT NULL,
        sonar_project_key TEXT NOT NULL,
        directory TEXT NOT NULL,
        branch TEXT,
        issue_type TEXT,
        severity TEXT,
        rule TEXT,
        message TEXT,
        component TEXT,
        relative_path TEXT,
        absolute_path TEXT,
        line INTEGER,
        issue_status TEXT,
        instructions_snapshot TEXT,
        state TEXT NOT NULL,
        attempt_count INTEGER NOT NULL DEFAULT 0,
        max_attempts INTEGER NOT NULL DEFAULT 3,
        next_attempt_at TEXT,
        session_id TEXT,
        open_code_url TEXT,
        last_error TEXT,
        created_at TEXT NOT NULL,
        updated_at TEXT NOT NULL,
        dispatched_at TEXT,
        completed_at TEXT,
        cancelled_at TEXT
      );

      CREATE INDEX IF NOT EXISTS idx_queue_state_next_attempt ON queue_items(state, next_attempt_at, created_at);
      CREATE INDEX IF NOT EXISTS idx_queue_issue_key ON queue_items(issue_key);
      CREATE INDEX IF NOT EXISTS idx_queue_mapping_state ON queue_items(mapping_id, state, created_at);
    `);

    this.persist();
  }

  async ensureReady() {
    await this.readyPromise;
    if (!this.db) throw new Error('Orchestrator DB is not initialized');
  }

  persist() {
    if (!this.db) return;
    const bytes = this.db.export();
    fs.writeFileSync(this.dbPath, Buffer.from(bytes));
  }

  run(sql, params = []) {
    this.db.run(sql, params);
  }

  all(sql, params = []) {
    const stmt = this.db.prepare(sql, params);
    const rows = [];
    while (stmt.step()) {
      rows.push(stmt.getAsObject());
    }
    stmt.free();
    return rows;
  }

  get(sql, params = []) {
    const rows = this.all(sql, params);
    return rows.length > 0 ? rows[0] : null;
  }

  changes() {
    const row = this.get('SELECT changes() AS count');
    return Number.isFinite(row?.count) ? row.count : 0;
  }

  mapMappingRow(row) {
    if (!row) return null;
    return {
      id: Number(row.id),
      sonarProjectKey: row.sonar_project_key,
      directory: row.directory,
      branch: row.branch || null,
      enabled: Boolean(Number(row.enabled)),
      createdAt: row.created_at,
      updatedAt: row.updated_at
    };
  }

  mapQueueRow(row) {
    if (!row) return null;
    const lineNum = parseIntSafe(row.line, null);
    return {
      id: Number(row.id),
      issueKey: row.issue_key,
      mappingId: Number(row.mapping_id),
      sonarProjectKey: row.sonar_project_key,
      directory: row.directory,
      branch: row.branch || null,
      issueType: row.issue_type || null,
      severity: row.severity || null,
      rule: row.rule || null,
      message: row.message || null,
      component: row.component || null,
      relativePath: row.relative_path || null,
      absolutePath: row.absolute_path || null,
      line: Number.isFinite(lineNum) ? lineNum : null,
      issueStatus: row.issue_status || null,
      instructions: row.instructions_snapshot || null,
      state: row.state,
      attemptCount: parseIntSafe(row.attempt_count, 0),
      maxAttempts: parseIntSafe(row.max_attempts, this.maxAttempts),
      nextAttemptAt: row.next_attempt_at || null,
      sessionId: row.session_id || null,
      openCodeUrl: row.open_code_url || null,
      lastError: row.last_error || null,
      createdAt: row.created_at,
      updatedAt: row.updated_at,
      dispatchedAt: row.dispatched_at || null,
      completedAt: row.completed_at || null,
      cancelledAt: row.cancelled_at || null
    };
  }

  isConfigured() {
    return Boolean(this.sonarUrl && this.sonarToken && this.opencodeFetch);
  }

  async listMappings() {
    await this.ensureReady();
    const rows = this.all(`
      SELECT id, sonar_project_key, directory, branch, enabled, created_at, updated_at
      FROM project_mappings
      ORDER BY sonar_project_key COLLATE NOCASE ASC
    `);
    return rows.map(r => this.mapMappingRow(r));
  }

  async getMappingById(mappingId) {
    await this.ensureReady();
    const id = parseIntSafe(mappingId, null);
    if (!Number.isFinite(id) || id <= 0) return null;
    const row = this.get(`
      SELECT id, sonar_project_key, directory, branch, enabled, created_at, updated_at
      FROM project_mappings
      WHERE id = ?
      LIMIT 1
    `, [id]);
    return this.mapMappingRow(row);
  }

  async upsertMapping(payload) {
    await this.ensureReady();
    const now = nowIso();
    const sonarProjectKey = String(payload?.sonarProjectKey || payload?.sonar_project_key || '').trim();
    let directory = String(payload?.directory || '').trim();
    const branch = String(payload?.branch || '').trim() || null;
    const enabled = payload?.enabled === undefined ? true : Boolean(payload.enabled);

    if (!sonarProjectKey) throw new Error('Missing sonarProjectKey');
    if (!directory) throw new Error('Missing directory');

    directory = this.normalizeDirectory ? this.normalizeDirectory(directory) : directory.replace(/\\/g, '/');

    const id = parseIntSafe(payload?.id, null);
    if (Number.isFinite(id) && id > 0) {
      this.run(`
        UPDATE project_mappings
        SET sonar_project_key = ?,
            directory = ?,
            branch = ?,
            enabled = ?,
            updated_at = ?
        WHERE id = ?
      `, [sonarProjectKey, directory, branch, enabled ? 1 : 0, now, id]);
      if (this.changes() === 0) throw new Error('Mapping not found');
      this.persist();
      return this.getMappingById(id);
    }

    this.run(`
      INSERT INTO project_mappings (
        sonar_project_key, directory, branch, enabled, created_at, updated_at
      ) VALUES (?, ?, ?, ?, ?, ?)
      ON CONFLICT(sonar_project_key) DO UPDATE SET
        directory = excluded.directory,
        branch = excluded.branch,
        enabled = excluded.enabled,
        updated_at = excluded.updated_at
    `, [sonarProjectKey, directory, branch, enabled ? 1 : 0, now, now]);

    this.persist();
    const row = this.get(`
      SELECT id, sonar_project_key, directory, branch, enabled, created_at, updated_at
      FROM project_mappings
      WHERE sonar_project_key = ?
      LIMIT 1
    `, [sonarProjectKey]);
    return this.mapMappingRow(row);
  }

  async getInstructionProfile({ mappingId, issueType }) {
    await this.ensureReady();
    const mapping = await this.getMappingById(mappingId);
    if (!mapping) return null;
    const type = normalizeIssueType(issueType);
    if (!type) return null;
    return this.get(`
      SELECT id, mapping_id, issue_type, instructions, created_at, updated_at
      FROM instruction_profiles
      WHERE mapping_id = ? AND issue_type = ?
      LIMIT 1
    `, [mapping.id, type]);
  }

  async upsertInstructionProfile({ mappingId, issueType, instructions }) {
    await this.ensureReady();
    const mapping = await this.getMappingById(mappingId);
    if (!mapping) throw new Error('Mapping not found');

    const type = normalizeIssueType(issueType);
    if (!type) throw new Error('Missing issueType');

    const text = String(instructions || '').trim();
    if (!text) throw new Error('Missing instructions');

    const now = nowIso();
    this.run(`
      INSERT INTO instruction_profiles (mapping_id, issue_type, instructions, created_at, updated_at)
      VALUES (?, ?, ?, ?, ?)
      ON CONFLICT(mapping_id, issue_type) DO UPDATE SET
        instructions = excluded.instructions,
        updated_at = excluded.updated_at
    `, [mapping.id, type, text, now, now]);
    this.persist();

    return this.get(`
      SELECT id, mapping_id, issue_type, instructions, created_at, updated_at
      FROM instruction_profiles
      WHERE mapping_id = ? AND issue_type = ?
      LIMIT 1
    `, [mapping.id, type]);
  }

  async sonarFetch(endpointPath, query = {}) {
    if (!this.sonarUrl || !this.sonarToken) {
      throw new Error('SonarQube is not configured');
    }

    const url = new URL(endpointPath, this.sonarUrl);
    for (const [k, v] of Object.entries(query || {})) {
      if (v === undefined || v === null || v === '') continue;
      url.searchParams.set(k, String(v));
    }

    const token = Buffer.from(`${this.sonarToken}:`, 'utf8').toString('base64');
    const res = await fetch(url, {
      method: 'GET',
      headers: {
        'Accept': 'application/json',
        'Authorization': `Basic ${token}`
      }
    });

    if (!res.ok) {
      const body = await res.text().catch(() => '');
      const err = new Error(`SonarQube request failed: ${res.status} ${res.statusText}`);
      err.status = res.status;
      err.body = body;
      throw err;
    }

    return res.json();
  }

  async listIssues({ mappingId, issueType, severity, issueStatus, page, pageSize }) {
    await this.ensureReady();
    const mapping = await this.getMappingById(mappingId);
    if (!mapping || !mapping.enabled) {
      throw new Error('Mapping not found or disabled');
    }

    const type = normalizeIssueType(issueType);
    const sev = String(severity || '').trim().toUpperCase();
    const status = String(issueStatus || '').trim().toUpperCase();
    const p = Math.max(1, parseIntSafe(page, 1));
    const ps = Math.max(1, Math.min(500, parseIntSafe(pageSize, 100)));

    const query = {
      componentKeys: mapping.sonarProjectKey,
      p,
      ps
    };
    if (type) query.types = type;
    if (sev) query.severities = sev;
    if (status) query.statuses = status;
    if (mapping.branch) query.branch = mapping.branch;

    const data = await this.sonarFetch('/api/issues/search', query);
    const issuesRaw = Array.isArray(data?.issues) ? data.issues : [];
    const issues = [];
    for (const raw of issuesRaw) {
      const issue = normalizeIssueForQueue(raw, mapping);
      if (!issue) continue;
      issues.push({
        key: issue.key,
        type: issue.type,
        severity: issue.severity,
        rule: issue.rule,
        message: issue.message,
        component: issue.component,
        line: issue.line,
        status: issue.status,
        relativePath: issue.relativePath,
        absolutePath: issue.absolutePath
      });
    }

    const paging = data?.paging || {};
    return {
      mapping,
      paging: {
        pageIndex: Number.isFinite(paging.pageIndex) ? paging.pageIndex : p,
        pageSize: Number.isFinite(paging.pageSize) ? paging.pageSize : ps,
        total: Number.isFinite(paging.total) ? paging.total : issues.length
      },
      issues
    };
  }

  async enqueueIssues({ mappingId, issueType, instructions, issues }) {
    await this.ensureReady();
    const mapping = await this.getMappingById(mappingId);
    if (!mapping || !mapping.enabled) {
      throw new Error('Mapping not found or disabled');
    }

    const rawIssues = Array.isArray(issues) ? issues.slice(0, MAX_ENQUEUE_BATCH) : [];
    if (rawIssues.length === 0) {
      throw new Error('No issues provided');
    }

    const type = normalizeIssueType(issueType);
    const defaultProfile = await this.getInstructionProfile({ mappingId: mapping.id, issueType: type || '' });
    const defaultInstruction = defaultProfile?.instructions || '';
    const instructionText = String(instructions || '').trim() || String(defaultInstruction || '').trim();

    if (type && instructionText) {
      const nowProfile = nowIso();
      this.run(`
        INSERT INTO instruction_profiles (mapping_id, issue_type, instructions, created_at, updated_at)
        VALUES (?, ?, ?, ?, ?)
        ON CONFLICT(mapping_id, issue_type) DO UPDATE SET
          instructions = excluded.instructions,
          updated_at = excluded.updated_at
      `, [mapping.id, type, instructionText, nowProfile, nowProfile]);
    }

    const createdItems = [];
    const skipped = [];
    const now = nowIso();

    for (const rawIssue of rawIssues) {
      const issue = normalizeIssueForQueue(rawIssue, mapping);
      if (!issue) {
        skipped.push({ issueKey: null, reason: 'invalid-issue' });
        continue;
      }

      const existing = this.get(`
        SELECT id, state
        FROM queue_items
        WHERE mapping_id = ?
          AND issue_key = ?
          AND state IN ('queued', 'dispatching')
        LIMIT 1
      `, [mapping.id, issue.key]);

      if (existing) {
        skipped.push({ issueKey: issue.key, reason: `already-${existing.state}` });
        continue;
      }

      this.run(`
        INSERT INTO queue_items (
          issue_key, mapping_id, sonar_project_key, directory, branch,
          issue_type, severity, rule, message,
          component, relative_path, absolute_path, line, issue_status,
          instructions_snapshot,
          state, attempt_count, max_attempts, next_attempt_at,
          created_at, updated_at
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 'queued', 0, ?, ?, ?, ?)
      `, [
        issue.key,
        mapping.id,
        mapping.sonarProjectKey,
        mapping.directory,
        mapping.branch,
        type || issue.type,
        issue.severity,
        issue.rule,
        issue.message,
        issue.component,
        issue.relativePath,
        issue.absolutePath,
        issue.line,
        issue.status,
        instructionText || null,
        this.maxAttempts,
        now,
        now,
        now
      ]);

      const inserted = this.get(`
        SELECT *
        FROM queue_items
        WHERE id = last_insert_rowid()
      `);
      if (inserted) createdItems.push(this.mapQueueRow(inserted));
    }

    this.persist();
    if (createdItems.length > 0) this.onChange({ type: 'queue.enqueued' });

    return {
      created: createdItems.length,
      skipped,
      items: createdItems
    };
  }

  async listQueue({ states, limit }) {
    await this.ensureReady();
    const selectedStates = normalizeQueueStateList(states);
    const n = Math.max(1, Math.min(5000, parseIntSafe(limit, 250)));

    const params = [];
    let where = '';
    if (selectedStates.length > 0) {
      const placeholders = selectedStates.map(() => '?').join(', ');
      where = `WHERE state IN (${placeholders})`;
      params.push(...selectedStates);
    }
    params.push(n);

    const rows = this.all(`
      SELECT *
      FROM queue_items
      ${where}
      ORDER BY datetime(updated_at) DESC, id DESC
      LIMIT ?
    `, params);

    return rows.map(r => this.mapQueueRow(r));
  }

  async getQueueStats() {
    await this.ensureReady();
    const rows = this.all(`
      SELECT state, COUNT(*) as count
      FROM queue_items
      GROUP BY state
    `);
    const stats = {
      queued: 0,
      dispatching: 0,
      session_created: 0,
      done: 0,
      failed: 0,
      cancelled: 0
    };
    for (const row of rows) {
      if (!row || !row.state) continue;
      const key = String(row.state);
      if (stats[key] === undefined) continue;
      stats[key] = parseIntSafe(row.count, 0);
    }
    return stats;
  }

  async cancelQueueItem(queueId) {
    await this.ensureReady();
    const id = parseIntSafe(queueId, null);
    if (!Number.isFinite(id) || id <= 0) throw new Error('Invalid queue id');
    const now = nowIso();
    this.run(`
      UPDATE queue_items
      SET state = 'cancelled',
          cancelled_at = ?,
          updated_at = ?
      WHERE id = ?
        AND state IN ('queued', 'dispatching')
    `, [now, now, id]);
    const changed = this.changes();
    if (changed > 0) {
      this.persist();
      this.onChange({ type: 'queue.cancelled', queueId: id });
      return true;
    }
    return false;
  }

  async retryFailed() {
    await this.ensureReady();
    const now = nowIso();
    this.run(`
      UPDATE queue_items
      SET state = 'queued',
          next_attempt_at = ?,
          updated_at = ?,
          last_error = NULL
      WHERE state = 'failed'
    `, [now, now]);
    const changed = this.changes();
    if (changed > 0) {
      this.persist();
      this.onChange({ type: 'queue.retried' });
    }
    return changed;
  }

  getPublicConfig() {
    return {
      configured: this.isConfigured(),
      maxActive: this.maxActive,
      pollMs: this.pollMs,
      maxAttempts: this.maxAttempts
    };
  }

  composePrompt(item) {
    const lines = [];
    lines.push('Resolve the following SonarQube warning with a minimal, targeted change.');
    lines.push('');
    lines.push(`Issue key: ${item.issueKey}`);
    if (item.issueType) lines.push(`Issue type: ${item.issueType}`);
    if (item.severity) lines.push(`Severity: ${item.severity}`);
    if (item.rule) lines.push(`Rule: ${item.rule}`);
    if (item.issueStatus) lines.push(`Issue status: ${item.issueStatus}`);
    if (item.relativePath) lines.push(`File: ${item.relativePath}`);
    if (item.line) lines.push(`Line: ${item.line}`);
    if (item.message) lines.push(`Message: ${item.message}`);
    lines.push('');
    lines.push('Constraints:');
    lines.push('- Fix only this issue; avoid unrelated refactors.');
    lines.push('- Preserve behavior and public contracts.');
    lines.push('- If the issue is not actionable, explain why and propose the safest alternative.');

    const extra = String(item.instructions || '').trim();
    if (extra) {
      lines.push('');
      lines.push('Additional instructions:');
      lines.push(extra);
    }

    return lines.join('\n').trim();
  }

  async claimNextQueuedItem() {
    await this.ensureReady();
    const now = nowIso();
    const row = this.get(`
      SELECT *
      FROM queue_items
      WHERE state = 'queued'
        AND (next_attempt_at IS NULL OR next_attempt_at <= ?)
      ORDER BY datetime(created_at) ASC, id ASC
      LIMIT 1
    `, [now]);
    if (!row) return null;

    this.run(`
      UPDATE queue_items
      SET state = 'dispatching',
          attempt_count = attempt_count + 1,
          updated_at = ?,
          dispatched_at = COALESCE(dispatched_at, ?),
          last_error = NULL
      WHERE id = ? AND state = 'queued'
    `, [now, now, row.id]);
    if (this.changes() === 0) return null;

    this.persist();
    const claimed = this.get('SELECT * FROM queue_items WHERE id = ?', [row.id]);
    return this.mapQueueRow(claimed);
  }

  async dispatchQueueItem(queueItem) {
    const item = queueItem;
    if (!item) return;

    try {
      const title = `[${item.issueType || 'ISSUE'}] ${item.issueKey}`;
      const created = await this.opencodeFetch('/session', {
        method: 'POST',
        directory: item.directory,
        json: { title }
      });

      const sessionId = String(created?.id || '').trim();
      if (!sessionId) {
        throw new Error('OpenCode did not return a session id');
      }

      const prompt = this.composePrompt(item);
      await this.opencodeFetch(`/session/${encodeURIComponent(sessionId)}/prompt_async`, {
        method: 'POST',
        directory: item.directory,
        json: {
          parts: [{ type: 'text', text: prompt }]
        }
      });

      const ts = nowIso();
      const openCodeUrl = this.buildOpenCodeSessionUrl
        ? this.buildOpenCodeSessionUrl({ sessionId, directory: item.directory })
        : null;

      this.run(`
        UPDATE queue_items
        SET state = 'session_created',
            session_id = ?,
            open_code_url = ?,
            completed_at = ?,
            updated_at = ?,
            next_attempt_at = NULL,
            last_error = NULL
        WHERE id = ?
          AND state = 'dispatching'
      `, [sessionId, openCodeUrl, ts, ts, item.id]);
      const changed = this.changes();
      if (changed > 0) {
        this.persist();
        this.onChange({ type: 'queue.session_created', queueId: item.id, sessionId });
      }
    } catch (error) {
      const fresh = this.get('SELECT attempt_count, max_attempts FROM queue_items WHERE id = ?', [item.id]);
      const attemptCount = parseIntSafe(fresh?.attempt_count, item.attemptCount || 0);
      const maxAttempts = parseIntSafe(fresh?.max_attempts, item.maxAttempts || this.maxAttempts);

      const exhausted = attemptCount >= maxAttempts;
      const nextAttemptAt = exhausted ? null : new Date(Date.now() + makeBackoffMs(attemptCount)).toISOString();
      const state = exhausted ? 'failed' : 'queued';
      const lastError = String(error?.message || error || 'Unknown error');

      this.run(`
        UPDATE queue_items
        SET state = ?,
            next_attempt_at = ?,
            last_error = ?,
            updated_at = ?
        WHERE id = ?
          AND state = 'dispatching'
      `, [state, nextAttemptAt, lastError, nowIso(), item.id]);
      const changed = this.changes();
      if (changed > 0) {
        this.persist();
        this.onChange({ type: exhausted ? 'queue.failed' : 'queue.retry_scheduled', queueId: item.id });
      }
    }
  }

  async tick() {
    if (this.tickRunning) return;
    this.tickRunning = true;
    try {
      await this.ensureReady();
      if (!this.isConfigured()) return;

      while (this.inFlight.size < this.maxActive) {
        const claim = await this.claimNextQueuedItem();
        if (!claim) break;

        const key = String(claim.id);
        this.inFlight.add(key);
        this.onChange({ type: 'queue.dispatching', queueId: claim.id });

        this.dispatchQueueItem(claim)
          .finally(() => {
            this.inFlight.delete(key);
            this.onChange({ type: 'queue.updated', queueId: claim.id });
          });
      }
    } finally {
      this.tickRunning = false;
    }
  }

  start() {
    if (this.timer) return;
    this.timer = setInterval(() => {
      this.tick().catch(() => {});
    }, this.pollMs);
    this.tick().catch(() => {});
  }

  stop() {
    if (!this.timer) return;
    clearInterval(this.timer);
    this.timer = null;
  }
}

function createSonarOrchestrator(options) {
  return new SonarOrchestrator(options);
}

module.exports = {
  createSonarOrchestrator
};
