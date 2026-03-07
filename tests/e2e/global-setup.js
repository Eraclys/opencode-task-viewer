const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');

const runtimePath = path.join(__dirname, '.runtime.json');
const orchestratorDbPath = path.join(__dirname, '.orchestrator-test.sqlite');

function cleanupOrchestratorDb() {
  const suffixes = ['', '-shm', '-wal'];
  for (const suffix of suffixes) {
    try {
      fs.unlinkSync(`${orchestratorDbPath}${suffix}`);
    } catch {
      // Ignore
    }
  }
}

function waitForLine(proc, prefix, timeoutMs) {
  return new Promise((resolve, reject) => {
    const start = Date.now();
    let buffer = '';

    const onData = (chunk) => {
      buffer += chunk.toString('utf8');
      const lines = buffer.split(/\r?\n/);
      buffer = lines.pop() || '';
      for (const line of lines) {
        if (line.startsWith(prefix)) {
          cleanup();
          return resolve(line.slice(prefix.length));
        }
      }
      if (Date.now() - start > timeoutMs) {
        cleanup();
        reject(new Error(`Timed out waiting for ${prefix}`));
      }
    };

    const onExit = (code) => {
      cleanup();
      reject(new Error(`Process exited while waiting for ${prefix} (code=${code})`));
    };

    function cleanup() {
      proc.stdout?.off('data', onData);
      proc.stderr?.off('data', onData);
      proc.off('exit', onExit);
    }

    proc.on('exit', onExit);
    proc.stdout?.on('data', onData);
    proc.stderr?.on('data', onData);
  });
}

async function waitForOk(url, timeoutMs) {
  const start = Date.now();
  // eslint-disable-next-line no-constant-condition
  while (true) {
    try {
      const res = await fetch(url);
      if (res.ok) return;
    } catch {
      // ignore
    }
    if (Date.now() - start > timeoutMs) {
      throw new Error(`Timed out waiting for ${url}`);
    }
    await new Promise(r => setTimeout(r, 100));
  }
}

module.exports = async () => {
  // Ensure prior runs are cleaned up
  try { fs.unlinkSync(runtimePath); } catch { /* ignore */ }
  cleanupOrchestratorDb();

  const rootDir = path.join(__dirname, '..', '..');
  const mockPath = path.join(rootDir, 'tests', 'mock-opencode', 'server.js');
  const mockSonarPath = path.join(rootDir, 'tests', 'mock-sonarqube', 'server.js');
  const viewerPath = path.join(rootDir, 'server.js');

  const mockProc = spawn(process.execPath, [mockPath], {
    cwd: rootDir,
    env: {
      ...process.env,
      HOST: '127.0.0.1',
      PORT: '0'
    },
    stdio: ['ignore', 'pipe', 'pipe']
  });

  const mockUrl = await waitForLine(mockProc, 'MOCK_OPENCODE_URL=', 15_000);
  await waitForOk(`${mockUrl}/__test__/health`, 10_000);

  const mockSonarProc = spawn(process.execPath, [mockSonarPath], {
    cwd: rootDir,
    env: {
      ...process.env,
      HOST: '127.0.0.1',
      PORT: '0'
    },
    stdio: ['ignore', 'pipe', 'pipe']
  });

  const sonarUrl = await waitForLine(mockSonarProc, 'MOCK_SONAR_URL=', 15_000);
  await waitForOk(`${sonarUrl}/__test__/health`, 10_000);

  const viewerProc = spawn(process.execPath, [viewerPath], {
    cwd: rootDir,
    env: {
      ...process.env,
      HOST: '127.0.0.1',
      PORT: '0',
      OPENCODE_URL: mockUrl,
      ORCHESTRATOR_DB_PATH: orchestratorDbPath,
      SONARQUBE_URL: sonarUrl,
      SONARQUBE_TOKEN: 'test-token',
      ORCH_POLL_MS: '1200',
      ORCH_MAX_ACTIVE: '1',
      ORCH_MAX_ATTEMPTS: '1'
    },
    stdio: ['ignore', 'pipe', 'pipe']
  });

  const viewerUrl = await waitForLine(viewerProc, 'VIEWER_URL=', 15_000);
  await waitForOk(`${viewerUrl}/api/sessions?limit=1`, 10_000);

  fs.writeFileSync(runtimePath, JSON.stringify({
    viewerUrl,
    mockUrl,
    sonarUrl,
    orchestratorDbPath,
    pids: {
      mock: mockProc.pid,
      viewer: viewerProc.pid,
      sonar: mockSonarProc.pid
    }
  }, null, 2));
};
