import { X } from "lucide-react";
import { useEffect, useRef, type ReactNode } from "react";
import styles from "./Dialog.module.css";

interface Props {
  open: boolean;
  title: string;
  onClose: () => void;
  children: ReactNode;
  footer?: ReactNode;
  wide?: boolean;
}

/** A modal dialog; becomes a bottom sheet on phones. */
export function Dialog({ open, title, onClose, children, footer, wide }: Props) {
  const ref = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const dialog = ref.current;
    if (!dialog) return;
    if (open && !dialog.open) dialog.showModal();
    if (!open && dialog.open) dialog.close();
  }, [open]);

  return (
    <dialog
      ref={ref}
      className={`${styles.dialog} ${wide ? styles.wide : ""}`}
      onCancel={(event) => {
        event.preventDefault();
        onClose();
      }}
      onClick={(event) => {
        // Clicking the backdrop closes the dialog.
        if (event.target === ref.current) onClose();
      }}
      aria-labelledby="dialog-title"
    >
      {open && (
        <>
          <header className={styles.header}>
            <h2 id="dialog-title" className={styles.title}>
              {title}
            </h2>
            <button type="button" className="icon-btn" onClick={onClose} aria-label="Close">
              <X />
            </button>
          </header>
          <div className={styles.body}>{children}</div>
          {footer && <footer className={styles.footer}>{footer}</footer>}
        </>
      )}
    </dialog>
  );
}
