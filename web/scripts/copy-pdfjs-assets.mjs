// Copies the static files PDF.js loads at runtime (character maps, standard fonts, WASM decoders,
// colour profiles) from pdfjs-dist into public/pdfjs, so they ship with the app instead of a CDN.
import { cpSync, existsSync, rmSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const source = join(root, "node_modules", "pdfjs-dist");
const target = join(root, "public", "pdfjs");

rmSync(target, { recursive: true, force: true });
for (const folder of ["cmaps", "standard_fonts", "wasm", "iccs"]) {
  if (existsSync(join(source, folder))) {
    cpSync(join(source, folder), join(target, folder), { recursive: true });
  }
}

console.log("Copied PDF.js assets to public/pdfjs");
