/* Render the generated SVGs with sharp. Asset tooling only; no app dependency.
 * Usage: node tools/branding/rasterize.cjs [path/to/node_modules]
 */
const fs = require('node:fs/promises');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const sharp = process.argv[2] ? require(path.join(process.argv[2], 'sharp')) : require('sharp');

(async () => {
  const jobs = JSON.parse(await fs.readFile(path.join(root, 'assets/brand/render-jobs.json'), 'utf8'));
  for (const job of jobs) {
    await sharp(path.join(root, job.svg), {density: 384})
      .resize(job.width, job.height, {fit: 'fill'})
      .png({compressionLevel: 9}).toFile(path.join(root, job.png));
  }
  console.log(JSON.stringify({rendered: jobs.length}));
})().catch(error => { console.error(error.message); process.exitCode = 1; });
