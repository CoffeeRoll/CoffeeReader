import type { ReactNode } from "react";
import styles from "./EmptyState.module.css";

export function EmptyState({ icon, title, children, actions }: { icon: ReactNode; title: string; children?: ReactNode; actions?: ReactNode }) {
  return (
    <div className={styles.empty}>
      <div className={styles.icon}>{icon}</div>
      <h2 className={styles.title}>{title}</h2>
      {children && <p className="muted">{children}</p>}
      {actions && <div className={styles.actions}>{actions}</div>}
    </div>
  );
}
