/**
 * Geotrack – image conversion script
 *
 * Kako koristiti:
 *   1. Ubaci originalne slike (jpg, png, webp, avif…) u:
 *      src/server/Geotrack/Geotrack.Server/media/source/
 *
 *   2. U tom folderu pokreni:
 *      cd scripts
 *      npm install       (samo prvi put)
 *      npm run convert
 *
 *   3. Konvertovane .webp slike se pojavljuju u:
 *      src/server/Geotrack/Geotrack.Server/media/seed/
 *      → naziv fajla ostaje isti, samo je ekstenzija .webp
 *
 *   4. U SeedData.cs referencuj ih sa:
 *      SeedWebp("naziv-fajla")   // bez ekstenzije
 *
 * Napomena:
 *   - source/ folder je gitignored (originali mogu biti veliki)
 *   - seed/*.webp se commituju (WebP je mali, ~70% manji od JPEG/PNG)
 *   - Slike se resizuju na max 1400px širine (proporcionalno)
 *   - Kvalitet: 82 (dobar balans kvalitet/veličina)
 */

import sharp from 'sharp';
import { readdir, mkdir } from 'fs/promises';
import { existsSync } from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

const MEDIA_ROOT = path.resolve(
  __dirname,
  '../src/server/Geotrack/Geotrack.Server/media'
);
const SOURCE_DIR = path.join(MEDIA_ROOT, 'source');
const OUTPUT_DIR = path.join(MEDIA_ROOT, 'seed');

const WEBP_QUALITY = 82;
const MAX_WIDTH = 1400;

const SUPPORTED_EXTENSIONS = new Set([
  '.jpg', '.jpeg', '.png', '.webp', '.avif', '.tiff', '.tif', '.bmp',
]);

async function main() {
  // Provjeri postoji li source folder
  if (!existsSync(SOURCE_DIR)) {
    await mkdir(SOURCE_DIR, { recursive: true });
    console.log(`\nKreiran source folder: ${SOURCE_DIR}`);
    console.log('Ubaci slike u taj folder pa ponovo pokreni skriptu.\n');
    return;
  }

  // Osiguraj da output folder postoji
  await mkdir(OUTPUT_DIR, { recursive: true });

  const files = await readdir(SOURCE_DIR);
  const imageFiles = files.filter(f =>
    SUPPORTED_EXTENSIONS.has(path.extname(f).toLowerCase())
  );

  if (imageFiles.length === 0) {
    console.log(`\nNema slika u: ${SOURCE_DIR}`);
    console.log(`Ubaci .jpg/.png/.webp fajlove u taj folder.\n`);
    return;
  }

  console.log(`\nKonvertujem ${imageFiles.length} slika/e → .webp\n`);

  let ok = 0;
  let skip = 0;
  let fail = 0;

  for (const file of imageFiles) {
    const baseName = path.basename(file, path.extname(file));
    const inputPath = path.join(SOURCE_DIR, file);
    const outputPath = path.join(OUTPUT_DIR, `${baseName}.webp`);

    try {
      const image = sharp(inputPath);
      const meta = await image.metadata();

      // Resize samo ako je šira od MAX_WIDTH
      if (meta.width && meta.width > MAX_WIDTH) {
        image.resize({ width: MAX_WIDTH, withoutEnlargement: true });
      }

      await image
        .webp({ quality: WEBP_QUALITY })
        .toFile(outputPath);

      const inputSize = Math.round((await import('fs')).statSync(inputPath).size / 1024);
      const outputSize = Math.round((await import('fs')).statSync(outputPath).size / 1024);
      const saving = Math.round((1 - outputSize / inputSize) * 100);

      console.log(
        `  ✓  ${baseName}.webp` +
        `  (${inputSize} KB → ${outputSize} KB, -${saving}%)`
      );
      ok++;
    } catch (err) {
      console.error(`  ✗  ${file}  →  ${err.message}`);
      fail++;
    }
  }

  console.log(`\nGotovo: ${ok} konvertovano, ${skip} preskočeno, ${fail} grešaka`);

  if (ok > 0) {
    console.log('\nSledeci koraci:');
    console.log('  1. Dodaj slike u SeedData.cs koristeći SeedWebp("naziv-fajla")');
    console.log('  2. git add src/server/Geotrack/Geotrack.Server/media/seed/*.webp');
    console.log('  3. Commituj (source/ folder je automatski ignorisan)\n');
  }
}

main().catch(err => {
  console.error('\nGreška:', err.message);
  process.exit(1);
});
