import { useQuery } from "@tanstack/react-query";
import { lazy, Suspense } from "react";
import { Link, useParams } from "react-router";
import { api } from "../../api/client";
import { queryKeys } from "../../api/queries";
import type { ReaderManifest } from "../../api/types";
import { EpubReader } from "./EpubReader";

// PDF.js is large; only load it for PDFs.
const PdfReader = lazy(() => import("./PdfReader"));

const spinner = (
  <div className="center-fill" style={{ minHeight: "100dvh" }}>
    <div className="spinner" />
  </div>
);

/** Opens the right reader for the book's format. */
export default function ReaderPage() {
  const bookId = Number(useParams().bookId);
  const manifestQuery = useQuery({
    queryKey: queryKeys.manifest(bookId),
    queryFn: () => api.get<ReaderManifest>(`/api/books/${bookId}/manifest`),
    staleTime: Infinity,
    refetchOnWindowFocus: false,
  });

  if (manifestQuery.isError) {
    return (
      <div className="center-fill" style={{ minHeight: "100dvh", padding: 24, textAlign: "center" }}>
        <div style={{ display: "grid", gap: 12, justifyItems: "center" }}>
          <p>This book couldn't be opened. {manifestQuery.error.message}</p>
          <Link to="/" className="btn">
            Back to library
          </Link>
        </div>
      </div>
    );
  }

  const manifest = manifestQuery.data;
  if (!manifest) return spinner;

  return manifest.format === "pdf" ? (
    <Suspense fallback={spinner}>
      <PdfReader key={bookId} manifest={manifest} />
    </Suspense>
  ) : (
    <EpubReader key={bookId} manifest={manifest} />
  );
}
