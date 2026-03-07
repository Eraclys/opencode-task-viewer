const fs = require('fs');
const path = require('path');
const { test, expect } = require('@playwright/test');

function getRuntime() {
  const runtimePath = path.join(__dirname, '.runtime.json');
  return JSON.parse(fs.readFileSync(runtimePath, 'utf8'));
}

async function resetMockOpenCode(request, mockUrl) {
  await request.post(`${mockUrl}/__test__/reset`);
  await request.post(`${mockUrl}/__test__/emit`, {
    data: {
      directory: 'C:\\Work\\Alpha',
      type: 'session.updated',
      properties: {}
    }
  });
}

test.beforeEach(async ({ request }) => {
  const { mockUrl } = getRuntime();
  await resetMockOpenCode(request, mockUrl);
});

test('loads with no sidebar and shows All Sessions board', async ({ page }) => {
  const { viewerUrl } = getRuntime();

  await page.goto(viewerUrl);

  await expect(page.locator('aside.sidebar')).toHaveCount(0);
  await expect(page.getByTestId('connection-status')).toContainText('Connected');
  await expect(page.getByText('All Sessions')).toBeVisible();

  // From mock data:
  // sess-busy: runtime busy -> in_progress
  // sess-retry: runtime retry -> in_progress
  // sess-recent: no assistant response -> pending
  // sess-stale: assistant responded at least once -> completed
  await expect(page.getByTestId('count-pending')).toHaveText('1');
  await expect(page.getByTestId('count-in-progress')).toHaveText('2');
  await expect(page.getByTestId('count-completed')).toHaveText('1');
  await expect(page.getByTestId('count-cancelled')).toHaveText('0');

  await expect(page.getByTestId('column-in-progress')).toContainText('Busy Session');
  await expect(page.getByTestId('column-in-progress')).toContainText('Retrying Session');
  await expect(page.getByTestId('column-pending')).toContainText('Recently Updated');
  await expect(page.getByTestId('column-completed')).toContainText('Stale Session');

  const inProgressTitles = await page
    .getByTestId('column-in-progress')
    .locator('[data-testid="session-card"] .task-title')
    .allTextContents();
  expect(inProgressTitles.map(t => t.trim())).toEqual(['Retrying Session', 'Busy Session']);

  // Archived sessions are filtered out via `time.archived` from /session.
  await expect(page.getByText('Archived Session (Should Not Show)')).toHaveCount(0);
});

test('includes sessions discovered via project sandboxes', async ({ page, request }) => {
  const { viewerUrl, mockUrl } = getRuntime();

  await request.post(`${mockUrl}/__test__/addSandboxSession`, {
    data: {
      projectWorktree: 'C:\\Work\\Alpha',
      sandboxPath: 'C:\\Work\\Alpha\\SandboxOnly',
      directory: 'C:\\Work\\Alpha\\SandboxOnly',
      sessionId: 'sess-sandbox-only',
      title: 'Sandbox Only Session'
    }
  });

  await page.goto(viewerUrl);

  await expect(page.getByTestId('column-pending')).toContainText('Sandbox Only Session');

  await page.getByTestId('project-filter').selectOption('C:/Work/Alpha');
  await expect(page.getByTestId('column-pending')).toContainText('Sandbox Only Session');
});

test('project filter applies and persists via local storage', async ({ page }) => {
  const { viewerUrl } = getRuntime();
  await page.goto(viewerUrl);

  const projectFilter = page.getByTestId('project-filter');
  await projectFilter.selectOption('C:/Work/Gamma');

  await expect(page.getByTestId('count-pending')).toHaveText('1');
  await expect(page.getByTestId('count-in-progress')).toHaveText('0');
  await expect(page.getByTestId('count-completed')).toHaveText('1');
  await expect(page.getByTestId('column-pending')).toContainText('Recently Updated');
  await expect(page.getByTestId('column-completed')).toContainText('Stale Session');
  await expect(page.getByTestId('column-in-progress')).not.toContainText('Busy Session');

  await page.reload();
  await expect(page.getByTestId('project-filter')).toHaveValue('C:/Work/Gamma');
  await expect(page.getByTestId('count-pending')).toHaveText('1');
  await expect(page.getByTestId('count-completed')).toHaveText('1');
});

