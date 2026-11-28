import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from "react";
import styles from "./Toaster.module.css";

interface Toast {
  id: number;
  message: string;
  kind: "info" | "error";
}

interface ToastApi {
  show: (message: string) => void;
  error: (error: unknown) => void;
}

const ToastContext = createContext<ToastApi | null>(null);
let nextId = 1;

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);

  const push = useCallback((message: string, kind: Toast["kind"]) => {
    const id = nextId++;
    setToasts((current) => [...current.slice(-2), { id, message, kind }]);
    window.setTimeout(() => setToasts((current) => current.filter((t) => t.id !== id)), kind === "error" ? 6000 : 3500);
  }, []);

  const api = useMemo<ToastApi>(
    () => ({
      show: (message) => push(message, "info"),
      error: (error) => push(error instanceof Error ? error.message : String(error), "error"),
    }),
    [push],
  );

  return (
    <ToastContext.Provider value={api}>
      {children}
      <div className={styles.region} role="status" aria-live="polite">
        {toasts.map((toast) => (
          <div key={toast.id} className={`${styles.toast} ${toast.kind === "error" ? styles.error : ""}`}>
            {toast.message}
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast(): ToastApi {
  const context = useContext(ToastContext);
  if (!context) throw new Error("useToast must be used inside ToastProvider");
  return context;
}
