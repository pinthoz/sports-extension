// Service worker: only job is to mirror the first starred game's score on the
// toolbar badge as a fallback view. The WebSocket lives in the content script.

chrome.runtime.onMessage.addListener((msg) => {
  if (msg.type === "scores") {
    const game = (msg.games || [])[0];
    if (game) {
      chrome.action.setBadgeText({ text: `${game.homeScore}-${game.awayScore}` });
      chrome.action.setBadgeBackgroundColor({ color: game.isLive ? "#d32f2f" : "#555555" });
      chrome.action.setTitle({
        title: msg.games
          .map((g) => `${g.home} ${g.homeScore}-${g.awayScore} ${g.away} (${g.stage})`)
          .join("\n")
      });
    } else {
      chrome.action.setBadgeText({ text: "" });
      chrome.action.setTitle({ title: "FlashScore Starred Tracker: no starred games found" });
    }
  } else if (msg.type === "status") {
    if (msg.status === "disconnected") {
      chrome.action.setBadgeText({ text: "!" });
      chrome.action.setBadgeBackgroundColor({ color: "#f9a825" });
    }
  }
});