test('refreshes board after session.status SSE', async ({ page, request }) => {
  const { viewerUrl, mockUrl } = getRuntime();
  await page.goto(viewerUrl);

  // Recently Updated starts as pending
  await expect(page.getByTestId('column-pending')).toContainText('Recently Updated');

  // Flip it to busy so it becomes in_progress
  await request.post(`${mockUrl}/__test__/setStatus`, {
    data: {
      directory: 'C:\\Work\\Gamma',
      sessionId: 'sess-recent',
      type: 'busy'
    }
  });

  await request.post(`${mockUrl}/__test__/emit`, {
    data: {
      directory: 'C:\\Work\\Gamma',
      type: 'session.status',
      properties: { sessionID: 'sess-recent', status: { type: 'busy' } }
    }
  });

  await expect(page.getByTestId('column-in-progress')).toContainText('Recently Updated');
  await expect(page.getByTestId('count-in-progress')).toHaveText('3');
  await expect(page.getByTestId('count-pending')).toHaveText('0');
});

test('does not crash if /api/sessions fails', async ({ page }) => {
  const { viewerUrl } = getRuntime();

  await page.route('**/api/sessions**', async (route) => {
    await route.fulfill({
      status: 500,
      contentType: 'application/json',
      body: JSON.stringify({ error: 'Injected failure' })
    });
  });

  await page.goto(viewerUrl);

  await expect(page.getByText('No sessions found')).toBeVisible();
  await expect(page.getByTestId('count-pending')).toHaveText('0');
  await expect(page.getByTestId('count-in-progress')).toHaveText('0');
  await expect(page.getByTestId('count-completed')).toHaveText('0');
  await expect(page.getByTestId('count-cancelled')).toHaveText('0');
});

test('can archive a single selected session via bulk action', async ({ page }) => {
  const { viewerUrl } = getRuntime();

  page.on('dialog', d => d.accept());
  await page.goto(viewerUrl);

  const card = page.getByTestId('session-card').filter({ hasText: 'Recently Updated' }).first();
  await card.hover();
  await card.locator('input.card-select').check();
  await expect(page.getByTestId('bulk-archive-btn')).toHaveText('Archive selected (1)');
  await page.getByTestId('bulk-archive-btn').click();

  await expect(page.getByText('Recently Updated')).toHaveCount(0);
});

test('can clear selected sessions', async ({ page }) => {
  const { viewerUrl } = getRuntime();
  await page.goto(viewerUrl);

  const recentCard = page.getByTestId('session-card').filter({ hasText: 'Recently Updated' }).first();
  const staleCard = page.getByTestId('session-card').filter({ hasText: 'Stale Session' }).first();

  await recentCard.hover();
  await recentCard.locator('input.card-select').check();
  await staleCard.hover();
  await staleCard.locator('input.card-select').check();

  await expect(page.getByTestId('bulk-archive-btn')).toHaveText('Archive selected (2)');
  await expect(page.getByTestId('clear-selection-btn')).toBeEnabled();

  await page.getByTestId('clear-selection-btn').click();

  await expect(page.getByTestId('bulk-archive-btn')).toHaveText('Archive selected (0)');
  await expect(page.getByTestId('bulk-archive-btn')).toBeDisabled();
  await expect(page.getByTestId('clear-selection-btn')).toBeDisabled();
  await expect(page.locator('input.card-select:checked')).toHaveCount(0);
});

test('can bulk archive selected sessions', async ({ page }) => {
  const { viewerUrl } = getRuntime();

  page.on('dialog', d => d.accept());
  await page.goto(viewerUrl);

  await page.getByTestId('project-filter').selectOption('C:/Work/Gamma');

  const recentCard = page.getByTestId('session-card').filter({ hasText: 'Recently Updated' }).first();
  const staleCard = page.getByTestId('session-card').filter({ hasText: 'Stale Session' }).first();

  await recentCard.hover();
  await recentCard.locator('input.card-select').check();
  await staleCard.hover();
  await staleCard.locator('input.card-select').check();

  await expect(page.getByTestId('bulk-archive-btn')).toHaveText('Archive selected (2)');
  await page.getByTestId('bulk-archive-btn').click();

  await expect(page.getByText('Recently Updated')).toHaveCount(0);
  await expect(page.getByText('Stale Session')).toHaveCount(0);
});

test('details show last agent message and OpenCode link', async ({ page }) => {
  const { viewerUrl, mockUrl } = getRuntime();
  await page.goto(viewerUrl);

  await page.getByTestId('session-card').filter({ hasText: 'Stale Session' }).first().click();

  await expect(page.getByTestId('detail-last-agent-message')).toContainText('Diagnostics complete; all checks passed.');
  await expect(page.getByTestId('detail-opencode-link')).toHaveAttribute(
    'href',
    `${mockUrl}/QzovV29yay9HYW1tYQ/session/sess-stale`
  );
});
