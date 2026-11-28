import { describe, expect, it } from "vitest";
import { defaultSettings, fonts, typographyCss } from "./readerSettings";

describe("readerSettings", () => {
  it("uses the book's own font by default, paginated", () => {
    expect(defaultSettings.font).toBe("publisher");
    expect(defaultSettings.flow).toBe("paginated");
  });

  it("falls back to Literata, without overriding fonts the book sets", () => {
    const css = typographyCss(defaultSettings);
    // Zero specificity: any font-family in the book's own CSS wins.
    expect(css).toContain(`:where(html) { font-family: ${fonts.literata.stack}; }`);
    expect(css).not.toContain("font-family: 'CR Literata', Georgia, serif !important");
  });

  it("forces a chosen font over the book's", () => {
    const css = typographyCss({ ...defaultSettings, font: "atkinson" });
    expect(css).toContain(`body { font-family: ${fonts.atkinson.stack} !important; }`);
    expect(css).not.toContain(":where(html)");
  });
});
