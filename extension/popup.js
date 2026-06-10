const dot = document.getElementById("dot");
const statusText = document.getElementById("statusText");
const enabledInput = document.getElementById("enabled");
const portInput = document.getElementById("port");

function renderStatus(status, at) {
  // Treat a stale status (no update for 30 s) as "no FlashScore tab open".
  const stale = !at || Date.now() - at > 30000;
  const effective = stale ? "no FlashScore tab" : status;
  dot.className = stale ? "" : status;
  statusText.textContent = effective;
}

chrome.storage.local.get(
  { trackerPort: 8787, trackerEnabled: true, trackerStatus: "unknown", trackerStatusAt: 0 },
  (cfg) => {
    portInput.value = cfg.trackerPort;
    enabledInput.checked = cfg.trackerEnabled !== false;
    renderStatus(cfg.trackerStatus, cfg.trackerStatusAt);
  }
);

chrome.storage.onChanged.addListener((changes, area) => {
  if (area === "local" && changes.trackerStatus) {
    renderStatus(changes.trackerStatus.newValue, Date.now());
  }
});

document.getElementById("save").addEventListener("click", () => {
  chrome.storage.local.set({
    trackerPort: Number(portInput.value) || 8787,
    trackerEnabled: enabledInput.checked
  });
  window.close();
});
