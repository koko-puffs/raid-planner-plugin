// Renders tools/icon.svg to RaidPlanner/images/icon.png (512×512, what Dalamud's installer shows).
// Needs sharp: `npm i -g sharp` or run from a folder where it's installed. ImageMagick with an SVG
// delegate works too: magick -background none tools/icon.svg -resize 512x512 RaidPlanner/images/icon.png
import { createRequire } from 'node:module'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const here = dirname(fileURLToPath(import.meta.url))
const require = createRequire(import.meta.url)
const sharp = require(process.env.SHARP_PATH ?? 'sharp')

await sharp(join(here, 'icon.svg'), { density: 72 })
  .resize(512, 512)
  .png({ compressionLevel: 9 })
  .toFile(join(here, '..', 'RaidPlanner', 'images', 'icon.png'))
console.log('Wrote RaidPlanner/images/icon.png')
