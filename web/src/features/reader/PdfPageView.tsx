import { RenderingCancelledException, TextLayer } from "pdfjs-dist";
import { useEffect, useRef, useState } from "react";
import type { PDFDocumentProxy } from "./pdf";
import { fold, foldQuery } from "./textSearch";
import styles from "./ReaderPage.module.css";

export type PdfFit = "page" | "width";

interface Props {
  doc: PDFDocumentProxy;
  index: number;
  box: { width: number; height: number };
  fit: PdfFit;
  hidden: boolean;
  highlight: string | null;
  filter: string | undefined;
  align: "start" | "end" | "center";
  onHighlighted?: (element: HTMLElement) => void;
}

const MAX_CANVAS_PIXELS = 16_777_216;

/** One PDF page: a canvas sized for the screen's pixel density plus a selectable text layer. */
export function PdfPageView({ doc, index, box, fit, hidden, highlight, filter, align, onHighlighted }: Props) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const textRef = useRef<HTMLDivElement>(null);
  const [size, setSize] = useState<{ width: number; height: number } | null>(null);
  const [rendered, setRendered] = useState(false);
  const highlightRef = useRef({ highlight, onHighlighted });
  highlightRef.current = { highlight, onHighlighted };

  // Work out the scale from the page's own proportions.
  const [scale, setScale] = useState<number | null>(null);
  useEffect(() => {
    let cancelled = false;
    void doc.getPage(index + 1).then((page) => {
      if (cancelled) return;
      const natural = page.getViewport({ scale: 1 });
      const next = fit === "width" ? box.width / natural.width : Math.min(box.width / natural.width, box.height / natural.height);
      setScale(Math.max(0.1, Math.round(next * 1000) / 1000));
    });
    return () => {
      cancelled = true;
    };
  }, [doc, index, box.width, box.height, fit]);

  useEffect(() => {
    if (scale === null) return;
    let cancelled = false;
    let renderTask: { cancel: () => void; promise: Promise<unknown> } | null = null;
    let textLayer: TextLayer | null = null;

    (async () => {
      const page = await doc.getPage(index + 1);
      if (cancelled) return;
      const viewport = page.getViewport({ scale });
      const canvas = canvasRef.current!;
      const density = Math.min(window.devicePixelRatio || 1, 3);
      const outputScale = Math.min(density, Math.sqrt(MAX_CANVAS_PIXELS / (viewport.width * viewport.height)));
      canvas.width = Math.floor(viewport.width * outputScale);
      canvas.height = Math.floor(viewport.height * outputScale);
      setSize({ width: Math.floor(viewport.width), height: Math.floor(viewport.height) });

      renderTask = page.render({
        canvas,
        viewport,
        transform: outputScale === 1 ? undefined : [outputScale, 0, 0, outputScale, 0, 0],
        background: "#ffffff",
      });
      await renderTask.promise;
      if (cancelled) return;
      setRendered(true);

      const container = textRef.current!;
      container.replaceChildren();
      container.style.setProperty("--total-scale-factor", String(scale));
      textLayer = new TextLayer({ textContentSource: page.streamTextContent(), container, viewport });
      await textLayer.render();
      if (!cancelled) applyHighlight(container, highlightRef.current.highlight, highlightRef.current.onHighlighted);
    })().catch((error) => {
      if (!(error instanceof RenderingCancelledException) && !cancelled) console.warn("PDF page failed to render", error);
    });

    return () => {
      cancelled = true;
      renderTask?.cancel();
      textLayer?.cancel();
    };
  }, [doc, index, scale]);

  useEffect(() => {
    if (textRef.current) applyHighlight(textRef.current, highlight, onHighlighted);
  }, [highlight, onHighlighted]);

  return (
    <div
      className={styles.pdfPage}
      data-page={index + 1}
      style={{
        width: size?.width,
        height: size?.height,
        display: hidden ? "none" : undefined,
        justifySelf: align,
        opacity: rendered ? 1 : 0,
      }}
    >
      <canvas ref={canvasRef} style={{ width: size?.width, height: size?.height, filter }} />
      <div ref={textRef} className="textLayer" />
    </div>
  );
}

/** Marks every match of the query on the page (within a line of text), case- and accent-insensitively. */
function applyHighlight(container: HTMLElement, query: string | null, onHighlighted?: (element: HTMLElement) => void) {
  container.querySelectorAll("mark.pdfHit").forEach((mark) => mark.replaceWith(...mark.childNodes));
  container.normalize();
  const needle = query ? foldQuery(query) : "";
  if (!needle) return;

  let first: HTMLElement | null = null;
  const walker = document.createTreeWalker(container, NodeFilter.SHOW_TEXT);
  const nodes: Text[] = [];
  for (let node = walker.nextNode(); node; node = walker.nextNode()) nodes.push(node as Text);

  for (const node of nodes) {
    const { folded, map } = fold(node.data);
    const ranges: [number, number][] = [];
    for (let at = folded.indexOf(needle); at >= 0; at = folded.indexOf(needle, at + needle.length)) {
      ranges.push([map[at], map[at + needle.length - 1] + 1]);
    }

    // Wrap from the end so earlier offsets stay valid.
    for (const [start, end] of ranges.reverse()) {
      const range = document.createRange();
      range.setStart(node, start);
      range.setEnd(node, Math.min(end, node.data.length));
      const mark = document.createElement("mark");
      mark.className = "pdfHit";
      range.surroundContents(mark);
      first = mark;
    }
  }

  if (first) onHighlighted?.(first);
}
