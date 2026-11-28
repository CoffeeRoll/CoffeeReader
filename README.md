# CoffeeReader

A self-hosted ebook library and reader that puts **readability** first. Books look and turn like
pages in a book, not like web pages. Organise them on shelves by dragging and dropping, read on your
phone with taps and swipes, and keep everything in one container with two volumes.

Reads **EPUB**, **MOBI** and **PDF**.

![Home page with continue reading and shelves](docs/screenshots/home.webp)

| Reading on a wide screen | Search inside a book |
| --- | --- |
| ![Two-page spread with pinned table of contents](docs/screenshots/reader-spread.webp) | ![Search results highlighted in the page](docs/screenshots/reader-search.webp) |
| **PDFs as book spreads** | **Every cover shows its format** |
| ![PDF two-page spread with its outline as contents](docs/screenshots/reader-pdf.webp) | ![Library grouped by series with format chips](docs/screenshots/library.webp) |

| Phone reader | Phone library |
| --- | --- |
| <img src="docs/screenshots/mobile-reader.webp" width="300" alt="Mobile reader in sepia"> | <img src="docs/screenshots/mobile-home.webp" width="300" alt="Mobile home page"> |

## What it does differently

| Pain point | CoffeeReader |
| --- | --- |
| Pages render as plain HTML with no page structure | Chapters are laid out as real **pages**: margins, running heads, page numbers, "pages left in chapter", a two-page spread on wide screens, and themes (Paper, Sepia, Dusk, Night) with comfortable line lengths. |
| Turning pages needs small buttons or side areas | **Click or tap** the right side for the next page, the left for the previous one. **Swipe** on touch screens (the page follows your finger), **scroll** with a mouse wheel or trackpad (one flick, one page), or use the **keyboard**. Prefer scrolling? **Scroll mode** shows each chapter as one long page, and code keeps its lines instead of wrapping across pages. |
| Table of contents is hidden in a menu | The contents can be **pinned open beside the page** on larger screens and always highlights where you are. On phones it's one tap away. |
| No search | **Full-text search inside a book**: results grouped by chapter with snippets; tap one to jump to the page with the words highlighted, then step through matches. Plus library search by title, author or series. |
| Home page shows books in a single ungrouped row | The home page shows **Continue reading**, then **every shelf as a section of covers**. Drag books between shelves, reorder them, or drag shelves into order. While you drag, your shelves stay **docked at the bottom of the screen**, so books at the bottom of a long page can be shelved without scrolling. The Library page sorts, filters and groups by **series** or **author**. |

Also:

- **Upload from the browser.** Click **Add books** or drop files anywhere on the page. Uploads go to
  the books folder, so the folder stays the source of truth.
- **Or just drop files in the folder.** New, moved and renamed files are noticed automatically.
  Moving or renaming a file keeps its reading progress and shelf places, because books are
  recognised by content.
- **Organisation lives in the database**: shelves, edited metadata and reading progress never touch
  your files.
- **Multiple people.** Everyone shares the library and has their own shelves and progress.
  Administrators manage people, edit book details and delete books.
- **Progress syncs** between devices. Opening a book anywhere resumes at the same sentence, even if
  the other device uses a different text size.
- **Installable** on phones as a home-screen app (PWA) for full-screen reading.

### Formats

| Format | How it reads |
| --- | --- |
| **EPUB** (2 and 3) | Pages or scroll mode, with all typography settings. Uses the book's own fonts unless you pick one (books that don't set one get Literata). |
| **MOBI** (`.mobi`, `.azw`, `.prc`; Mobipocket / Kindle MOBI 6) | Converted on the fly into the same reader as EPUB: chapters, contents, internal links, images, search and typography all work. Combined KindleGen files are read through their MOBI 6 part. |
| **PDF** | Shown as real pages (PDF.js), one at a time or as book-style spreads, with the PDF's outline as contents. Choose *Whole page* or *Page width* (with zoom up to 300% for small text on phones), and optionally tint pages to match Sepia or invert them for the dark themes. Text can be selected and searched. |

Every cover carries a small chip (**EPUB**, **MOBI**, **PDF**) so you can tell at a glance, the
book page lists the format, and the Library can filter by format.

## Quick start (Docker)

```bash
git clone https://github.com/CoffeeRoll/CoffeeReader.git && cd CoffeeReader
```

```bash
mkdir -p books config
```

```bash
docker compose up -d --build
```

Open <http://localhost:8080> and create the administrator account. Then add books by uploading them,
or by copying ebooks (`.epub`, `.mobi`, `.pdf`) into `./books` (any folder structure works).

