import { describe, expect, it } from "vitest";
import { findOccurrence, fold } from "./textSearch";

describe("textSearch", () => {
  it("folds case, accents and whitespace like the server", () => {
    expect(fold("Café  OWNER\n\tlater").folded).toBe("cafe owner later");
  });

  it("finds the nth occurrence as offsets into the original text", () => {
    const text = "The Café was quiet.\n   Later, the   cafe OWNER returned to the CAFÉ.";
    const [start, end] = findOccurrence(text, "cafe", 2)!;
    expect(text.slice(start, end)).toBe("CAFÉ");
    const phrase = findOccurrence(text, "cafe owner", 0)!;
    expect(text.slice(phrase[0], phrase[1])).toBe("cafe OWNER");
  });

  it("handles decomposed accents and astral characters", () => {
    const text = "résumé 📚 résumé";
    const [start, end] = findOccurrence(text, "resume", 1)!;
    expect(text.slice(start, end)).toBe("résumé");
  });
});
