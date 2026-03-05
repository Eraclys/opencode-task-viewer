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

  // Try graceful
  tryKill(viewerPid, 'SIGTERM');
  tryKill(mockPid, 'SIGTERM');
  await sleep(500);

  // Then force
  tryKill(viewerPid, 'SIGKILL');
  tryKill(mockPid, 'SIGKILL');

  try { fs.unlinkSync(runtimePath); } catch { /* ignore */ }
};
