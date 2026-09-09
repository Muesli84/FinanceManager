const fs = require('fs');
const path = require('path');
const { PNG } = require('pngjs');
const { GIFEncoder, quantize, applyPalette } = require('gifenc');

const repoRoot = path.resolve(__dirname, '..', '..');
const sourceDir = path.join(repoRoot, 'Docs', 'screenshots');
const outputFile = path.join(sourceDir, 'demo.gif');

const frames = [
  'register.png',
  'home.png',
  'accounts.png',
  'statement-drafts.png',
  'contacts.png',
  'savings-plans.png',
  'securities.png',
  'budget-purposes.png',
  'reports.png',
  'budget-report.png'
];

const targetWidth = 1280;
const targetHeight = 900;

function readCroppedPng(fileName) {
  const filePath = path.join(sourceDir, fileName);
  const png = PNG.sync.read(fs.readFileSync(filePath));
  const target = Buffer.alloc(targetWidth * targetHeight * 4, 255);

  const copyWidth = Math.min(png.width, targetWidth);
  const copyHeight = Math.min(png.height, targetHeight);

  for (let y = 0; y < copyHeight; y++) {
    for (let x = 0; x < copyWidth; x++) {
      const src = (y * png.width + x) * 4;
      const dst = (y * targetWidth + x) * 4;
      target[dst] = png.data[src];
      target[dst + 1] = png.data[src + 1];
      target[dst + 2] = png.data[src + 2];
      target[dst + 3] = png.data[src + 3];
    }
  }

  return new Uint8Array(target);
}

(async () => {
  const gif = GIFEncoder();
  let frameIndex = 0;

  for (const fileName of frames) {
    if (!fs.existsSync(path.join(sourceDir, fileName))) {
      console.warn(`Skipping missing file: ${fileName}`);
      continue;
    }

    const rgba = readCroppedPng(fileName);
    const palette = quantize(rgba, 256, { format: 'rgb565' });
    const index = applyPalette(rgba, palette, 'rgb565');

    gif.writeFrame(index, targetWidth, targetHeight, {
      palette,
      delay: 2000,
      repeat: frameIndex === 0 ? 0 : undefined
    });

    frameIndex++;
  }

  gif.finish();
  const bytes = gif.bytes();
  fs.writeFileSync(outputFile, Buffer.from(bytes));
  console.log(`GIF saved to ${outputFile} (${(bytes.length / 1024 / 1024).toFixed(2)} MB)`);
})();
