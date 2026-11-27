/** A failed API call, carrying the server's problem details when it sent them. */
export class ApiError extends Error {
  readonly status: number;
  readonly errors: Record<string, string[]>;

  constructor(status: number, message: string, errors: Record<string, string[]> = {}) {
    super(message);
    this.status = status;
    this.errors = errors;
  }
}

type Listener = () => void;
const unauthorizedListeners = new Set<Listener>();

/** Lets the auth layer react when a session expires mid-use. */
export function onUnauthorized(listener: Listener): () => void {
  unauthorizedListeners.add(listener);
  return () => unauthorizedListeners.delete(listener);
}

// Sent with every request; the server rejects state changes without it (CSRF defence).
const baseHeaders = { "X-Requested-With": "CoffeeReader" };

async function toError(response: Response): Promise<ApiError> {
  let message = response.statusText || "Something went wrong";
  let errors: Record<string, string[]> = {};
  try {
    const body = await response.json();
    message = body.detail ?? body.title ?? message;
    errors = body.errors ?? {};
  } catch {
    // Not JSON; keep the status text.
  }

  if (response.status === 413) {
    message = "That file is too large to upload.";
  } else if (response.status === 429) {
    message = "Too many attempts. Wait a minute and try again.";
  }

  return new ApiError(response.status, message, errors);
}

export async function request<T>(method: string, url: string, body?: unknown, init?: RequestInit): Promise<T> {
  const headers: Record<string, string> = { ...baseHeaders, Accept: "application/json" };
  if (body !== undefined) {
    headers["Content-Type"] = "application/json";
  }

  const response = await fetch(url, {
    method,
    credentials: "same-origin",
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
    ...init,
  });

  if (response.status === 401 && !url.startsWith("/api/auth/")) {
    unauthorizedListeners.forEach((listener) => listener());
  }

  if (!response.ok) {
    throw await toError(response);
  }

  if (response.status === 204 || response.headers.get("Content-Length") === "0") {
    return undefined as T;
  }

  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

export const api = {
  get: <T>(url: string, init?: RequestInit) => request<T>("GET", url, undefined, init),
  post: <T>(url: string, body?: unknown) => request<T>("POST", url, body),
  put: <T>(url: string, body?: unknown) => request<T>("PUT", url, body),
  delete: <T>(url: string) => request<T>("DELETE", url),
};

/** Uploads one file with progress reporting (fetch can't report upload progress). */
export function uploadFile<T>(url: string, file: File, onProgress: (fraction: number) => void, signal?: AbortSignal): Promise<T> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open("POST", url);
    xhr.withCredentials = true;
    xhr.setRequestHeader("X-Requested-With", baseHeaders["X-Requested-With"]);
    xhr.responseType = "json";
    xhr.upload.onprogress = (event) => {
      if (event.lengthComputable) {
        onProgress(event.loaded / event.total);
      }
    };
    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        resolve(xhr.response as T);
        return;
      }

      const body = xhr.response as { detail?: string; title?: string } | null;
      const message = xhr.status === 413 ? "That file is too large to upload." : body?.detail ?? body?.title ?? `Upload failed (${xhr.status})`;
      reject(new ApiError(xhr.status, message));
    };
    xhr.onerror = () => reject(new ApiError(0, "The upload was interrupted. Check your connection."));
    xhr.onabort = () => reject(new ApiError(0, "Upload cancelled."));
    signal?.addEventListener("abort", () => xhr.abort());

    const form = new FormData();
    form.append("files", file, file.name);
    xhr.send(form);
  });
}

export function coverUrl(book: { id: number; coverVersion: number }): string {
  return `/api/books/${book.id}/cover?v=${book.coverVersion}`;
}

export function bookFileUrl(bookId: number, path: string): string {
  return `/api/books/${bookId}/files/${path.split("/").map(encodeURIComponent).join("/")}`;
}
