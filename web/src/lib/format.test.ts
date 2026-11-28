import { describe, expect, it } from "vitest";
import { authorsFieldSeparator, authorsParts, authorsText, splitAuthorsField } from "./format";

describe("authors", () => {
  it("writes names the same way everywhere, never joining people with '&'", () => {
    expect(authorsText(["Black & Decker"])).toBe("Black & Decker");
    expect(authorsText(["Black", "Decker"])).toBe("Black and Decker");
    expect(authorsText(["Ann Lee", "Bo Chen", "Cy Diaz"], { max: Infinity })).toBe("Ann Lee, Bo Chen, and Cy Diaz");
    expect(authorsText(["Ann Lee", "Bo Chen", "Cy Diaz"])).toBe("Ann Lee and 2 others");
    expect(authorsText([])).toBe("Unknown author");
    expect(authorsText([], { fallback: "" })).toBe("");
  });

  it("splits the same wording into names for links", () => {
    expect(authorsParts(["Black & Decker", "Jane Smith"])).toEqual([
      { name: true, value: "Black & Decker" },
      { name: false, value: " and " },
      { name: true, value: "Jane Smith" },
    ]);
  });

  it("only separates people with ';' in the edit form, and round-trips", () => {
    expect(splitAuthorsField("Black & Decker")).toEqual(["Black & Decker"]);
    expect(splitAuthorsField(" Smith, Jane ;Doe,  John; ")).toEqual(["Smith, Jane", "Doe, John"]);
    const names = ["Black & Decker", "John Wiley & Sons, Inc."];
    expect(splitAuthorsField(names.join(authorsFieldSeparator))).toEqual(names);
  });
});
