/** Stable, pleasant colours for books without a cover image. */
const palettes: [string, string][] = [
  ["#7a4520", "#b97a45"],
  ["#2f4858", "#5b7f95"],
  ["#4a3b5c", "#7d6a96"],
  ["#3e5a3a", "#73906b"],
  ["#6b2e2e", "#a35a52"],
  ["#5a4a2a", "#9a8350"],
  ["#2d4a4a", "#5f8a84"],
  ["#583a2e", "#93685a"],
];

export function hashString(value: string): number {
  let hash = 2166136261;
  for (let i = 0; i < value.length; i++) {
    hash ^= value.charCodeAt(i);
    hash = Math.imul(hash, 16777619);
  }

  return hash >>> 0;
}

export function placeholderPalette(title: string): [string, string] {
  return palettes[hashString(title) % palettes.length];
}
