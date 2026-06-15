// Content script: runs inside FlashScore pages.
// Scrapes every starred match and streams the data to the local
// SportsOverlay desktop app over a WebSocket (ws://127.0.0.1:<port>).

(() => {
  const DEFAULT_PORT = 8787;
  const SCRAPE_INTERVAL_MS = 2000;
  const HEARTBEAT_MS = 10000;
  const RECONNECT_MIN_MS = 1000;
  const RECONNECT_MAX_MS = 15000;

  let port = DEFAULT_PORT;
  let enabled = true;
  let socket = null;
  let reconnectDelay = RECONNECT_MIN_MS;
  let reconnectTimer = null;
  let lastPayloadHash = "";
  let lastSentAt = 0;

  // FlashScore changes its markup from time to time, so every lookup tries a
  // list of selectors (oldest first) and uses the first one that matches.
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
    stage: [".event__stage--block", ".event__stage", ".event__time", "[class*='stage']"],
    homeFlag: [".event__logo--home.flag", "[class*='participant--home'] .flag"],
    awayFlag: [".event__logo--away.flag", "[class*='participant--away'] .flag"],
    // Crests are either an <img> nested inside the participant cell (football
    // club badges) or a sibling <img class="event__logo--home/away"> without
    // the "flag" class (basketball etc.; the "flag" variant is a <span>
    // handled by homeFlag/awayFlag above).
    homeLogo: [".event__participant--home img", "[class*='homeParticipant'] img", ".event__logo--home:not(.flag)"],
    awayLogo: [".event__participant--away img", "[class*='awayParticipant'] img", ".event__logo--away:not(.flag)"]
  };

  // FlashScore encodes the sport in the match id: g_<sportId>_<matchId>.
  const SPORT_BY_ID = {
    1: "football", 2: "tennis", 3: "basketball", 4: "hockey", 5: "american-football",
    6: "baseball", 7: "handball", 8: "rugby", 11: "futsal", 12: "volleyball",
    13: "cricket", 14: "darts", 15: "snooker", 16: "boxing", 17: "beach-volleyball",
    19: "rugby-league", 21: "badminton", 24: "motorsport", 28: "esports",
    29: "mma", 30: "table-tennis", 32: "motorsport"
  };

  // Localized FlashScore path/section names (flashscore.pt etc.) → canonical sport.
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
    boxe: "boxing",
    "auto-racing": "motorsport", "motorsport-auto-racing": "motorsport",
    automobilismo: "motorsport"
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

  // Looks for a link whose first path segment is a sport name
  // (league headers link to e.g. /tenis/atp-singles/...).
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
    // 1. Sport id encoded in the match element id (g_<sportId>_<matchId>).
    const m = /^g_(\d+)_/.exec(matchEl.id || "");
    if (m && SPORT_BY_ID[m[1]]) return SPORT_BY_ID[m[1]];

    // 2. Sport-named links inside the row or its league header.
    const fromLinks = sportFromLinks(matchEl) || sportFromLinks(headerEl);
    if (fromLinks) return fromLinks;

    // 3. An ancestor section sometimes carries the sport as a CSS class.
    for (let el = matchEl.parentElement; el; el = el.parentElement) {
      for (const cls of el.classList || []) {
        const sport = canonicalSport(cls);
        if (sport) return sport;
      }
    }

    // 4. The page URL (flashscore.com/tennis/, flashscore.pt/tenis/...).
    return canonicalSport(location.pathname.split("/").filter(Boolean)[0]) || "football";
  }

  const SUP_DIGITS = { "0": "⁰", "1": "¹", "2": "²", "3": "³", "4": "⁴",
                       "5": "⁵", "6": "⁶", "7": "⁷", "8": "⁸", "9": "⁹" };
  const toSuperscript = (s) => [...s].map((c) => SUP_DIGITS[c] || c).join("");

  // Per-period scores: halves in football, sets in tennis/volleyball, etc.
  // Tennis tiebreak scores live in a nested <sup>-like element; converting them
  // to superscript digits keeps "7(9)" from reading as "79".
  // Reads a single set/points column ("event__part--<side>--<i>"), folding any
  // tiebreak sup/sub into a superscript (e.g. 7⁹). Returns "" when absent.
  function scrapePart(matchEl, side, i) {
    const el = matchEl.querySelector(`.event__part--${side}.event__part--${i}`);
    if (!el) return "";
    let t = el.textContent.trim().replace(/\s+/g, "");
    const sup = el.querySelector("sup, sub, [class*='tiebreak' i]");
    if (sup) {
      const supText = sup.textContent.trim();
      if (supText && t.endsWith(supText) && t.length > supText.length)
        t = t.slice(0, -supText.length) + toSuperscript(supText);
    }
    return t;
  }

  function scrapeParts(matchEl, side, maxIndex = 7) {
    const out = [];
    for (let i = 1; i <= maxIndex; i++) {
      const t = scrapePart(matchEl, side, i);
      if (t && /\d/.test(t)) out.push(t);
    }
    return out;
  }

  // Current game points in a live tennis match (15/30/40), when present.
  function scrapePoints(matchEl, side) {
    const el = matchEl.querySelector(
      `.event__gamePart--${side}, [class*='gamePart'][class*='${side}']`
    );
    return el ? el.textContent.trim() : "";
  }

  // Which side is serving: FlashScore marks the server with
  // <svg class="serve-ico icon--serveHome|icon--serveAway"> in the row.
  function detectServing(matchEl) {
    if (matchEl.querySelector("[class*='serveHome' i]")) return "home";
    if (matchEl.querySelector("[class*='serveAway' i]")) return "away";
    // Fallback for older markup where the icon sits inside the participant cell.
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

  // FlashScore marks nationality with a CSS-sprite span carrying the
  // country name in its title (e.g. <span class="flag fl_24" title="Australia">).
  // Map that name to an ISO 3166-1 alpha-2 code so it can be shown as a flag image.
  const COUNTRY_TO_ISO2 = {
    Afghanistan: "AF", Albania: "AL", Algeria: "DZ", Andorra: "AD", Angola: "AO",
    Argentina: "AR", Armenia: "AM", Australia: "AU", Austria: "AT", Azerbaijan: "AZ",
    Bahamas: "BS", Bahrain: "BH", Bangladesh: "BD", Barbados: "BB", Belarus: "BY",
    Belgium: "BE", Belize: "BZ", Benin: "BJ", Bermuda: "BM", Bhutan: "BT",
    Bolivia: "BO", "Bosnia and Herzegovina": "BA", Botswana: "BW", Brazil: "BR",
    Brunei: "BN", Bulgaria: "BG", "Burkina Faso": "BF", Burundi: "BI", Cambodia: "KH",
    Cameroon: "CM", Canada: "CA", "Cape Verde": "CV", "Central African Republic": "CF",
    Chad: "TD", Chile: "CL", China: "CN", Colombia: "CO", Comoros: "KM", Congo: "CG",
    "Costa Rica": "CR", Croatia: "HR", Cuba: "CU", Curacao: "CW", Cyprus: "CY",
    "Czech Republic": "CZ", Czechia: "CZ", Denmark: "DK", Djibouti: "DJ",
    Dominica: "DM", "Dominican Republic": "DO", Ecuador: "EC", Egypt: "EG",
    "El Salvador": "SV", "Equatorial Guinea": "GQ", Eritrea: "ER", Estonia: "EE",
    Eswatini: "SZ", Ethiopia: "ET", "Faroe Islands": "FO", Fiji: "FJ", Finland: "FI",
    France: "FR", Gabon: "GA", Gambia: "GM", Georgia: "GE", Germany: "DE",
    Ghana: "GH", Gibraltar: "GI", Greece: "GR", Greenland: "GL", Grenada: "GD",
    Guatemala: "GT", Guinea: "GN", "Guinea-Bissau": "GW", Guyana: "GY", Haiti: "HT",
    Honduras: "HN", "Hong Kong": "HK", Hungary: "HU", Iceland: "IS", India: "IN",
    Indonesia: "ID", Iran: "IR", Iraq: "IQ", Ireland: "IE", Israel: "IL", Italy: "IT",
    "Ivory Coast": "CI", Jamaica: "JM", Japan: "JP", Jordan: "JO", Kazakhstan: "KZ",
    Kenya: "KE", Kosovo: "XK", Kuwait: "KW", Kyrgyzstan: "KG", Laos: "LA",
    Latvia: "LV", Lebanon: "LB", Lesotho: "LS", Liberia: "LR", Libya: "LY",
    Liechtenstein: "LI", Lithuania: "LT", Luxembourg: "LU", Madagascar: "MG",
    Malawi: "MW", Malaysia: "MY", Maldives: "MV", Mali: "ML", Malta: "MT",
    Mauritania: "MR", Mauritius: "MU", Mexico: "MX", Moldova: "MD", Monaco: "MC",
    Mongolia: "MN", Montenegro: "ME", Morocco: "MA", Mozambique: "MZ", Myanmar: "MM",
    Namibia: "NA", Nepal: "NP", Netherlands: "NL", "New Zealand": "NZ",
    Nicaragua: "NI", Niger: "NE", Nigeria: "NG", "North Korea": "KP",
    "North Macedonia": "MK", Norway: "NO", Oman: "OM", Pakistan: "PK",
    Palestine: "PS", Panama: "PA", "Papua New Guinea": "PG", Paraguay: "PY",
    Peru: "PE", Philippines: "PH", Poland: "PL", Portugal: "PT",
    "Puerto Rico": "PR", Qatar: "QA", Romania: "RO", Russia: "RU", Rwanda: "RW",
    "San Marino": "SM", "Saudi Arabia": "SA", Senegal: "SN", Serbia: "RS",
    "Sierra Leone": "SL", Singapore: "SG", Slovakia: "SK", Slovenia: "SI",
    Somalia: "SO", "South Africa": "ZA", "South Korea": "KR", "Korea Republic": "KR",
    "South Sudan": "SS", Spain: "ES", "Sri Lanka": "LK", Sudan: "SD",
    Suriname: "SR", Sweden: "SE", Switzerland: "CH", Syria: "SY", Taiwan: "TW",
    "Chinese Taipei": "TW", Tajikistan: "TJ", Tanzania: "TZ", Thailand: "TH",
    Togo: "TG", "Trinidad and Tobago": "TT", Tunisia: "TN", Turkey: "TR",
    Turkmenistan: "TM", Uganda: "UG", Ukraine: "UA", "United Arab Emirates": "AE",
    "United Kingdom": "GB", "United States": "US", USA: "US", Uruguay: "UY",
    Uzbekistan: "UZ", Venezuela: "VE", Vietnam: "VN", Yemen: "YE", Zambia: "ZM",
    Zimbabwe: "ZW"
  };

  function flagCode(countryName) {
    const code = COUNTRY_TO_ISO2[(countryName || "").trim()];
    return code ? code.toLowerCase() : "";
  }

  // Reads a "flag" span's title (country name) and returns the matching ISO2 code.
  function flagOf(root, selectors) {
    const el = q(root, selectors);
    return el ? flagCode(el.getAttribute("title") || "") : "";
  }

  // Reads the team/participant crest <img> src, if FlashScore renders one.
  function logoUrl(root, selectors) {
    const el = q(root, selectors);
    return el ? el.getAttribute("src") || "" : "";
  }

  // Walks up from a match row to find the league header that precedes it.
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

  // Ranking events (Formula 1, MotoGP...): no home/away duel, just a list of
  // drivers with times. The whole session becomes one game whose "ranking"
  // array carries the classification.
  const NODUEL_SEL = {
    section: ["[class*='sportName--noDuel']"],
    row: [".event__match--noDuel[data-event-row]", ".event__match--noDuel[id^='g_']"],
    title: ["[id^='header-league-title']", ".headerLeague__title", ".event__title"],
    sportName: [".sportHeader__name"],
    actions: [".headerLeague__actions"],
    info: [".event__header--info"],
    rank: [".event__rating"],
    name: [".event__participantName"],
    team: [".event__participantTeam"],
    time: [".event__result--time", ".event__center"],
    laps: [".event__resultLaps"],
    flag: [".event__participantName .flag", ".flag"]
  };

  function scrapeNoDuelSections() {
    const games = [];
    for (const section of qa(document, NODUEL_SEL.section)) {
      // Starred on the event header or on any individual driver row.
      if (!q(section, SEL.starActive)) continue;

      const rows = qa(section, NODUEL_SEL.row)
        .filter((r) => /^\d+\.?$/.test(text(r, NODUEL_SEL.rank)));
      if (!rows.length) continue;

      const ranking = rows.map((r) => ({
        rank: text(r, NODUEL_SEL.rank),
        name: text(r, NODUEL_SEL.name),
        team: text(r, NODUEL_SEL.team),
        time: text(r, NODUEL_SEL.time),
        laps: text(r, NODUEL_SEL.laps),
        flag: flagOf(r, NODUEL_SEL.flag)
      }));

      const actions = text(section, NODUEL_SEL.actions);
      const isLive = /live/i.test(actions) ||
        rows.some((r) => r.className.includes("--live"));
      const isFinished = /finished|ended|cancel/i.test(actions);
      // Scheduled sessions have no status badge; fall back to the start time
      // in the info line ("12.06.2026 16:00, Circuit de ...").
      const stage = actions ||
        (text(section, NODUEL_SEL.info).match(/\b\d{1,2}:\d{2}\b/) || [""])[0];

      const titleEl = q(section, NODUEL_SEL.title);
      const title = titleEl ? titleEl.textContent.trim().replace(/\s+/g, " ") : "";
      // Stable id: the session hash FlashScore puts in the header title id.
      const idm = /header-league-title-([\w-]+)/.exec((titleEl && titleEl.id) || "");
      games.push({
        id: idm ? `noduel_${idm[1]}` : `noduel_${title}`,
        sport: detectSport(rows[0], section),
        title,
        home: ranking[0].name,
        away: "",
        homeScore: ranking[0].time || "-",
        awayScore: "",
        homeParts: [], awayParts: [], homePoints: "", awayPoints: "",
        serving: "",
        ranking,
        stage,
        isLive,
        isFinished,
        competition: text(section, NODUEL_SEL.sportName) || title
      });
    }
    return games;
  }

  function scrapeStarredGames() {
    const games = [];
    for (const match of qa(document, SEL.match)) {
      if (match.className.includes("--noDuel")) continue; // ranking rows, handled above
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
      // Tennis sets live in part--1..5; the current game points sit in the
      // dedicated part--6 slot. Reading points straight from part--6 (instead
      // of popping the last set column) keeps a fresh "0 games" column from
      // being mistaken for "0 points" at the start of a game/set, and carries
      // the advantage marker ("A"/"AD") through unchanged.
      const tennis = sport === "tennis";
      const homeParts = scrapeParts(match, "home", tennis ? 5 : 7);
      const awayParts = scrapeParts(match, "away", tennis ? 5 : 7);
      let homePoints = scrapePoints(match, "home");
      let awayPoints = scrapePoints(match, "away");

      const GAME_POINTS = /^(0|15|30|40|A|AD)$/i;
      if (tennis && isLive && !homePoints && !awayPoints) {
        const h = scrapePart(match, "home", 6);
        const a = scrapePart(match, "away", 6);
        if (GAME_POINTS.test(h) && GAME_POINTS.test(a)) {
          homePoints = h;
          awayPoints = a;
        }
      }

      games.push({
        id: match.id || `${text(match, SEL.home)}-${text(match, SEL.away)}`,
        sport,
        home: text(match, SEL.home),
        away: text(match, SEL.away),
        homeFlag: flagOf(match, SEL.homeFlag),
        awayFlag: flagOf(match, SEL.awayFlag),
        homeLogo: logoUrl(match, SEL.homeLogo),
        awayLogo: logoUrl(match, SEL.awayLogo),
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
    return games.concat(scrapeNoDuelSections());
  }

  // WebSocket plumbing

  function setStatus(status) {
    try {
      chrome.storage.local.set({ trackerStatus: status, trackerStatusAt: Date.now() });
      chrome.runtime.sendMessage({ type: "status", status }).catch(() => {});
    } catch {
      // Extension context was invalidated (extension reloaded); nothing to do.
    }
  }

  function connect() {
    if (!enabled || (socket && socket.readyState <= WebSocket.OPEN)) return;
    try {
      socket = new WebSocket(`ws://127.0.0.1:${port}/`);
    } catch {
      scheduleReconnect();
      return;
    }

    socket.onopen = () => {
      reconnectDelay = RECONNECT_MIN_MS;
      lastPayloadHash = ""; // force a fresh send on (re)connect
      setStatus("connected");
      sendScores(true);
    };
    socket.onclose = () => {
      socket = null;
      setStatus("disconnected");
      scheduleReconnect();
    };
    socket.onerror = () => {
      if (socket) socket.close();
    };
  }

  function scheduleReconnect() {
    if (!enabled || reconnectTimer) return;
    reconnectTimer = setTimeout(() => {
      reconnectTimer = null;
      connect();
    }, reconnectDelay);
    reconnectDelay = Math.min(reconnectDelay * 2, RECONNECT_MAX_MS);
  }

  function sendScores(force = false) {
    const games = scrapeStarredGames();
    const hash = JSON.stringify(games);
    const heartbeatDue = Date.now() - lastSentAt > HEARTBEAT_MS;
    if (!force && hash === lastPayloadHash && !heartbeatDue) return;

    try {
      chrome.runtime.sendMessage({ type: "scores", games }).catch(() => {});
    } catch {}

    if (!socket || socket.readyState !== WebSocket.OPEN) return;
    socket.send(JSON.stringify({
      type: "scores",
      source: "flashscore",
      url: location.href,
      sentAt: Date.now(),
      games
    }));
    lastPayloadHash = hash;
    lastSentAt = Date.now();
  }

  // Settings and lifecycle

  chrome.storage.local.get({ trackerPort: DEFAULT_PORT, trackerEnabled: true }, (cfg) => {
    port = Number(cfg.trackerPort) || DEFAULT_PORT;
    enabled = cfg.trackerEnabled !== false;
    if (enabled) connect();
    else setStatus("paused");
  });

  chrome.storage.onChanged.addListener((changes, area) => {
    if (area !== "local") return;
    if (changes.trackerPort) {
      port = Number(changes.trackerPort.newValue) || DEFAULT_PORT;
      if (socket) socket.close();
    }
    if (changes.trackerEnabled) {
      enabled = changes.trackerEnabled.newValue !== false;
      if (!enabled && socket) {
        socket.close();
        setStatus("paused");
      } else if (enabled) {
        connect();
      }
    }
  });

  // React quickly to score changes, with the interval as a safety net.
  const observer = new MutationObserver(() => sendScores());
  observer.observe(document.body, { childList: true, subtree: true, characterData: true });
  setInterval(() => {
    if (enabled) sendScores();
  }, SCRAPE_INTERVAL_MS);
})();
