const fs = require('fs');

async function main() {
  const [playwrightPath, executablePath, inputPath, outputPath] = process.argv.slice(2);
  const svg = fs.readFileSync(inputPath);
  const source = svg.toString('utf8');
  const root = source.match(/<svg\b([^>]*)>/i);
  if (!root) throw new Error('El archivo no contiene un elemento SVG');
  const attr = (name) => {
    const match = root[1].match(new RegExp('(?:^|\\s)' + name + '\\s*=\\s*["\']([^"\']+)["\']', 'i'));
    return match ? match[1] : '';
  };
  const box = attr('viewBox').trim().split(/[\s,]+/).map(Number);
  let width = parseFloat(attr('width'));
  let height = parseFloat(attr('height'));
  if (!Number.isFinite(width) || width <= 0) width = box.length === 4 && box[2] > 0 ? box[2] : 1024;
  if (!Number.isFinite(height) || height <= 0) height = box.length === 4 && box[3] > 0 ? box[3] : 1024;
  const scale = Math.min(2048 / Math.max(width, height), Math.max(1, 1024 / Math.max(width, height)));
  width = Math.max(1, Math.round(width * scale));
  height = Math.max(1, Math.round(height * scale));
  const { chromium } = require(playwrightPath);
  const browser = await chromium.launch({ executablePath, headless: true, args: ['--no-sandbox', '--disable-background-networking'] });
  try {
    const context = await browser.newContext({ viewport: { width, height }, javaScriptEnabled: false });
    await context.route('**/*', route => route.abort());
    const page = await context.newPage();
    const data = 'data:image/svg+xml;base64,' + svg.toString('base64');
    await page.setContent('<meta http-equiv="Content-Security-Policy" content="default-src \'none\'; img-src data:; style-src \'unsafe-inline\'"><style>html,body{margin:0;width:100%;height:100%;background:transparent}img{display:block;width:100%;height:100%;object-fit:contain}</style><img src="' + data + '">', { waitUntil: 'load' });
    const valid = await page.locator('img').evaluate(image => image.complete && image.naturalWidth > 0);
    if (!valid) throw new Error('El navegador no pudo interpretar el SVG');
    await page.screenshot({ path: outputPath, type: 'png', omitBackground: true, animations: 'disabled' });
    await context.close();
  } finally {
    await browser.close();
  }
}

main().catch(error => { console.error(error && error.message ? error.message : String(error)); process.exitCode = 1; });
