const fs = require('fs');
const path = require('path');
const { test, expect } = require('@playwright/test');

function getRuntime() {
  const runtimePath = path.join(__dirname, '.runtime.json');
  return JSON.parse(fs.readFileSync(runtimePath, 'utf8'));
}

async function resetMocks(request, mockUrl, sonarUrl) {
  await request.post(`${mockUrl}/__test__/reset`);
  await request.post(`${mockUrl}/__test__/setFailures`, {
    data: { sessionCreateCount: 0, promptAsyncCount: 0 }
  });
  await request.post(`${mockUrl}/__test__/emit`, {
    data: {
      directory: 'C:\\Work\\Alpha',
      type: 'session.updated',
      properties: {}
    }
  });
  await request.post(`${sonarUrl}/__test__/reset`);
}

async function setupGammaMapping(page) {
  await expect(page.getByTestId('orch-settings-toggle')).toBeVisible();
  await page.getByTestId('orch-settings-toggle').click();
  await expect(page.getByTestId('orch-settings-modal')).toBeVisible();
  await page.getByTestId('orch-new-project-key').fill('gamma-key');
  await page.getByTestId('orch-new-directory').fill('C:/Work/Gamma');
  await page.getByTestId('orch-save-mapping-btn').click();
  await expect(page.getByTestId('orch-settings-modal')).not.toBeVisible();
  await expect(page.getByTestId('orch-mapping-select')).toHaveValue(/\d+/);
}

async function loadCodeSmellIssues(page) {
  await page.getByTestId('orch-issue-type').selectOption('CODE_SMELL');
  await page.getByTestId('orch-load-issues-btn').click();
  await expect(page.locator('.orch-issue-row')).toHaveCount(2);
}

async function getLatestQueueItemForIssue(request, viewerUrl, issueKey) {
  const res = await request.get(`${viewerUrl}/api/orch/queue?limit=500`);
  if (!res.ok()) throw new Error('Failed to load queue');
  const data = await res.json();
  const items = Array.isArray(data?.items) ? data.items : [];
  const matches = items.filter(item => item?.issueKey === issueKey);
  if (matches.length === 0) return null;
  return matches.sort((a, b) => Number(b.id || 0) - Number(a.id || 0))[0];
}

async function waitForQueueItemStateById(request, viewerUrl, queueId, expectedState, timeoutMs = 15_000) {
  const id = Number(queueId);
  if (!Number.isFinite(id) || id <= 0) {
    throw new Error(`Invalid queue id: ${queueId}`);
  }

  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const res = await request.get(`${viewerUrl}/api/orch/queue?limit=200`);
    if (res.ok()) {
      const data = await res.json();
      const items = Array.isArray(data?.items) ? data.items : [];
      const match = items.find(item => Number(item?.id) === id);
      if (match && match.state === expectedState) return match;
    }
    await pageWait(250);
  }

  throw new Error(`Timed out waiting for queue item ${id} to reach state ${expectedState}`);
}

async function pageWait(ms) {
  await new Promise(resolve => setTimeout(resolve, ms));
}

test('orchestration flow queues per issue and creates OpenCode sessions', async ({ page, request }) => {
  const { viewerUrl, mockUrl, sonarUrl } = getRuntime();
  await resetMocks(request, mockUrl, sonarUrl);

  await page.goto(viewerUrl);

  await expect(page.getByTestId('orchestrator-panel')).toBeVisible();

  await setupGammaMapping(page);
  await loadCodeSmellIssues(page);

  const issueRows = page.locator('.orch-issue-row');

  const firstIssue = issueRows.first();
  await firstIssue.locator('input[type="checkbox"]').check();

  await page.getByTestId('orch-instructions').fill('Keep changes minimal and only address this single Sonar warning.');
  await page.getByTestId('orch-enqueue-btn').click();

  await expect(page.getByTestId('orch-issues-status')).toContainText('Queued 1 issue');
  await expect(page.getByTestId('column-pending')).toContainText('[Queued]');

  await expect(page.getByTestId('column-pending')).toContainText('[CODE_SMELL]');
  await expect(page.getByTestId('column-pending')).toContainText('sq-gamma-');
  await expect(page.getByTestId('column-pending')).not.toContainText('[Queued]', { timeout: 15_000 });
});

