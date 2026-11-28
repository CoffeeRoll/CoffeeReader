import { bookFileUrl } from "../../api/client";
import type { Chapter } from "../../api/types";
import { readerFontFaces } from "./fonts";
import { findOccurrence } from "./textSearch";

export type FlowMode = "paginated" | "scrolled";

/** Geometry and styling for one rendered page (or two-page spread), or a scrolling column. */
export interface PageLayout {
  /** Pages side by side, or one continuous column that scrolls vertically. */
  mode: FlowMode;
  width: number;
  height: number;
  columns: 1 | 2;
  /** Horizontal margin on each side of every column. */
  margin: number;
  /** Vertical padding above and below the text. */
  padding: number;
  /** Scroll mode: how far code blocks too wide for the text may extend into the side margins. */
  bleed?: number;
  css: string;
  animate: boolean;
}

export type PageTarget =
  | { kind: "fraction"; value: number }
  | { kind: "end" }
  | { kind: "fragment"; id: string }
  | { kind: "search"; query: string; occurrence: number };

export interface Relocation {
  page: number;
  pageCount: number;
  /** Position of the first visible character as a fraction of the chapter's text. */
  fraction: number;
  /** False while a page turn animation is still running. */
  settled: boolean;
  /** Scroll mode: how far down the chapter the view is, from 0 at the top to 1 at the bottom. */
  scrolled?: number;
}

export interface LinkTarget {
  path: string;
  fragment: string | null;
}

export interface PaginatorEvents {
  onRelocate(location: Relocation): void;
  onTap(zone: "prev" | "next" | "middle"): void;
  onSwipe(direction: 1 | -1): void;
  onLink(target: LinkTarget): void;
  onKey(event: KeyboardEvent): void;
  onWheel(event: WheelEvent): void;
  /** The "next chapter" control at the end of a chapter (scroll mode) was chosen. */
  onChapterEnd(): void;
  /** Whether there's another chapter in that direction (for rubber-banding at the ends). */
  canLeave(direction: 1 | -1): boolean;
}

interface TextIndex {
  nodes: Text[];
  starts: number[];
  lookup: Map<Node, number>;
  total: number;
}

const TURN_MS = 300;
const NAV_ID = "cr-chapter-nav";

export interface LoadOptions {
  /** Label for the control at the end of the chapter in scroll mode, e.g. "Next: Chapter 4". */
  nextLabel?: string | null;
}

/**
 * Lays a chapter out inside a same-origin iframe, either as pages (CSS multi-column layout, moving
 * between pages by translating the body) or as one column that scrolls. Book content never runs
 * scripts: the server sanitizes it and the app's Content-Security-Policy (inherited by the srcdoc
 * iframe) forbids them.
 */
export class Paginator {
  private doc: Document | null = null;
  private layout: PageLayout | null = null;
  private dir: "ltr" | "rtl" = "ltr";
  private page = 0;
  private pageCount = 1;
  private text: TextIndex | null = null;
  private loadToken = 0;
  private detachers: (() => void)[] = [];
  private settleTimer = 0;
  private relayoutTimer = 0;
  private touch: { x: number; y: number; time: number; dragging: boolean; dx: number; moved: number } | null = null;
  private suppressClickUntil = 0;
  private scrollFrame = 0;
  private readonly iframe: HTMLIFrameElement;
  private readonly bookId: number;
  private readonly events: PaginatorEvents;

  constructor(iframe: HTMLIFrameElement, bookId: number, events: PaginatorEvents) {
    this.iframe = iframe;
    this.bookId = bookId;
    this.events = events;
  }

  get currentPage() {
    return this.page;
  }

  get totalPages() {
    return this.pageCount;
  }

  get direction() {
    return this.dir;
  }

  private get scrolled() {
    return this.layout?.mode === "scrolled";
  }

  /** The element that scrolls in scroll mode (the iframe's document). */
  private get scroller(): HTMLElement | null {
    return (this.doc?.scrollingElement as HTMLElement | null) ?? null;
  }

  async load(chapter: Chapter, layout: PageLayout, target: PageTarget, options: LoadOptions = {}): Promise<boolean> {
    const token = ++this.loadToken;
    this.detach();
    this.layout = layout;
    this.dir = chapter.direction === "rtl" ? "rtl" : "ltr";
    this.page = 0;

    await new Promise<void>((resolve) => {
      const timeout = window.setTimeout(resolve, 6000);
      this.iframe.addEventListener(
        "load",
        () => {
          window.clearTimeout(timeout);
          resolve();
        },
        { once: true },
      );
      this.iframe.srcdoc = this.buildDocument(chapter, layout, options.nextLabel ?? null);
    });
    if (token !== this.loadToken) return false;

    const doc = this.iframe.contentDocument;
    if (!doc?.body) return false;
    this.doc = doc;

    // The window may have been resized while the chapter loaded; lay out for the current size.
    const current = this.layout ?? layout;
    if (current !== layout) {
      doc.getElementById("cr-typography")!.textContent = current.css;
      doc.getElementById("cr-layout")!.textContent = this.layoutCss(current);
    }

    await this.waitForFonts(doc);
    if (token !== this.loadToken) return false;

    this.attach(doc);
    this.markImagePage(doc);
    this.indexText();
    this.widenCode();
    this.measure();
    if (this.scrolled) {
      this.scrollToTarget(target);
    } else {
      this.page = this.pageFor(target);
      this.applyTransform(false);
    }

    this.emit(true);
    return true;
  }

