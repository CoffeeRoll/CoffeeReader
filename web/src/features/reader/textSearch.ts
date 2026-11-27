// The browser half of in-book search. It folds text exactly like the server's TextSearcher
// (src/CoffeeReader.Application/Reading/TextSearcher.cs), so "occurrence N in chapter X" from the
// server lands on the same words here.

const spaces = new Set([0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x20, 0x85, 0xa0, 0x1680, 0x2028, 0x2029, 0x202f, 0x205f, 0x3000, 0xfeff]);
const nonSpacingMark = /\p{Mn}/u;

export function isSpace(codePoint: number): boolean {
  return spaces.has(codePoint) || (codePoint >= 0x2000 && codePoint <= 0x200a);
}

/** Folds case, accents and runs of whitespace; `map[i]` is the source index of folded unit `i`. */
export function fold(text: string): { folded: string; map: number[] } {
  let folded = "";
  const map: number[] = [];
  let lastWasSpace = true;
  let index = 0;
  for (const char of text) {
    const codePoint = char.codePointAt(0)!;
    if (isSpace(codePoint)) {
      if (!lastWasSpace) {
        folded += " ";
        map.push(index);
        lastWasSpace = true;
      }
    } else {
      for (const part of char.normalize("NFD")) {
        if (nonSpacingMark.test(part)) continue;
        const lower = part.toLowerCase();
        for (let i = 0; i < lower.length; i++) {
          folded += lower[i];
          map.push(index);
        }

        lastWasSpace = false;
      }
    }

    index += char.length;
  }

  return { folded, map };
}

export function foldQuery(query: string): string {
  return fold(query).folded.trim();
}

/** Finds the Nth non-overlapping match, as [start, end) offsets into `text`. */
export function findOccurrence(text: string, query: string, occurrence: number): [number, number] | null {
  const needle = foldQuery(query);
  if (!needle) return null;
  const { folded, map } = fold(text);
  let position = 0;
  let count = 0;
  let first: [number, number] | null = null;
  while (position <= folded.length - needle.length) {
    const found = folded.indexOf(needle, position);
    if (found < 0) break;
    const start = map[found];
    const lastSource = map[found + needle.length - 1];
    const end = lastSource + (text.codePointAt(lastSource)! > 0xffff ? 2 : 1);
    first ??= [start, end];
    if (count === occurrence) return [start, end];
    count++;
    position = found + needle.length;
  }

  // If the count drifted (unusual markup), the first match is better than nothing.
  return first;
}
