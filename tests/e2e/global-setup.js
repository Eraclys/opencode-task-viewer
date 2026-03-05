const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');

const runtimePath = path.join(__dirname, '.runtime.json');

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

  const rootDir = path.join(__dirname, '..', '..');
  const mockPath = path.join(rootDir, 'tests', 'mock-opencode', 'server.js');
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

  const viewerProc = spawn(process.execPath, [viewerPath], {
    cwd: rootDir,
    env: {
      ...process.env,
      HOST: '127.0.0.1',
      PORT: '0',
      OPENCODE_URL: mockUrl
    },
    stdio: ['ignore', 'pipe', 'pipe']
  });

  const viewerUrl = await waitForLine(viewerProc, 'VIEWER_URL=', 15_000);
  await waitForOk(`${viewerUrl}/api/sessions?limit=1`, 10_000);

  fs.writeFileSync(runtimePath, JSON.stringify({
    viewerUrl,
    mockUrl,
    pids: {
      mock: mockProc.pid,
      viewer: viewerProc.pid
    }
  }, null, 2));
};