  /** Applies new geometry or typography, keeping the reader on the same sentence. */
  async relayout(layout: PageLayout): Promise<void> {
    const doc = this.doc;
    if (!doc) {
      this.layout = layout;
      return;
    }

    const fraction = this.readFraction();
    this.layout = layout;
    doc.getElementById("cr-typography")!.textContent = layout.css;
    doc.getElementById("cr-layout")!.textContent = this.layoutCss(layout);
    await this.waitForFonts(doc);
    this.page = 0;
    this.applyTransform(false);
    if (this.scroller) this.scroller.scrollTop = 0;
    this.widenCode();
    this.measure();
    if (this.scrolled) {
      this.scrollToTarget({ kind: "fraction", value: fraction });
    } else {
      this.page = this.pageFor({ kind: "fraction", value: fraction });
      this.applyTransform(false);
    }

    this.emit(true);
  }

  /** Moves forward a page (or a screen in scroll mode). False at the end of the chapter. */
  next(): boolean {
    if (this.scrolled) return this.scrollScreen(1);
    if (this.page >= this.pageCount - 1) return false;
    this.goTo(this.page + 1, true);
    return true;
  }

  prev(): boolean {
    if (this.scrolled) return this.scrollScreen(-1);
    if (this.page <= 0) return false;
    this.goTo(this.page - 1, true);
    return true;
  }

  /** Scroll mode: moves the column by some pixels (wheel outside the page, arrow keys). */
  scrollBy(delta: number, smooth = false) {
    this.scroller?.scrollBy({ top: delta, behavior: smooth ? "smooth" : "instant" });
  }

  goTo(page: number, animate = false) {
    if (this.scrolled) {
      const scroller = this.scroller;
      if (scroller) scroller.scrollTop = page * scroller.clientHeight;
      return;
    }

    this.page = Math.max(0, Math.min(this.pageCount - 1, page));
    this.applyTransform(animate && !!this.layout?.animate);
    this.emit(!(animate && this.layout?.animate));
  }

  /** Returns a dragged page to rest (e.g. after a swipe that went nowhere). */
  settle() {
    this.applyTransform(!!this.layout?.animate);
  }

  goToTarget(target: PageTarget) {
    if (this.scrolled) {
      this.scrollToTarget(target);
      this.emit(true);
      return;
    }

    this.snap();
    this.goTo(this.pageFor(target), false);
  }

  /** The page (or screen, in scroll mode) an element with this id is on, or null. */
  pageOfFragment(id: string): number | null {
    const element = this.findFragment(id);
    if (!element) return null;
    if (this.scrolled) {
      const scroller = this.scroller!;
      return Math.floor((element.getBoundingClientRect().top + scroller.scrollTop + 1) / Math.max(1, scroller.clientHeight));
    }

    return this.pageOfNode(element);
  }

  clearHighlight() {
    const win = this.iframe.contentWindow as (Window & typeof globalThis) | null;
    const highlights = (win?.CSS as unknown as { highlights?: Map<string, unknown> } | undefined)?.highlights;
    highlights?.delete("cr-search");
    win?.getSelection()?.removeAllRanges();
  }

  destroy() {
    this.loadToken++;
    this.detach();
    window.clearTimeout(this.settleTimer);
    window.clearTimeout(this.relayoutTimer);
    cancelAnimationFrame(this.scrollFrame);
    this.scrollFrame = 0;
  }

  // ---- Document ------------------------------------------------------------------------------------

