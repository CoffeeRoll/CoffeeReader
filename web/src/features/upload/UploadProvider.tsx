import { useQueryClient } from "@tanstack/react-query";
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { uploadFile } from "../../api/client";
import { queryKeys, useLibraryFormats } from "../../api/queries";
import type { UploadResult } from "../../api/types";
import { UploadPanel } from "./UploadPanel";

export interface UploadItem {
  id: number;
  file: File;
  state: "queued" | "uploading" | "done" | "duplicate" | "failed";
  progress: number;
  result?: UploadResult;
  error?: string;
}

interface UploadApi {
  items: UploadItem[];
  openPicker: () => void;
  addFiles: (files: Iterable<File>) => void;
  clearFinished: () => void;
  accept: string;
}

const UploadContext = createContext<UploadApi | null>(null);
let nextId = 1;

/**
 * Queues books for upload one at a time (so each gets its own progress and a failure
 * doesn't sink the rest) and accepts files dropped anywhere on the page.
 */
export function UploadProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<UploadItem[]>([]);
  const [dragging, setDragging] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  const busy = useRef(false);
  const client = useQueryClient();
  const formats = useLibraryFormats();
  const extensions = useMemo(() => formats.data?.extensions ?? [".epub"], [formats.data]);
  const accept = extensions.join(",");

  const update = useCallback((id: number, patch: Partial<UploadItem>) => {
    setItems((current) => current.map((item) => (item.id === id ? { ...item, ...patch } : item)));
  }, []);

  const addFiles = useCallback(
    (files: Iterable<File>) => {
      const added: UploadItem[] = [];
      for (const file of files) {
        const supported = extensions.some((ext) => file.name.toLowerCase().endsWith(ext));
        const tooBig = formats.data && file.size > formats.data.maxUploadBytes;
        added.push({
          id: nextId++,
          file,
          progress: 0,
          state: supported && !tooBig ? "queued" : "failed",
          error: !supported ? `Only ${extensions.join(", ")} files are supported.` : tooBig ? "This file is larger than the upload limit." : undefined,
        });
      }

      setItems((current) => [...current, ...added]);
    },
    [extensions, formats.data],
  );

  // Work through the queue.
  useEffect(() => {
    if (busy.current) return;
    const next = items.find((item) => item.state === "queued");
    if (!next) return;

    busy.current = true;
    update(next.id, { state: "uploading" });
    uploadFile<UploadResult[]>("/api/books/upload", next.file, (progress) => update(next.id, { progress }))
      .then(([result]) => {
        const state = result.status === "added" ? "done" : result.status === "duplicate" ? "duplicate" : "failed";
        update(next.id, { state, result, progress: 1, error: result.error ?? undefined });
        if (result.status === "added") {
          client.invalidateQueries({ queryKey: queryKeys.books });
        }
      })
      .catch((error: Error) => update(next.id, { state: "failed", error: error.message }))
      .finally(() => {
        busy.current = false;
        setItems((current) => [...current]); // nudge the queue
      });
  }, [items, update, client]);

  // Accept files dragged in from the desktop anywhere in the app.
  useEffect(() => {
    let depth = 0;
    const hasFiles = (event: DragEvent) => Array.from(event.dataTransfer?.types ?? []).includes("Files");
    const onEnter = (event: DragEvent) => {
      if (!hasFiles(event)) return;
      depth++;
      setDragging(true);
    };
    const onLeave = (event: DragEvent) => {
      if (!hasFiles(event)) return;
      depth = Math.max(0, depth - 1);
      if (depth === 0) setDragging(false);
    };
    const onOver = (event: DragEvent) => {
      if (hasFiles(event)) event.preventDefault();
    };
    const onDrop = (event: DragEvent) => {
      if (!hasFiles(event)) return;
      event.preventDefault();
      depth = 0;
      setDragging(false);
      if (event.dataTransfer?.files.length) addFiles(Array.from(event.dataTransfer.files));
    };
    window.addEventListener("dragenter", onEnter);
    window.addEventListener("dragleave", onLeave);
    window.addEventListener("dragover", onOver);
    window.addEventListener("drop", onDrop);
    return () => {
      window.removeEventListener("dragenter", onEnter);
      window.removeEventListener("dragleave", onLeave);
      window.removeEventListener("dragover", onOver);
      window.removeEventListener("drop", onDrop);
    };
  }, [addFiles]);

  const api = useMemo<UploadApi>(
    () => ({
      items,
      accept,
      addFiles,
      openPicker: () => inputRef.current?.click(),
      clearFinished: () => setItems((current) => current.filter((item) => item.state === "queued" || item.state === "uploading")),
    }),
    [items, accept, addFiles],
  );

  return (
    <UploadContext.Provider value={api}>
      {children}
      <input
        ref={inputRef}
        type="file"
        accept={accept}
        multiple
        hidden
        onChange={(event) => {
          if (event.target.files) addFiles(Array.from(event.target.files));
          event.target.value = "";
        }}
      />
      <UploadPanel dragging={dragging} />
    </UploadContext.Provider>
  );
}

export function useUploads(): UploadApi {
  const context = useContext(UploadContext);
  if (!context) throw new Error("useUploads must be used inside UploadProvider");
  return context;
}
