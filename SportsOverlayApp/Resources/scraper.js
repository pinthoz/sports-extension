// Scraping script injected into the embedded FlashScore browser (WebView2).
// Keep in sync with extension/content.js. The selectors and parsing rules match.
// Evaluates to a JSON string of the starred games on the current page.
(() => {
  const SEL = {
    match: [".event__match", "[id^='g_']"],
    starActive: [
      "[data-testid='wcl-favorite-active']",
      ".eventSubscriber__star--active",
      ".eventStar--active",
      "[class*='star'][class*='active']",
      "[data-state='starred']"
    ],
    home: [".event__participant--home", ".event__homeParticipant", "[class*='homeParticipant']"],
    away: [".event__participant--away", ".event__awayParticipant", "[class*='awayParticipant']"],
    homeScore: [".event__score--home", "[class*='score'][class*='home']"],
    awayScore: [".event__score--away", "[class*='score'][class*='away']"],
    stage: [".event__stage--block", ".event__stage", ".event__time", "[class*='stage']"]
  };

  const SPORT_BY_ID = {
    1: "football", 2: "tennis", 3: "basketball", 4: "hockey", 5: "american-football",
    6: "baseball", 7: "handball", 8: "rugby", 11: "futsal", 12: "volleyball",
    13: "cricket", 14: "darts", 15: "snooker", 16: "boxing", 17: "beach-volleyball",
    19: "rugby-league", 21: "badminton", 24: "motorsport", 28: "esports",
    29: "mma", 30: "table-tennis"
  };

  const SPORT_ALIASES = {
    soccer: "football", futebol: "football",
    tenis: "tennis",
    basquetebol: "basketball", basquete: "basketball",
    hoquei: "hockey", "hoquei-no-gelo": "hockey",
    "futebol-americano": "american-football",
    basebol: "baseball",
    andebol: "handball",
    voleibol: "volleyball", "voleibol-de-praia": "beach-volleyball",
    dardos: "darts",
    "tenis-de-mesa": "table-tennis",
    criquete: "cricket",
    boxe: "boxing"
  };
  const KNOWN_SPORTS = new Set([
    ...Object.values(SPORT_BY_ID),
    ...Object.values(SPORT_ALIASES)
  ]);

  function canonicalSport(name) {
    if (!name) return null;
    const s = name.toLowerCase().normalize("NFD").replace(/[̀-ͯ]/g, "");
    if (SPORT_ALIASES[s]) return SPORT_ALIASES[s];
    if (KNOWN_SPORTS.has(s)) return s;
    return null;
  }

  function sportFromLinks(rootEl) {
    if (!rootEl) return null;
    for (const a of rootEl.querySelectorAll("a[href]")) {
      let path;
      try {
        path = new URL(a.getAttribute("href"), location.origin).pathname;
      } catch {
        continue;
      }
      const sport = canonicalSport(path.split("/").filter(Boolean)[0]);
      if (sport) return sport;
    }
    return null;
  }

  function detectSport(matchEl, headerEl) {
    const m = /^g_(\d+)_/.exec(matchEl.id || "");
    if (m && SPORT_BY_ID[m[1]]) return SPORT_BY_ID[m[1]];
    const fromLinks = sportFromLinks(matchEl) || sportFromLinks(headerEl);
    if (fromLinks) return fromLinks;
    for (let el = matchEl.parentElement; el; el = el.parentElement) {
      for (const cls of el.classList || []) {
        const sport = canonicalSport(cls);
        if (sport) return sport;
      }
    }
    return canonicalSport(location.pathname.split("/").filter(Boolean)[0]) || "football";
  }

  const SUP_DIGITS = { "0": "⁰", "1": "¹", "2": "²", "3": "³", "4": "⁴",
                       "5": "⁵", "6": "⁶", "7": "⁷", "8": "⁸", "9": "⁹" };
  const toSuperscript = (s) => [...s].map((c) => SUP_DIGITS[c] || c).join("");

  function scrapeParts(matchEl, side) {
    const out = [];
    for (let i = 1; i <= 7; i++) {
      const el = matchEl.querySelector(`.event__part--${side}.event__part--${i}`);
      if (!el) continue;
      let t = el.textContent.trim().replace(/\s+/g, "");
      const sup = el.querySelector("sup, sub, [class*='tiebreak' i]");
      if (sup) {
        const supText = sup.textContent.trim();
        if (supText && t.endsWith(supText) && t.length > supText.length)
          t = t.slice(0, -supText.length) + toSuperscript(supText);
      }
      if (t && /\d/.test(t)) out.push(t);
    }
    return out;
  }

  function scrapePoints(matchEl, side) {
    const el = matchEl.querySelector(
      `.event__gamePart--${side}, [class*='gamePart'][class*='${side}']`
    );
    return el ? el.textContent.trim() : "";
  }

  function detectServing(matchEl) {
    if (matchEl.querySelector("[class*='serveHome' i]")) return "home";
    if (matchEl.querySelector("[class*='serveAway' i]")) return "away";
    for (const el of matchEl.querySelectorAll("[class*='serve' i], [title*='serv' i]")) {
      if (el.closest(".event__participant--home, [class*='homeParticipant']")) return "home";
      if (el.closest(".event__participant--away, [class*='awayParticipant']")) return "away";
    }
    return "";
  }

  function q(root, selectors) {
    for (const s of selectors) {
      const el = root.querySelector(s);
      if (el) return el;
    }
    return null;
  }

  function qa(root, selectors) {
    for (const s of selectors) {
      const els = root.querySelectorAll(s);
      if (els.length) return [...els];
    }
    return [];
  }

  function text(root, selectors) {
    const el = q(root, selectors);
    return el ? el.textContent.trim() : "";
  }

  function findHeaderEl(matchEl) {
    let el = matchEl;
    while (el) {
      let sib = el.previousElementSibling;
      while (sib) {
        if (sib.matches(".event__header, [class*='event__header'], [class*='headerLeague'], [class*='wclLeagueHeader']")) {
          return sib;
        }
        sib = sib.previousElementSibling;
      }
      el = el.parentElement;
    }
    return null;
  }

  function competitionFrom(headerEl) {
    if (!headerEl) return "";
    const title = headerEl.querySelector(".event__title, [class*='titleBox'], [class*='title']");
    return (title || headerEl).textContent.trim().replace(/\s+/g, " ");
  }

  function scrapeStarredGames() {
    const games = [];
    for (const match of qa(document, SEL.match)) {
      if (!q(match, SEL.starActive)) continue;

      const stage = text(match, SEL.stage);
      const stageLower = stage.toLowerCase();
      const isFinished = /^(finished|after|fin|ended|ft|aet|pen)/.test(stageLower);
      const isLive =
        match.classList.contains("event__match--live") ||
        match.className.includes("--live") ||
        (!isFinished && /\d/.test(stage) && !/^\d{1,2}:\d{2}$/.test(stage)) ||
        /half time|halftime|break/.test(stageLower);

      const headerEl = findHeaderEl(match);
      const sport = detectSport(match, headerEl);
      const homeParts = scrapeParts(match, "home");
      const awayParts = scrapeParts(match, "away");
      let homePoints = scrapePoints(match, "home");
      let awayPoints = scrapePoints(match, "away");

      const GAME_POINTS = /^(0|15|30|40|A|AD)$/i;
      if (sport === "tennis" && isLive && !homePoints && !awayPoints &&
          homeParts.length && homeParts.length === awayParts.length &&
          GAME_POINTS.test(homeParts[homeParts.length - 1]) &&
          GAME_POINTS.test(awayParts[awayParts.length - 1])) {
        homePoints = homeParts.pop();
        awayPoints = awayParts.pop();
      }

      games.push({
        id: match.id || `${text(match, SEL.home)}-${text(match, SEL.away)}`,
        sport,
        home: text(match, SEL.home),
        away: text(match, SEL.away),
        homeScore: text(match, SEL.homeScore) || "-",
        awayScore: text(match, SEL.awayScore) || "-",
        homeParts,
        awayParts,
        homePoints,
        awayPoints,
        serving: isLive ? detectServing(match) : "",
        stage: stage,
        isLive,
        isFinished,
        competition: competitionFrom(headerEl)
      });
    }
    return games;
  }

  return JSON.stringify(scrapeStarredGames());
})()
