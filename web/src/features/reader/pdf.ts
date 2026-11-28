import { getDocument, GlobalWorkerOptions, PasswordException, type PDFDocumentProxy } from "pdfjs-dist";
import workerUrl from "pdfjs-dist/build/pdf.worker.min.mjs?url";
import "./pdfTextLayer.css";

GlobalWorkerOptions.workerSrc = workerUrl;

/** Opens a book's PDF with PDF.js, fetching only the byte ranges it needs. */
export function openPdf(bookId: number, version: string) {
  const task = getDocument({
    url: `/api/books/${bookId}/file?v=${encodeURIComponent(version)}`,
    // Static assets copied from pdfjs-dist by scripts/copy-pdfjs-assets.mjs
    cMapUrl: "/pdfjs/cmaps/",
    cMapPacked: true,
    standardFontDataUrl: "/pdfjs/standard_fonts/",
    wasmUrl: "/pdfjs/wasm/",
    iccUrl: "/pdfjs/iccs/",
    rangeChunkSize: 256 * 1024,
  });
  return {
    promise: task.promise as Promise<PDFDocumentProxy>,
    destroy: () => void task.destroy(),
  };
}

export function describePdfError(error: unknown): string {
  if (error instanceof PasswordException) return "This PDF is password-protected, so it can't be opened here.";
  return error instanceof Error ? `The PDF couldn't be opened: ${error.message}` : "The PDF couldn't be opened.";
}

export type { PDFDocumentProxy };
