// Mirrors the API's DTOs (see src/CoffeeReader.Application/**/*Dtos.cs).

export type UserRole = "user" | "admin";

export interface User {
  id: number;
  username: string;
  displayName: string;
  role: UserRole;
  createdAt: string;
  lastLoginAt: string | null;
}

export interface AuthStatus {
  setupRequired: boolean;
  user: User | null;
}

export interface AuthorRef {
  id: number;
  name: string;
  sortName: string;
}

export interface SeriesRef {
  id: number;
  name: string;
}

export interface ProgressSummary {
  percentage: number;
  isFinished: boolean;
  updatedAt: string;
}

export type BookFormat = "epub" | "pdf" | "mobi";

export interface BookSummary {
  id: number;
  title: string;
  sortTitle: string;
  authors: AuthorRef[];
  series: SeriesRef | null;
  seriesIndex: number | null;
  hasCover: boolean;
  coverVersion: number;
  addedAt: string;
  progress: ProgressSummary | null;
  format: BookFormat;
}

export interface BookDetails extends BookSummary {
  description: string | null;
  publisher: string | null;
  language: string | null;
  publishedDate: string | null;
  isbn: string | null;
  fileName: string;
  fileSize: number;
  chapterCount: number;
  metadataLocked: boolean;
  shelfIds: number[];
}

export interface UpdateBookRequest {
  title: string;
  authors: string[];
  series: string | null;
  seriesIndex: number | null;
  description: string | null;
  publisher: string | null;
  language: string | null;
  publishedDate: string | null;
  isbn: string | null;
}

export interface Shelf {
  id: number;
  name: string;
  sortOrder: number;
  bookIds: number[];
}

export interface ChapterInfo {
  index: number;
  href: string;
  title: string | null;
  length: number;
  isLinear: boolean;
}

export interface TocEntry {
  title: string;
  depth: number;
  chapterIndex: number;
  fragment: string | null;
}

export interface Progress {
  chapterIndex: number;
  chapterProgress: number;
  percentage: number;
  isFinished: boolean;
  updatedAt: string;
  finishedAt: string | null;
}

export interface ReaderManifest {
  bookId: number;
  format: BookFormat;
  /** Changes whenever the file changes; used to cache the raw file safely. */
  fileVersion: string;
  title: string;
  authors: string[];
  language: string | null;
  chapters: ChapterInfo[];
  tableOfContents: TocEntry[];
  totalLength: number;
  progress: Progress | null;
}

export interface Chapter {
  index: number;
  href: string;
  html: string;
  stylesheets: string[];
  inlineStyles: string[];
  bodyId: string | null;
  bodyClass: string | null;
  bodyStyle: string | null;
  language: string | null;
  direction: "ltr" | "rtl" | null;
}

export interface SearchHit {
  chapterIndex: number;
  chapterTitle: string | null;
  occurrence: number;
  before: string;
  match: string;
  after: string;
  percentage: number;
}

export interface SearchResponse {
  query: string;
  totalMatches: number;
  truncated: boolean;
  hits: SearchHit[];
}

export type UploadStatus = "added" | "duplicate" | "failed";

export interface UploadResult {
  fileName: string;
  status: UploadStatus;
  bookId: number | null;
  title: string | null;
  error: string | null;
}

export interface ScanSummary {
  added: number;
  updated: number;
  restored: number;
  missing: number;
  failed: number;
  durationSeconds: number;
}

export interface LibraryStatus {
  isScanning: boolean;
  lastScanStartedAt: string | null;
  lastScanCompletedAt: string | null;
  lastScan: ScanSummary | null;
  lastError: string | null;
  issues: { path: string; message: string }[];
  bookCount: number;
  missingCount: number;
}

export interface LibraryFormats {
  extensions: string[];
  maxUploadBytes: number;
}
