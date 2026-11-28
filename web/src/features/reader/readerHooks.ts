import { useQueryClient } from "@tanstack/react-query";
import { useCallback, useEffect, useLayoutEffect, useRef, useState, type RefObject } from "react";
import { request } from "../../api/client";
import { queryKeys } from "../../api/queries";

export type Panel = "toc" | "search" | "settings" | null;

export function useElementSize(ref: RefObject<HTMLElement | null>) {
  const [size, setSize] = useState<{ width: number; height: number } | null>(null);
  useLayoutEffect(() => {
    const element = ref.current;
    if (!element) return;
    const observer = new ResizeObserver(([entry]) => setSize({ width: entry.contentRect.width, height: entry.contentRect.height }));
    observer.observe(element);
    return () => observer.disconnect();
  }, [ref]);
  return size;
}

/**
 * Saves reading position a moment after it settles, and immediately when the page is hidden or left,
 * so progress is never lost when a phone locks mid-chapter.
 */
export function useProgressSaver(bookId: number) {
  const client = useQueryClient();
  const pending = useRef<{ chapterIndex: number; chapterProgress: number } | null>(null);
  const timer = useRef(0);

  const flush = useCallback(() => {
    window.clearTimeout(timer.current);
    const body = pending.current;
    if (!body) return;
    pending.current = null;
    request("PUT", `/api/books/${bookId}/progress`, body, { keepalive: true }).catch(() => {
      // Offline or signed out; the next page turn will try again.
    });
  }, [bookId]);

  const queue = useCallback(
    (chapterIndex: number, chapterProgress: number) => {
      pending.current = { chapterIndex, chapterProgress };
      window.clearTimeout(timer.current);
      timer.current = window.setTimeout(flush, 1200);
    },
    [flush],
  );

  useEffect(() => {
    const onHide = () => document.visibilityState === "hidden" && flush();
    document.addEventListener("visibilitychange", onHide);
    window.addEventListener("pagehide", flush);
    return () => {
      document.removeEventListener("visibilitychange", onHide);
      window.removeEventListener("pagehide", flush);
      flush();
      client.invalidateQueries({ queryKey: queryKeys.books });
    };
  }, [flush, client]);

  return { queue, flush };
}

/** Scrolling turns pages: one flick or one wheel notch is one page, ignoring trackpad momentum. */
export function useWheelTurns(turn: (direction: 1 | -1) => void) {
  const state = useRef({ total: 0, lastTime: 0, lastSize: 0, locked: false, turnedAt: 0 });
  return useCallback(
    (event: WheelEvent) => {
      event.preventDefault();
      const s = state.current;
      const now = performance.now();
      const raw = Math.abs(event.deltaY) >= Math.abs(event.deltaX) ? event.deltaY : event.deltaX;
      const delta = raw * (event.deltaMode === 1 ? 32 : event.deltaMode === 2 ? 600 : 1);
      const size = Math.abs(delta);
      const gap = now - s.lastTime;
      s.lastTime = now;

      if (s.locked) {
        const newGesture = gap > 180 || size > s.lastSize * 1.5 + 4 || (size >= 50 && now - s.turnedAt > 380);
        s.lastSize = size;
        if (!newGesture) return;
        s.locked = false;
        s.total = 0;
      }

      s.lastSize = size;
      if (gap > 300) s.total = 0;
      s.total += delta;
      if (Math.abs(s.total) >= 40) {
        turn(s.total > 0 ? 1 : -1);
        s.total = 0;
        s.locked = true;
        s.turnedAt = now;
      }
    },
    [turn],
  );
}

/** Arrow keys, space, page up/down; T for contents and / for search. Returns a handler for iframes. */
export function useReaderKeys(turn: (direction: 1 | -1) => void, setPanel: (update: (panel: Panel) => Panel) => void, isRtl: () => boolean) {
  const onKey = useCallback(
    (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null;
      if (target?.closest?.("input, textarea, select") || event.metaKey || event.ctrlKey || event.altKey) return;
      const rtl = isRtl();
      const forward = () => (event.preventDefault(), turn(1));
      const back = () => (event.preventDefault(), turn(-1));
      switch (event.key) {
        case "ArrowRight":
          return rtl ? back() : forward();
        case "ArrowLeft":
          return rtl ? forward() : back();
        case "ArrowDown":
        case "PageDown":
          return forward();
        case "ArrowUp":
        case "PageUp":
          return back();
        case " ":
          return event.shiftKey ? back() : forward();
        case "Escape":
          setPanel(() => null);
          return;
        case "t":
          setPanel((p) => (p === "toc" ? null : "toc"));
          return;
        case "/":
          event.preventDefault();
          setPanel(() => "search");
          return;
      }
    },
    [turn, setPanel, isRtl],
  );

  useEffect(() => {
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onKey]);

  return onKey;
}

/** Match the browser chrome to the page colour and keep the page from scrolling behind the reader. */
export function useReaderDocument(title: string, pageColor: string) {
  useEffect(() => {
    const meta = document.querySelector<HTMLMetaElement>('meta[name="theme-color"]:not([media])') ?? document.head.appendChild(Object.assign(document.createElement("meta"), { name: "theme-color" }));
    const previousOverflow = document.body.style.overflow;
    meta.content = pageColor;
    document.body.style.overflow = "hidden";
    return () => {
      meta.remove();
      document.body.style.overflow = previousOverflow;
    };
  }, [pageColor]);

  useEffect(() => {
    document.title = `${title} · CoffeeReader`;
    return () => {
      document.title = "CoffeeReader";
    };
  }, [title]);
}