  private buildDocument(chapter: Chapter, layout: PageLayout, nextLabel: string | null): string {
    const escape = (value: string) => value.replace(/&/g, "&amp;").replace(/"/g, "&quot;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
    const chapterNav = nextLabel ? `<nav id="${NAV_ID}" aria-label="Chapter"><button type="button" data-cr-next>${escape(nextLabel)} →</button></nav>` : "";
    const directory = chapter.href.includes("/") ? chapter.href.slice(0, chapter.href.lastIndexOf("/")) : "";
    const base = directory ? `${bookFileUrl(this.bookId, directory)}/` : `/api/books/${this.bookId}/files/`;
    const stylesheets = chapter.stylesheets.map((path) => `<link rel="stylesheet" href="${escape(bookFileUrl(this.bookId, path))}">`).join("");
    const inlineStyles = chapter.inlineStyles.map((css) => `<style>${css}</style>`).join("");
    const bodyAttributes = [
      chapter.bodyId ? ` id="${escape(chapter.bodyId)}"` : "",
      chapter.bodyClass ? ` class="${escape(chapter.bodyClass)}"` : "",
      chapter.bodyStyle ? ` style="${escape(chapter.bodyStyle)}"` : "",
    ].join("");
    const lang = chapter.language ? ` lang="${escape(chapter.language)}"` : "";

    return `<!DOCTYPE html><html${lang} dir="${this.dir}"><head><meta charset="utf-8"><base href="${escape(base)}">` +
      `<style>${readerFontFaces}</style>${stylesheets}${inlineStyles}` +
      `<style id="cr-typography">${layout.css}</style><style id="cr-layout">${this.layoutCss(layout)}</style>` +
      `</head><body${bodyAttributes}>${absolutizeUrls(chapter.html, new URL(base, window.location.href))}<div id="cr-end"></div>${chapterNav}</body></html>`;
  }

  private layoutCss(layout: PageLayout): string {
    const { width, height, columns, margin, padding } = layout;
    const textHeight = height - padding * 2;
    const shared = `
      img, svg, video, object, canvas { max-width: 100% !important; max-height: ${textHeight}px !important;
        box-sizing: border-box; object-fit: contain; break-inside: avoid; }
      img { height: auto; }
      figure, table { break-inside: avoid; }
      figure { margin-left: 0; margin-right: 0; }
      #cr-end { display: block; height: 0; margin: 0; padding: 0; border: 0; }
      body.cr-image-page { text-align: center !important; }
      body.cr-image-page * { margin-top: 0 !important; margin-bottom: 0 !important; padding-top: 0 !important; padding-bottom: 0 !important; }
      body.cr-image-page img { display: block !important; margin: 0 auto !important; width: auto !important; height: auto !important; max-height: ${textHeight}px !important; }
      body.cr-image-page svg { display: block !important; margin: 0 auto !important; width: 100% !important; height: ${textHeight}px !important; }
    `;

    if (layout.mode === "scrolled") {
      return `
        html { margin: 0 !important; padding: 0 !important; width: auto !important; height: auto !important;
          overflow-x: hidden !important; overflow-y: auto !important; background: transparent !important;
          touch-action: manipulation; scrollbar-width: thin; -webkit-text-size-adjust: none; text-size-adjust: none;
          -webkit-user-select: text; user-select: text; }
        body { box-sizing: border-box !important; margin: 0 !important; width: auto !important; height: auto !important;
          min-width: 0 !important; max-width: none !important; min-height: 0 !important; max-height: none !important;
          padding: ${padding + 8}px ${margin}px 0 !important; overflow: visible !important; background: transparent !important;
          column-count: auto !important; column-width: auto !important; transform: none !important; transition: none !important; }
        /* Code keeps its lines: long ones scroll sideways instead of wrapping. */
        pre { white-space: pre !important; overflow-x: auto !important; max-width: 100% !important; box-sizing: border-box !important; }
        pre * { white-space: inherit !important; }
        pre.cr-wide { max-width: none !important; margin-left: -${layout.bleed ?? 0}px !important; margin-right: -${layout.bleed ?? 0}px !important; }
        #${NAV_ID} { display: flex !important; justify-content: center; margin: 3em 0 0; padding: 1.5em 0 3em;
          border-top: 1px solid color-mix(in srgb, currentColor 18%, transparent); }
        #${NAV_ID} button { font: inherit; font-size: 0.95em; color: inherit; background: transparent; cursor: pointer;
          padding: 0.6em 1.4em; border: 1px solid color-mix(in srgb, currentColor 35%, transparent); border-radius: 999px; }
        ${shared}
      `;
    }

    return `
      html { margin: 0 !important; padding: 0 !important; width: ${width}px !important; height: ${height}px !important;
        overflow: hidden !important; background: transparent !important; touch-action: none;
        -webkit-text-size-adjust: none; text-size-adjust: none; -webkit-user-select: text; user-select: text; }
      body { box-sizing: border-box !important; margin: 0 !important; width: ${width}px !important; height: ${height}px !important;
        min-width: 0 !important; max-width: none !important; min-height: 0 !important; max-height: none !important;
        padding: ${padding}px ${margin}px !important; overflow: visible !important; background: transparent !important;
        column-count: ${columns} !important; column-gap: ${margin * 2}px !important; column-fill: auto !important;
        column-width: auto !important; column-rule: none !important; }
      /* Code wraps rather than running into the next column (scroll mode keeps its lines). */
      pre, pre * { white-space: pre-wrap !important; }
      pre { break-inside: avoid; }
      #${NAV_ID} { display: none !important; }
      ${shared}
    `;
  }

  /** Scroll mode: code blocks with lines too long for the text column get the page's full width. */
  private widenCode() {
    const doc = this.doc;
    if (!doc) return;
    const blocks = Array.from(doc.body.querySelectorAll("pre"));
    for (const pre of blocks) pre.classList.remove("cr-wide");
    if (!this.scrolled || !this.layout?.bleed) return;
    const wide = blocks.filter((pre) => pre.scrollWidth > pre.clientWidth + 1);
    for (const pre of wide) pre.classList.add("cr-wide");
  }

  /** Pages that are just a picture (covers, maps, plates) are centred and scaled to fit. */
  private markImagePage(doc: Document) {
    const pictures = doc.body.querySelectorAll("img, svg, image");
    const navText = doc.getElementById(NAV_ID)?.textContent ?? "";
    const words = (doc.body.textContent ?? "").replace(navText, "").replace(/\s+/g, "").length;
    doc.body.classList.toggle("cr-image-page", pictures.length > 0 && pictures.length <= 2 && words < 40);
  }

  private async waitForFonts(doc: Document) {
    try {
      await Promise.race([doc.fonts.ready, new Promise((resolve) => window.setTimeout(resolve, 1500))]);
    } catch {
      // Ignore; text renders with fallback fonts.
    }
  }

  private attach(doc: Document) {
    const on = <K extends keyof DocumentEventMap>(type: K, handler: (event: DocumentEventMap[K]) => void, options?: AddEventListenerOptions) => {
      doc.addEventListener(type, handler as EventListener, options);
      this.detachers.push(() => doc.removeEventListener(type, handler as EventListener, options));
    };

    on("click", (event) => this.onClick(event));
    on("touchstart", (event) => this.onTouchStart(event), { passive: true });
    on("touchmove", (event) => this.onTouchMove(event), { passive: false });
    on("touchend", (event) => this.onTouchEnd(event));
    on("touchcancel", () => (this.touch = null));
    on("keydown", (event) => this.events.onKey(event));
    // In scroll mode the wheel scrolls the page natively; in page mode it turns pages.
    on("wheel", (event) => !this.scrolled && this.events.onWheel(event), { passive: false });
    on("scroll", () => this.onScroll(), { passive: true });
    on("dragstart", (event) => event.preventDefault());

    // Images without dimensions change the layout once they arrive.
    doc.querySelectorAll("img").forEach((image) => {
      if (!image.complete) {
        const handler = () => this.scheduleRelayout();
        image.addEventListener("load", handler, { once: true });
        this.detachers.push(() => image.removeEventListener("load", handler));
      }
    });
  }

  private detach() {
    this.detachers.forEach((detach) => detach());
    this.detachers = [];
    this.doc = null;
    this.text = null;
  }

  private scheduleRelayout() {
    window.clearTimeout(this.relayoutTimer);
    this.relayoutTimer = window.setTimeout(() => {
      if (!this.layout) return;
      // Scroll mode: the browser keeps the reader's place as pictures arrive; only re-measure.
      if (this.scrolled) {
        this.widenCode();
        this.measureScroll();
      } else {
        void this.relayout(this.layout);
      }
    }, 150);
  }

  // ---- Geometry ------------------------------------------------------------------------------------

  private applyTransform(animate: boolean, dragOffset = 0) {
    const body = this.doc?.body;
    if (!body || !this.layout) return;
    if (this.scrolled) {
      body.style.removeProperty("transform");
      body.style.removeProperty("transition");
      return;
    }

    const shift = this.page * this.layout.width * (this.dir === "rtl" ? 1 : -1) + dragOffset;
    body.style.setProperty("transition", animate ? `transform ${TURN_MS}ms cubic-bezier(0.22, 0.8, 0.24, 1)` : "none", "important");
    body.style.setProperty("transform", `translate3d(${shift}px, 0, 0)`, "important");
  }

  /** Jump any running animation to its end so measurements reflect the current page. */
  private snap() {
    this.applyTransform(false);
  }

  /** Distance of a rect from the start edge of page 0, in layout pixels. */
  private startOffset(rect: DOMRect): number {
    const layout = this.layout!;
    const shift = this.page * layout.width;
    return this.dir === "rtl" ? layout.width - rect.right + shift : rect.left + shift;
  }

  private pageOfRect(rect: DOMRect): number {
    const page = Math.floor((this.startOffset(rect) + 1) / this.layout!.width);
    return Math.max(0, Math.min(this.pageCount - 1, page));
  }

  /**
   * Counts pages by finding where the last visible content sits. Only real content counts (not
   * whitespace or empty containers), so a full-height image doesn't leave a blank page behind it.
   */
  private measure() {
    const doc = this.doc;
    const layout = this.layout;
    if (!doc || !layout) return;
    if (this.scrolled) {
      this.measureScroll();
      return;
    }

    this.snap();

    const content: Node[] = [];
    const walker = doc.createTreeWalker(doc.body, NodeFilter.SHOW_TEXT | NodeFilter.SHOW_ELEMENT);
    for (let node = walker.nextNode(); node; node = walker.nextNode()) {
      if (node.nodeType === Node.TEXT_NODE ? /\S/.test((node as Text).data) : isReplaced(node as Element)) {
        content.push(node);
      }
    }

    // Content flows in document order, so the last few pieces decide the page count.
    const range = doc.createRange();
    let lastPage = 0;
    for (let i = content.length - 1, checked = 0; i >= 0 && checked < 80; i--, checked++) {
      const node = content[i];
      let rects: DOMRectList;
      if (node.nodeType === Node.TEXT_NODE) {
        range.selectNodeContents(node);
        rects = range.getClientRects();
      } else {
        rects = (node as Element).getClientRects();
      }

      for (const rect of rects) {
        if (rect.width <= 0 && rect.height <= 0) continue;
        lastPage = Math.max(lastPage, Math.floor((this.startOffset(rect) + 1) / layout.width));
      }
    }

    this.pageCount = Math.max(1, lastPage + 1);
    this.page = Math.min(this.page, this.pageCount - 1);
  }

  /** Scroll mode: "pages" are screenfuls, so the footer can still say how much of the chapter is left. */
  private measureScroll() {
    const scroller = this.scroller;
    if (!scroller) return;
    const screen = Math.max(1, scroller.clientHeight);
    this.pageCount = Math.max(1, Math.ceil((scroller.scrollHeight - 2) / screen));
    const atEnd = scroller.scrollTop + screen >= scroller.scrollHeight - 2;
    this.page = atEnd ? this.pageCount - 1 : Math.min(this.pageCount - 1, Math.floor((scroller.scrollTop + 2) / screen));
  }

  private scrollScreen(direction: 1 | -1): boolean {
    const scroller = this.scroller;
    if (!scroller) return false;
    const atTop = scroller.scrollTop <= 1;
    const atEnd = scroller.scrollTop + scroller.clientHeight >= scroller.scrollHeight - 2;
    if ((direction === 1 && atEnd) || (direction === -1 && atTop)) return false;

    // Keep a couple of lines of overlap so the eye can find its place.
    const step = Math.max(40, scroller.clientHeight - 64);
    const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    scroller.scrollBy({ top: direction * step, behavior: reduceMotion ? "instant" : "smooth" });
    return true;
  }

  private scrollToTarget(target: PageTarget) {
    const scroller = this.scroller;
    if (!scroller) return;
    const top = (rect: DOMRect | null, offset: number) => (rect ? rect.top + scroller.scrollTop - offset : 0);
    let position = 0;
    switch (target.kind) {
      case "end":
        position = scroller.scrollHeight;
        break;
      case "fraction":
        position = target.value <= 0 ? 0 : top(this.rectAtFraction(target.value), (this.layout?.padding ?? 0) + 8);
        break;
      case "fragment":
        position = top(this.findFragment(target.id)?.getBoundingClientRect() ?? null, 16);
        break;
      case "search": {
        const range = this.findText(target.query, target.occurrence);
        if (range) {
          this.highlight(range);
          position = top(range.getBoundingClientRect(), scroller.clientHeight / 3);
        }

        break;
      }
    }

    scroller.scrollTop = Math.max(0, position);
    this.measureScroll();
  }

  private rectAtFraction(fraction: number): DOMRect | null {
    const text = this.text;
    if (!text || text.total === 0 || !this.doc) return null;
    const position = this.positionAt(Math.min(text.total - 1, Math.max(0, Math.round(fraction * text.total))));
    if (!position) return null;
    const range = this.doc.createRange();
    range.setStart(position.node, position.offset);
    range.setEnd(position.node, Math.min(position.node.data.length, position.offset + 1));
    const rects = range.getClientRects();
    return rects.length > 0 ? rects[0] : range.getBoundingClientRect();
  }

  private onScroll() {
    if (!this.scrolled || this.scrollFrame) return;
    this.scrollFrame = requestAnimationFrame(() => {
      this.scrollFrame = 0;
      this.measureScroll();
      window.clearTimeout(this.settleTimer);
      this.events.onRelocate({ page: this.page, pageCount: this.pageCount, fraction: this.scrollFraction(), settled: false, scrolled: this.scrolledPast() });
      this.settleTimer = window.setTimeout(() => this.emit(true), 250);
    });
  }

  private scrolledPast(): number {
    const scroller = this.scroller;
    if (!scroller) return 0;
    const range = scroller.scrollHeight - scroller.clientHeight;
    return range <= 1 ? 1 : Math.min(1, Math.max(0, scroller.scrollTop / range));
  }

  /** A rough position from the scroll offset, used while scrolling is still under way. */
  private scrollFraction(): number {
    const scroller = this.scroller;
    if (!scroller || scroller.scrollHeight <= scroller.clientHeight) return 0;
    return scroller.scrollTop / scroller.scrollHeight;
  }

  private pageOfNode(node: Element | Range): number {
    const rects = node.getClientRects();
    const rect = rects.length > 0 ? rects[0] : node.getBoundingClientRect();
    return this.pageOfRect(rect);
  }

  private pageFor(target: PageTarget): number {
    switch (target.kind) {
      case "end":
        return this.pageCount - 1;
      case "fraction":
        return this.pageOfFraction(target.value);
      case "fragment": {
        const page = this.pageOfFragment(target.id);
        return page ?? 0;
      }
      case "search": {
        const range = this.findText(target.query, target.occurrence);
        if (!range) return 0;
        this.highlight(range);
        return this.pageOfNode(range);
      }
    }
  }

  private pageOfFraction(fraction: number): number {
    const text = this.text;
    if (!text || text.total === 0) return Math.round(Math.max(0, Math.min(1, fraction)) * (this.pageCount - 1));
    if (fraction <= 0) return 0;
    const offset = Math.min(text.total - 1, Math.max(0, Math.round(fraction * text.total)));
    const position = this.positionAt(offset);
    if (!position) return 0;
    const range = this.doc!.createRange();
    range.setStart(position.node, position.offset);
    range.setEnd(position.node, Math.min(position.node.data.length, position.offset + 1));
    return this.pageOfNode(range);
  }

  // ---- Text positions --------------------------------------------------------------------------------

  private indexText() {
    const doc = this.doc!;
    // Only the book's own text: the chapter navigation we add isn't part of it (search offsets must match the server's).
    const walker = doc.createTreeWalker(doc.body, NodeFilter.SHOW_TEXT, {
      acceptNode: (node) => (node.parentElement?.closest(`#${NAV_ID}`) ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT),
    });
    const nodes: Text[] = [];
    const starts: number[] = [];
    const lookup = new Map<Node, number>();
    let total = 0;
    for (let node = walker.nextNode() as Text | null; node; node = walker.nextNode() as Text | null) {
      lookup.set(node, nodes.length);
      nodes.push(node);
      starts.push(total);
      total += node.data.length;
    }

    this.text = { nodes, starts, lookup, total };
  }

  private positionAt(offset: number): { node: Text; offset: number } | null {
    const text = this.text;
    if (!text || text.nodes.length === 0) return null;
    let low = 0;
    let high = text.nodes.length - 1;
    while (low < high) {
      const mid = (low + high + 1) >> 1;
      if (text.starts[mid] <= offset) low = mid;
      else high = mid - 1;
    }

    return { node: text.nodes[low], offset: Math.min(offset - text.starts[low], text.nodes[low].data.length) };
  }

  /** The text position of the first character visible on the current page. */
  private readFraction(): number {
    const doc = this.doc;
    const layout = this.layout;
    const text = this.text;
    if (!doc || !layout || !text || text.total === 0) return this.pageCount > 1 ? this.page / this.pageCount : 0;
    if (this.scrolled) return this.readScrollFraction(doc, layout, text);

    this.snap();
    const columnWidth = layout.width / layout.columns - layout.margin * 2;
    const left = this.dir === "rtl" ? layout.width - layout.margin - 3 : layout.margin + 3;
    const across = this.dir === "rtl" ? -columnWidth / 2 : columnWidth / 2;
    const probes: [number, number][] = [
      [left, layout.padding + 3],
      [left, layout.padding + 14],
      [left + across, layout.padding + 3],
      [left, layout.height / 3],
      [left + across, layout.height / 2],
      [left, (layout.height * 2) / 3],
    ];

    for (const [x, y] of probes) {
      const caret = caretAt(doc, x, y);
      if (!caret || caret.node.nodeType !== Node.TEXT_NODE) continue;
      const index = text.lookup.get(caret.node);
      if (index === undefined) continue;

      // Make sure the hit really is on this page (hit-testing in a margin can snap to a neighbour).
      const range = doc.createRange();
      range.setStart(caret.node, caret.offset);
      range.setEnd(caret.node, Math.min((caret.node as Text).data.length, caret.offset + 1));
      if (this.pageOfNode(range) !== this.page) continue;
      return (text.starts[index] + caret.offset) / text.total;
    }

    return this.page / this.pageCount;
  }

  /** Scroll mode: the first character visible at the top of the viewport. */
  private readScrollFraction(doc: Document, layout: PageLayout, text: TextIndex): number {
    const scroller = this.scroller;
    if (!scroller || scroller.scrollTop <= 1) return 0;
    const width = scroller.clientWidth;
    const left = this.dir === "rtl" ? width - layout.margin - 3 : layout.margin + 3;
    const middle = width / 2;
    for (const y of [4, 16, 32, 56, 96, scroller.clientHeight / 3, scroller.clientHeight / 2]) {
      for (const x of [left, middle]) {
        const caret = caretAt(doc, x, y);
        if (!caret || caret.node.nodeType !== Node.TEXT_NODE) continue;
        const index = text.lookup.get(caret.node);
        if (index === undefined) continue;

        // Hit-testing above the first line can snap to the line scrolled out of view; skip those.
        const range = doc.createRange();
        range.setStart(caret.node, caret.offset);
        range.setEnd(caret.node, Math.min((caret.node as Text).data.length, caret.offset + 1));
        const rect = range.getClientRects()[0] ?? range.getBoundingClientRect();
        if (rect.top < -2 || rect.top > scroller.clientHeight) continue;
        return (text.starts[index] + caret.offset) / text.total;
      }
    }

    return this.scrollFraction();
  }

  private findText(query: string, occurrence: number): Range | null {
    const text = this.text;
    if (!text || !this.doc) return null;
    const full = text.nodes.map((node) => node.data).join("");
    const match = findOccurrence(full, query, occurrence);
    if (!match) return null;
    const start = this.positionAt(match[0]);
    const end = this.positionAt(match[1] - 1);
    if (!start || !end) return null;
    const range = this.doc.createRange();
    range.setStart(start.node, start.offset);
    range.setEnd(end.node, Math.min(end.node.data.length, end.offset + 1));
    return range;
  }

  private highlight(range: Range) {
    const win = this.iframe.contentWindow as (Window & typeof globalThis & { Highlight?: new (...ranges: Range[]) => unknown }) | null;
    const registry = (win?.CSS as unknown as { highlights?: Map<string, unknown> } | undefined)?.highlights;
    if (win?.Highlight && registry) {
      registry.set("cr-search", new win.Highlight(range));
      return;
    }

    // Older browsers: select the match instead.
    const selection = win?.getSelection();
    selection?.removeAllRanges();
    selection?.addRange(range);
  }

  private findFragment(id: string): Element | null {
    const doc = this.doc;
    if (!doc) return null;
    return doc.getElementById(id) ?? doc.querySelector(`[name="${CSS.escape(id)}"]`);
  }

  // ---- Input ---------------------------------------------------------------------------------------

  private onClick(event: MouseEvent) {
    const anchor = (event.target as Element | null)?.closest?.("a[href]") as HTMLAnchorElement | null;
    if (anchor) event.preventDefault();

    // Touch taps are handled on touchend (browsers don't always synthesise a click after a swipe).
    if (performance.now() < this.suppressClickUntil) return;
    this.handleTap(event.target as Element | null, event.clientX);
  }

  private handleTap(target: Element | null, clientX: number) {
    if (target?.closest?.("[data-cr-next]")) {
      this.events.onChapterEnd();
      return;
    }

    const anchor = target?.closest?.("a[href]") as HTMLAnchorElement | null;
    if (anchor) {
      this.followLink(anchor);
      return;
    }

    const selection = this.iframe.contentWindow?.getSelection();
    if (selection && !selection.isCollapsed) return;

    const width = this.iframe.clientWidth || this.layout?.width || 1;
    const x = clientX / width;
    const zone = x < 0.3 ? "prev" : x > 0.7 ? "next" : "middle";
    const mirrored = this.dir === "rtl" && zone !== "middle" ? (zone === "prev" ? "next" : "prev") : zone;
    this.events.onTap(mirrored);
  }

  private followLink(anchor: HTMLAnchorElement) {
    const href = anchor.getAttribute("href");
    if (!href || !this.doc) return;
    let url: URL;
    try {
      url = new URL(href, this.doc.baseURI);
    } catch {
      return;
    }

    const prefix = `/api/books/${this.bookId}/files/`;
    if (url.origin === window.location.origin && url.pathname.startsWith(prefix)) {
      const path = decodeURIComponent(url.pathname.slice(prefix.length));
      const fragment = url.hash.length > 1 ? decodeURIComponent(url.hash.slice(1)) : null;
      this.events.onLink({ path, fragment });
    } else if (url.protocol === "http:" || url.protocol === "https:" || url.protocol === "mailto:") {
      window.open(url.href, "_blank", "noopener,noreferrer");
    }
  }

  private onTouchStart(event: TouchEvent) {
    if (event.touches.length !== 1) {
      this.touch = null;
      return;
    }

    const touch = event.touches[0];
    this.touch = { x: touch.clientX, y: touch.clientY, time: performance.now(), dragging: false, dx: 0, moved: 0 };
  }

  private onTouchMove(event: TouchEvent) {
    const state = this.touch;
    if (!state || event.touches.length !== 1) return;
    const touch = event.touches[0];
    const dx = touch.clientX - state.x;
    const dy = touch.clientY - state.y;
    state.moved = Math.max(state.moved, Math.hypot(dx, dy));

    // Scroll mode: the browser scrolls natively; only watch for taps.
    if (this.scrolled) {
      if (state.moved > 10) this.touch = null;
      return;
    }

    if (!state.dragging) {
      const selection = this.iframe.contentWindow?.getSelection();
      if (selection && !selection.isCollapsed) {
        this.touch = null;
        return;
      }

      if (Math.abs(dx) > 10 && Math.abs(dx) > Math.abs(dy) * 1.2) {
        state.dragging = true;
      } else if (Math.abs(dy) > 14) {
        this.touch = null;
        return;
      }
    }

    if (state.dragging) {
      event.preventDefault();
      const forward = this.dir === "rtl" ? dx > 0 : dx < 0;
      const atEdge = forward ? this.page >= this.pageCount - 1 && !this.events.canLeave(1) : this.page <= 0 && !this.events.canLeave(-1);
      state.dx = atEdge ? dx * 0.25 : dx;
      this.applyTransform(false, state.dx);
    }
  }

  private onTouchEnd(event: TouchEvent) {
    const state = this.touch;
    this.touch = null;
    if (!state || !this.layout) return;

    if (!state.dragging) {
      // A short, still touch is a tap.
      if (state.moved < 10 && performance.now() - state.time < 350 && event.changedTouches.length === 1) {
        this.suppressClickUntil = performance.now() + 600;
        this.handleTap(event.target as Element | null, event.changedTouches[0].clientX);
      }

      return;
    }

    this.suppressClickUntil = performance.now() + 400;
    const elapsed = Math.max(1, performance.now() - state.time);
    const velocity = Math.abs(state.dx) / elapsed;
    const distance = Math.abs(state.dx);
    if (distance > Math.min(90, this.layout.width * 0.2) || (velocity > 0.35 && distance > 24)) {
      const forward = this.dir === "rtl" ? state.dx > 0 : state.dx < 0;
      this.events.onSwipe(forward ? 1 : -1);
    } else {
      this.settle();
    }
  }

  // ---- Reporting -----------------------------------------------------------------------------------

  private emit(settled: boolean) {
    window.clearTimeout(this.settleTimer);
    if (this.scrolled) this.measureScroll();
    const report = (isSettled: boolean) =>
      this.events.onRelocate({
        page: this.page,
        pageCount: this.pageCount,
        fraction: isSettled ? this.readFraction() : this.page / this.pageCount,
        settled: isSettled,
        scrolled: this.scrolled ? this.scrolledPast() : undefined,
      });

    if (settled) {
      report(true);
    } else {
      report(false);
      this.settleTimer = window.setTimeout(() => report(true), TURN_MS + 20);
    }
  }
}

const urlAttributes = ["src", "href", "xlink:href", "poster"];

/**
 * Resolves every resource and link URL in the chapter against the book's location. Browsers'
 * speculative loaders don't always honour <base> in a srcdoc document, so relative URLs would
 * otherwise be fetched from the wrong place first. Parsing with DOMParser loads nothing.
 */
function absolutizeUrls(html: string, base: URL): string {
  const parsed = new DOMParser().parseFromString(`<!DOCTYPE html><body>${html}</body>`, "text/html");
  const resolve = (value: string) => {
    if (value.startsWith("#")) return value;
    try {
      return new URL(value, base).href;
    } catch {
      return value;
    }
  };

  for (const element of parsed.body.querySelectorAll("*")) {
    for (const name of urlAttributes) {
      const value = element.getAttribute(name);
      if (value) element.setAttribute(name, resolve(value.trim()));
    }

    const srcset = element.getAttribute("srcset");
    if (srcset) {
      element.setAttribute(
        "srcset",
        srcset
          .split(",")
          .map((candidate) => {
            const [url, ...descriptor] = candidate.trim().split(/\s+/);
            return [resolve(url), ...descriptor].join(" ");
          })
          .join(", "),
      );
    }
  }

  return parsed.body.innerHTML;
}

const replacedElements = new Set(["IMG", "SVG", "VIDEO", "AUDIO", "CANVAS", "OBJECT", "HR", "IMAGE", "MATH"]);

function isReplaced(element: Element): boolean {
  return replacedElements.has(element.nodeName.toUpperCase());
}

function caretAt(doc: Document, x: number, y: number): { node: Node; offset: number } | null {
  const withPosition = doc as Document & { caretPositionFromPoint?: (x: number, y: number) => { offsetNode: Node; offset: number } | null };
  if (withPosition.caretPositionFromPoint) {
    const position = withPosition.caretPositionFromPoint(x, y);
    return position ? { node: position.offsetNode, offset: position.offset } : null;
  }

  const withRange = doc as Document & { caretRangeFromPoint?: (x: number, y: number) => Range | null };
  const range = withRange.caretRangeFromPoint?.(x, y);
  return range ? { node: range.startContainer, offset: range.startOffset } : null;
}
