// Selector regression test: runs the scraping logic from extension/content.js
// against a saved FlashScore row (tools/test-row.html) in headless Chrome.
//
// When FlashScore changes its markup and the bar stops showing data:
//   1. On flashscore.com, right-click a starred match row → Inspect → copy the
//      row's outer HTML into tools/test-row.html.
//   2. npm install && npm run test:scrape
//   3. Fix the selectors in extension/content.js AND SportsOverlayApp/Resources/scraper.js
//      (they must stay in sync) until the output is correct again.
const fs = require("fs");
const path = require("path");
// puppeteer-core: no bundled browser; we drive the system-installed Chrome/Edge.
const puppeteer = require("puppeteer-core");

const BROWSERS = [
  "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
  "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
  "C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe"
];

const ROW_HTML = fs.readFileSync(path.join(__dirname, "test-row.html"), "utf8");

(async () => {
  const executablePath = BROWSERS.find(fs.existsSync);
  if (!executablePath) {
    console.error("No Chrome/Edge found — edit BROWSERS in this script.");
    process.exit(1);
  }

  const browser = await puppeteer.launch({ headless: "new", executablePath });
  const page = await browser.newPage();
  await page.setContent(`<body>${ROW_HTML}</body>`);

  // content.js is an IIFE that needs chrome.* — instead of loading it whole,
  // extract and eval just the pure scraping helpers.
  const src = fs.readFileSync(path.join(__dirname, "..", "extension", "content.js"), "utf8");
  const start = src.indexOf("const SEL");
  const end = src.indexOf("WebSocket plumbing");
  if (start < 0 || end < 0) {
    console.error("Slice markers not found in content.js — update this test.");
    process.exit(1);
  }
  // Cut just before the comment line containing the end marker.
  const body = src.slice(start, src.lastIndexOf("\n", end));

  const result = await page.evaluate((code) => {
    eval(code);
    return scrapeStarredGames();
  }, body);

  console.log(JSON.stringify(result, null, 2));
  await browser.close();
})();