test('failed queue item can be retried and then creates session', async ({ page, request }) => {
  const { viewerUrl, mockUrl, sonarUrl } = getRuntime();
  await resetMocks(request, mockUrl, sonarUrl);

  await request.post(`${mockUrl}/__test__/setFailures`, {
    data: { sessionCreateCount: 1, promptAsyncCount: 0 }
  });

  await page.goto(viewerUrl);
  await expect(page.getByTestId('orchestrator-panel')).toBeVisible();

  await setupGammaMapping(page);
  await loadCodeSmellIssues(page);

  const firstIssue = page.locator('.orch-issue-row').first();
  const firstIssueText = String(await firstIssue.textContent() || '');
  const issueKeyMatch = firstIssueText.match(/sq-gamma-\d+/);
  expect(issueKeyMatch).toBeTruthy();
  const issueKey = issueKeyMatch[0];

  await firstIssue.locator('input[type="checkbox"]').check();
  await page.getByTestId('orch-instructions').fill('Retry path test instruction.');
  await page.getByTestId('orch-enqueue-btn').click();

  await expect(page.getByTestId('orch-issues-status')).toContainText('Queued 1 issue');

  const queuedItem = await getLatestQueueItemForIssue(request, viewerUrl, issueKey);
  expect(queuedItem).toBeTruthy();
  const queueId = queuedItem.id;

  const failedItem = await waitForQueueItemStateById(request, viewerUrl, queueId, 'failed', 20_000);
  expect(failedItem.lastError).toContain('OpenCode request failed');

  const retryRes = await request.post(`${viewerUrl}/api/orch/queue/retry-failed`);
  expect(retryRes.ok()).toBeTruthy();
  const retryBody = await retryRes.json();
  expect(retryBody.retried).toBeGreaterThan(0);

  const sessionCreated = await waitForQueueItemStateById(request, viewerUrl, queueId, 'session_created', 20_000);
  expect(sessionCreated.sessionId).toBeTruthy();
  expect(sessionCreated.openCodeUrl).toContain('/session/');

  await page.reload();
  await expect(page.getByTestId('column-pending')).toContainText(issueKey);
});

test('queue item can be cancelled while queued or dispatching', async ({ page, request }) => {
  const { viewerUrl, mockUrl, sonarUrl } = getRuntime();
  await resetMocks(request, mockUrl, sonarUrl);

  await request.post(`${mockUrl}/__test__/setFailures`, {
    data: { sessionCreateCount: 0, promptAsyncCount: 0, promptDelayMs: 2500 }
  });

  await page.goto(viewerUrl);
  await expect(page.getByTestId('orchestrator-panel')).toBeVisible();

  await setupGammaMapping(page);
  await loadCodeSmellIssues(page);

  const firstIssue = page.locator('.orch-issue-row').first();
  const firstIssueText = String(await firstIssue.textContent() || '');
  const issueKeyMatch = firstIssueText.match(/sq-gamma-\d+/);
  expect(issueKeyMatch).toBeTruthy();
  const issueKey = issueKeyMatch[0];

  await firstIssue.locator('input[type="checkbox"]').check();
  await page.getByTestId('orch-instructions').fill('Cancellation flow test instruction.');
  await page.getByTestId('orch-enqueue-btn').click();
  await expect(page.getByTestId('orch-issues-status')).toContainText('Queued 1 issue');

  const queuedItem = await getLatestQueueItemForIssue(request, viewerUrl, issueKey);
  expect(queuedItem).toBeTruthy();
  const queueId = queuedItem.id;

  const cancelRes = await request.post(`${viewerUrl}/api/orch/queue/${queueId}/cancel`);
  expect(cancelRes.ok()).toBeTruthy();
  const cancelBody = await cancelRes.json();
  expect(cancelBody.ok).toBeTruthy();

  const cancelled = await waitForQueueItemStateById(request, viewerUrl, queueId, 'cancelled', 20_000);
  expect(cancelled.cancelledAt).toBeTruthy();

  await pageWait(3200);
  const latest = await getLatestQueueItemForIssue(request, viewerUrl, issueKey);
  expect(latest).toBeTruthy();
  expect(latest.state).toBe('cancelled');

  const sessionsRes = await request.get(`${viewerUrl}/api/sessions?limit=all`);
  expect(sessionsRes.ok()).toBeTruthy();
  const sessions = await sessionsRes.json();
  const syntheticQueueCard = (Array.isArray(sessions) ? sessions : []).find(s => s?.id === `queue-${queueId}`);
  expect(syntheticQueueCard).toBeFalsy();
});
