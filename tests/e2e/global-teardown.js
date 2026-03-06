const fs = require('fs');
const path = require('path');

const runtimePath = path.join(__dirname, '.runtime.json');

function tryKill(pid, signal) {
  if (!pid) return;
  try {
    process.kill(pid, signal);
  } catch {
    // Ignore
  }
}

function cleanupOrchestratorDb(dbPath) {
  const raw = String(dbPath || '').trim();
  if (!raw) return;
  const suffixes = ['', '-shm', '-wal'];
  for (const suffix of suffixes) {
    try {
      fs.unlinkSync(`${raw}${suffix}`);
    } catch {
      // Ignore
    }
  }
}

async function sleep(ms) {
  return new Promise(r => setTimeout(r, ms));
}

module.exports = async () => {
  let runtime;
  try {
    runtime = JSON.parse(fs.readFileSync(runtimePath, 'utf8'));
  } catch {
    return;
  }

  const viewerPid = runtime?.pids?.viewer;
  const mockPid = runtime?.pids?.mock;
  const sonarPid = runtime?.pids?.sonar;

  // Try graceful
  tryKill(viewerPid, 'SIGTERM');
  tryKill(mockPid, 'SIGTERM');
  tryKill(sonarPid, 'SIGTERM');
  await sleep(500);

  // Then force
  tryKill(viewerPid, 'SIGKILL');
  tryKill(mockPid, 'SIGKILL');
  tryKill(sonarPid, 'SIGKILL');

  cleanupOrchestratorDb(runtime?.orchestratorDbPath);

  try { fs.unlinkSync(runtimePath); } catch { /* ignore */ }
};