The compose file builds the image from source. Once CI has published an image (it builds one for
amd64 and arm64 on every push to `main`), you can skip the clone: copy `docker-compose.yml` anywhere,
delete its `build: .` line and set `image: ghcr.io/coffeeroll/coffeereader:latest`.

### Volumes

| Path in container | What it holds |
| --- | --- |
| `/books` | Your ebooks. Uploads are saved to `/books/Uploads`. Can be mounted read-only (`:ro`); reading still works, but uploads and deleting are disabled. |
| `/config` | The SQLite database (shelves, progress, users, metadata edits), cover thumbnails and the keys that keep you signed in across restarts. **Back this up.** |

### Permissions

The container runs as a non-root user. Set `PUID`/`PGID` to the owner of your `books` and `config`
folders (find them with `id -u` and `id -g`):

```bash
PUID=1000 PGID=1000 docker compose up -d
```

If `/config` isn't writable, the container stops with a message saying so.

### Configuration

Settings are environment variables (double underscores separate sections).

| Variable | Default | Description |
| --- | --- | --- |
| `LibraryScan__IntervalMinutes` | `60` | Full rescan interval, a fallback for file systems that don't report changes (some network shares). `0` turns it off. |
| `LibraryScan__WatchForChanges` | `true` | React to files being added, moved or removed. |
| `LibraryScan__ScanOnStartup` | `true` | Scan the library when the app starts. |
| `Uploads__MaxFileSizeMb` | `512` | Largest file accepted by uploads. |
| `Storage__UploadFolder` | `Uploads` | Folder inside `/books` that uploads are written to. |
| `ReverseProxy__TrustForwardedHeaders` | `false` | Set to `true` behind a reverse proxy so client IPs and HTTPS are detected correctly. |
| `ASPNETCORE_HTTP_PORTS` | `8080` | Port the app listens on inside the container. |

### Reverse proxy

CoffeeReader is a single origin (the app and API share it), so any reverse proxy works with a plain
pass-through. Set `ReverseProxy__TrustForwardedHeaders=true` and raise the proxy's request size
limit to allow uploads (for example `client_max_body_size 512m;` in nginx). Caddy example:

```
books.example.com {
    reverse_proxy coffeereader:8080
}
```

The health check endpoint is `GET /health`.

## Reading controls

| Action | Mouse & keyboard | Touch |
| --- | --- | --- |
| Next page | Click anywhere but the left edge, scroll down, `→` `↓` `Space` `PageDown`, or the `›` arrow | Tap the right side, or swipe left |
| Previous page | Click the left 30% of the page, scroll up, `←` `↑` `Shift+Space` `PageUp`, or the `‹` arrow | Tap the left side, or swipe right |
| Show or hide toolbars | Always shown | Tap the middle of the page |
| Contents | `T`, or the list button (pin it to keep it open) | List button |
| Search | `/`, or the magnifier | Magnifier |
| Text, theme, margins, layout, pages or scroll (PDF: fit, zoom, tint) | The **Aa** button | The **Aa** button |

In a PDF zoomed past the page width, scrolling and dragging pan the page; taps, keys and the arrow
buttons still turn it.

**Scroll mode** (EPUB and MOBI: **Aa** → *Reading mode* → *Scroll*) shows each chapter as one long page
that scrolls with the wheel, trackpad, scrollbar or your finger. `↑` `↓` scroll a little, `Space` and
`PageDown` a screen at a time, and the button at the end of a chapter (or `Space` there) opens the
next one. Code keeps its line breaks: blocks too wide for the text get the page's full width, and
anything wider still scrolls sideways. On touch screens a tap shows or hides the toolbars.

Following a link in a book (for example a footnote) shows a **Back to where you were** button.
Reading settings are saved per device, since a phone and a monitor usually want different sizes.

## Architecture

```
src/
  CoffeeReader.Domain          Entities and rules (books, shelves, progress, users). No dependencies.
  CoffeeReader.Application     Use cases and ports: auth, users, catalog, reader, search, shelves,
                               library import/scan. Depends only on Domain (+ EF Core abstractions).
  CoffeeReader.Infrastructure  Adapters: EF Core + SQLite, ebook formats (Ebooks/Epub, Ebooks/Mobi,
                               Ebooks/Pdf, shared Ebooks/Html sanitizing renderer), library folder,
                               cover thumbnails, background scanner.
  CoffeeReader.Api             ASP.NET Core host: cookie sessions, minimal API endpoints,
                               security headers, serves the web app.
web/                           React + TypeScript (Vite)
  src/api                      Typed API client and TanStack Query hooks
  src/features/library         Home (shelf board), library grid, book page, shelf actions
  src/features/reader          Reader shell, EPUB/MOBI paginator, PDF reader (PDF.js), panels,
                               search folding
  src/features/settings        Account, people and library administration
tests/CoffeeReader.Tests       Domain, parser, sanitizer, search and full API flow tests
```

