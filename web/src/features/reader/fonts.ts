// Font files for the book iframe. Fonts loaded by the app page aren't visible inside the
// iframe's document, so the reader declares them again there.
import literataLatin from "@fontsource-variable/literata/files/literata-latin-wght-normal.woff2?url";
import literataLatinItalic from "@fontsource-variable/literata/files/literata-latin-wght-italic.woff2?url";
import literataLatinExt from "@fontsource-variable/literata/files/literata-latin-ext-wght-normal.woff2?url";
import literataLatinExtItalic from "@fontsource-variable/literata/files/literata-latin-ext-wght-italic.woff2?url";
import atkinson400 from "@fontsource/atkinson-hyperlegible/files/atkinson-hyperlegible-latin-400-normal.woff2?url";
import atkinson400Italic from "@fontsource/atkinson-hyperlegible/files/atkinson-hyperlegible-latin-400-italic.woff2?url";
import atkinson700 from "@fontsource/atkinson-hyperlegible/files/atkinson-hyperlegible-latin-700-normal.woff2?url";
import atkinson700Italic from "@fontsource/atkinson-hyperlegible/files/atkinson-hyperlegible-latin-700-italic.woff2?url";

const latin = "U+0000-00FF,U+0131,U+0152-0153,U+02BB-02BC,U+02C6,U+02DA,U+02DC,U+0304,U+0308,U+0329,U+2000-206F,U+20AC,U+2122,U+2191,U+2193,U+2212,U+2215,U+FEFF,U+FFFD";
const latinExt = "U+0100-02BA,U+02BD-02C5,U+02C7-02CC,U+02CE-02D7,U+02DD-02FF,U+0304,U+0308,U+0329,U+1D00-1DBF,U+1E00-1E9F,U+1EF2-1EFF,U+2020,U+20A0-20AB,U+20AD-20C0,U+2113,U+2C60-2C7F,U+A720-A7FF";

const face = (family: string, url: string, style: string, weight: string, range: string) =>
  `@font-face{font-family:'${family}';font-style:${style};font-weight:${weight};font-display:block;src:url(${url}) format('woff2');unicode-range:${range};}`;

export const readerFontFaces = [
  face("CR Literata", literataLatin, "normal", "200 900", latin),
  face("CR Literata", literataLatinItalic, "italic", "200 900", latin),
  face("CR Literata", literataLatinExt, "normal", "200 900", latinExt),
  face("CR Literata", literataLatinExtItalic, "italic", "200 900", latinExt),
  face("CR Atkinson", atkinson400, "normal", "400", latin),
  face("CR Atkinson", atkinson400Italic, "italic", "400", latin),
  face("CR Atkinson", atkinson700, "normal", "700", latin),
  face("CR Atkinson", atkinson700Italic, "italic", "700", latin),
].join("\n");
