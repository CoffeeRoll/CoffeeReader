import { CircleAlert, CircleCheck, Copy, FileUp, LoaderCircle, X } from "lucide-react";
import { Link } from "react-router";
import { useUploads } from "./UploadProvider";
import styles from "./UploadPanel.module.css";

export function UploadPanel({ dragging }: { dragging: boolean }) {
  const { items, clearFinished } = useUploads();
  const active = items.filter((i) => i.state === "queued" || i.state === "uploading").length;

  return (
    <>
      {dragging && (
        <div className={styles.overlay}>
          <div className={styles.dropTarget}>
            <FileUp />
            <div className={styles.dropTitle}>Drop to add to your library</div>
            <p className="muted">Books are saved to the library folder and appear right away.</p>
          </div>
        </div>
      )}

      {items.length > 0 && (
        <section className={styles.panel} aria-label="Uploads">
          <header className={styles.panelHeader}>
            <span>{active > 0 ? `Uploading ${active} of ${items.length}…` : "Uploads finished"}</span>
            {active === 0 && (
              <button type="button" className="icon-btn" onClick={clearFinished} aria-label="Dismiss uploads">
                <X />
              </button>
            )}
          </header>
          <ul className={styles.list}>
            {items.map((item) => (
              <li key={item.id} className={styles.item}>
                {item.state === "done" ? (
                  <CircleCheck className={styles.ok} />
                ) : item.state === "duplicate" ? (
                  <Copy className={styles.warn} />
                ) : item.state === "failed" ? (
                  <CircleAlert className={styles.fail} />
                ) : (
                  <LoaderCircle className={`${styles.warn} spin`} style={{ animation: "spin 1s linear infinite" }} />
                )}
                <span className={styles.name} title={item.file.name}>
                  {item.result?.title ?? item.file.name}
                </span>
                {item.state === "uploading" && (
                  <div className={styles.bar}>
                    <div className={styles.barFill} style={{ width: `${Math.round(item.progress * 100)}%` }} />
                  </div>
                )}
                {item.state === "queued" && <span className={styles.detail}>Waiting…</span>}
                {item.state === "done" && item.result?.bookId && (
                  <span className={styles.detail}>
                    Added · <Link to={`/books/${item.result.bookId}`}>View</Link>
                  </span>
                )}
                {item.state === "duplicate" && item.result?.bookId && (
                  <span className={styles.detail}>
                    Already in your library · <Link to={`/books/${item.result.bookId}`}>View</Link>
                  </span>
                )}
                {item.state === "failed" && <span className={`${styles.detail} ${styles.fail}`}>{item.error}</span>}
              </li>
            ))}
          </ul>
        </section>
      )}
    </>
  );
}