How the main pieces work:

- **Library scanning.** The books folder is the source of truth for *which* books exist. Each file
  is identified by its SHA-256 hash, so a moved or renamed file keeps its identity and a copy of an
  existing book is reported instead of imported twice. A file-system watcher, a periodic scan and
  an on-demand scan all feed one background queue, and imports never run concurrently. When an
  update improves how a format is read, books already in the library are read again once on the
  next scan (details an administrator edited are kept).
- **Reading.** The server parses each chapter (tolerating the sloppy XHTML found in real EPUBs),
  strips anything executable with an allow-list sanitizer, and returns the body and stylesheets.
  The browser renders it in a same-origin iframe laid out with CSS multi-column: one column per page,
  or two for a spread. Page turns are GPU transforms. Scroll mode uses the same iframe as a single
  scrolling column. Positions are stored as a character offset into the chapter, which is why
  reflowing text, or switching between pages and scrolling, keeps you on the same sentence.
- **Formats.** Each format plugs in a parser (metadata, reading order, contents, cover) and a
  content provider. MOBI is decompressed (PalmDOC or Huffman/CDIC) and split at its page breaks
  into HTML chapters; `filepos` links become anchors and image records become resources, so MOBI
  uses the EPUB reader unchanged. PDFs are read with PDFium (metadata, outline, page text for
  search, first-page cover) and drawn in the browser by PDF.js, which fetches only the byte ranges
  it needs. Each PDF page is one "chapter", so progress and search work per page.
- **Search.** The server scans the chapters and returns the *n*-th match per chapter. The browser
  folds text with the same rules (case, accents, whitespace) to find and highlight that exact
  occurrence.
- **Security.** Passwords use ASP.NET Core Identity's PBKDF2 hasher. Sessions are HttpOnly,
  SameSite=Strict cookies tied to a security stamp that is re-checked every 30 seconds, so changing
  a password or removing a user signs them out everywhere. State-changing API calls also require a custom
  header. A strict Content-Security-Policy (inherited by the reader iframe) means book content can
  never run scripts, and files served out of books are sandboxed. Logins are rate limited.

## Development

Requirements: .NET 10 SDK and Node.js 22. PDFium's native library comes from NuGet (Linux, Windows
and macOS); PDF.js's fonts and decoders are copied into `web/public/pdfjs` by `npm run dev`/`build`.

```bash
dotnet run --project src/CoffeeReader.Api
```

Open <http://localhost:5200>. `dotnet build` (and so `dotnet run`) also builds the React app and copies
it into `src/CoffeeReader.Api/wwwroot`: `npm ci` runs when the lockfile changes and `npm run build`
when anything in `web/` changes, so TypeScript errors show up in the build output too. Pass
`-p:SkipWebBuild=true` to skip it; without Node.js it's skipped with a message. Development data lives
in `.dev/books` and `.dev/config`.

For instant reloads while working on the frontend, also run the Vite dev server and use
<http://localhost:5173> instead (it proxies `/api` to the API):

```bash
cd web && npm run dev
```

Tests:

```bash
dotnet test
```

```bash
cd web && npm run typecheck && npm test
```

Database migrations live in `src/CoffeeReader.Infrastructure/Persistence/Migrations` and are applied
automatically at startup. To add one after changing the model:

```bash
dotnet tool restore
```

```bash
dotnet ef migrations add <Name> --project src/CoffeeReader.Infrastructure --startup-project src/CoffeeReader.Api --output-dir Persistence/Migrations
```

Build the container image locally:

```bash
docker build -t coffeereader .
```

## Limitations and ideas

- KF8-only Kindle books (AZW3) aren't supported yet; convert them to EPUB (for example with
  Calibre). DRM-protected books can't be opened.
- Comic archives (CBZ/CBR) aren't supported.
- PDFs are shown as fixed pages; there's no reflow mode for them.
- No bookmarks, highlights or notes yet.
- No OPDS feed for third-party reader apps.
- Reading needs a connection to the server; there's no offline mode.

## License

Apache 2.0. See [LICENSE](LICENSE).
