import { FolderSync, KeyRound, Moon, MoreHorizontal, Shield, Sun, SunMoon, Trash2, UserPlus } from "lucide-react";
import { useState, type FormEvent } from "react";
import { api, ApiError } from "../../api/client";
import { useCurrentUser, useLibraryStatus, useUserMutations, useUsers } from "../../api/queries";
import type { User, UserRole } from "../../api/types";
import { Dialog } from "../../components/Dialog";
import { Menu, MenuItem } from "../../components/Menu";
import { useToast } from "../../components/Toaster";
import { timeAgo } from "../../lib/format";
import { getAppTheme, setAppTheme, type AppTheme } from "../../lib/theme";
import styles from "./SettingsPage.module.css";

export function SettingsPage() {
  const user = useCurrentUser();
  return (
    <div className="page">
      <h1 className="page-title">Settings</h1>
      <div className={styles.layout}>
        <AppearanceCard />
        <PasswordCard />
        {user.role === "admin" && (
          <>
            <LibraryCard />
            <UsersCard currentUserId={user.id} />
          </>
        )}
      </div>
    </div>
  );
}

function AppearanceCard() {
  const [theme, setTheme] = useState<AppTheme>(getAppTheme);
  const choose = (value: AppTheme) => {
    setAppTheme(value);
    setTheme(value);
  };

  return (
    <section className={styles.card}>
      <h2 className={styles.cardTitle}>Appearance</h2>
      <p className="muted" style={{ fontSize: 14 }}>
        Applies to the library on this device. Reading themes and fonts are in the reader's <strong>Aa</strong> menu.
      </p>
      <div className={styles.themeOptions}>
        {([
          ["system", "Match system", <SunMoon key="s" />],
          ["light", "Light", <Sun key="l" />],
          ["dark", "Dark", <Moon key="d" />],
        ] as const).map(([value, label, icon]) => (
          <button key={value} type="button" className={`btn ${theme === value ? "btn-primary" : ""}`} onClick={() => choose(value)}>
            {icon} {label}
          </button>
        ))}
      </div>
    </section>
  );
}

function PasswordCard() {
  const toast = useToast();
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api.post("/api/auth/change-password", { currentPassword: current, newPassword: next });
      setCurrent("");
      setNext("");
      toast.show("Password changed. Other devices have been signed out.");
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Couldn't change your password.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className={styles.card}>
      <h2 className={styles.cardTitle}>Password</h2>
      <form onSubmit={submit} style={{ display: "grid", gap: 14 }}>
        {error && <div className="form-error">{error}</div>}
        <div className={styles.row}>
          <label className="field">
            <span>Current password</span>
            <input className="input" type="password" autoComplete="current-password" value={current} onChange={(e) => setCurrent(e.target.value)} required />
          </label>
          <label className="field">
            <span>New password</span>
            <input className="input" type="password" autoComplete="new-password" minLength={8} value={next} onChange={(e) => setNext(e.target.value)} required />
          </label>
        </div>
        <div>
          <button className="btn btn-primary" disabled={busy}>
            <KeyRound /> Change password
          </button>
        </div>
      </form>
    </section>
  );
}

function LibraryCard() {
  const { data: status, refetch } = useLibraryStatus();
  const toast = useToast();

  const scan = async () => {
    try {
      await api.post("/api/library/scan");
      toast.show("Scanning the library folder…");
      window.setTimeout(() => refetch(), 400);
    } catch (e) {
      toast.error(e);
    }
  };

  const removeMissing = async () => {
    if (!window.confirm("Forget books whose files are gone? Their reading progress and shelf places are removed too.")) return;
    try {
      const result = await api.delete<{ removed: number }>("/api/library/missing");
      toast.show(`Removed ${result.removed} missing books`);
      refetch();
    } catch (e) {
      toast.error(e);
    }
  };

  return (
    <section className={styles.card}>
      <div className={styles.cardHeader}>
        <h2 className={styles.cardTitle}>Library folder</h2>
        <button className="btn" onClick={scan} disabled={status?.isScanning}>
          <FolderSync /> {status?.isScanning ? "Scanning…" : "Scan now"}
        </button>
      </div>
      <p className="muted" style={{ fontSize: 14 }}>
        Books dropped into the mounted books folder are picked up automatically. Moving or renaming files keeps their progress and shelves.
      </p>
      {status && (
        <>
          <div className={styles.stats}>
            <div className={styles.stat}>
              <div className={styles.statValue}>{status.bookCount}</div>
              <div className={styles.statLabel}>books</div>
            </div>
            <div className={styles.stat}>
              <div className={styles.statValue}>{status.lastScan?.added ?? 0}</div>
              <div className={styles.statLabel}>added last scan</div>
            </div>
            <div className={styles.stat}>
              <div className={styles.statValue}>{status.missingCount}</div>
              <div className={styles.statLabel}>missing files</div>
            </div>
            <div className={styles.stat}>
              <div className={styles.statValue} style={{ fontSize: 16, paddingTop: 5 }}>
                {timeAgo(status.lastScanCompletedAt)}
              </div>
              <div className={styles.statLabel}>last scan</div>
            </div>
          </div>
          {status.lastError && <div className="form-error">Last scan failed: {status.lastError}</div>}
          {status.issues.length > 0 && (
            <div style={{ display: "grid", gap: 8 }}>
              <strong style={{ fontSize: 14 }}>Files that couldn't be added</strong>
              <ul className={styles.issues}>
                {status.issues.map((issue) => (
                  <li key={issue.path}>
                    <code>{issue.path}</code> — {issue.message}
                  </li>
                ))}
              </ul>
            </div>
          )}
          {status.missingCount > 0 && (
            <div>
              <button className="btn btn-sm btn-danger" onClick={removeMissing}>
                <Trash2 /> Forget {status.missingCount} missing books
              </button>
            </div>
          )}
        </>
      )}
    </section>
  );
}

function UsersCard({ currentUserId }: { currentUserId: number }) {
  const { data: users = [] } = useUsers();
  const mutations = useUserMutations();
  const toast = useToast();
  const [adding, setAdding] = useState(false);
  const [resetting, setResetting] = useState<User | null>(null);

  const run = async (action: () => Promise<unknown>, message: string) => {
    try {
      await action();
      toast.show(message);
    } catch (e) {
      toast.error(e);
    }
  };

  return (
    <section className={styles.card}>
      <div className={styles.cardHeader}>
        <h2 className={styles.cardTitle}>People</h2>
        <button className="btn" onClick={() => setAdding(true)}>
          <UserPlus /> Add person
        </button>
      </div>
      <p className="muted" style={{ fontSize: 14 }}>
        Everyone shares the same library, with their own shelves and reading progress. Administrators can add books' details, delete books and manage people.
      </p>
      <ul className={styles.users}>
        {users.map((u) => (
          <li key={u.id} className={styles.user}>
            <div className={styles.userAvatar}>{(u.displayName || u.username)[0].toUpperCase()}</div>
            <div className={styles.userInfo}>
              <span className={styles.userName}>
                {u.displayName} {u.id === currentUserId && <span className="muted">(you)</span>}
              </span>
              <span className={styles.userMeta}>
                @{u.username} · {u.role === "admin" ? "Administrator" : "Reader"} · last seen {timeAgo(u.lastLoginAt)}
              </span>
            </div>
            <Menu
              trigger={(props) => (
                <button type="button" className="icon-btn" aria-label={`Options for ${u.username}`} {...props}>
                  <MoreHorizontal />
                </button>
              )}
            >
              {(close) => (
                <>
                  <MenuItem
                    icon={<Shield />}
                    onSelect={() => {
                      close();
                      const role: UserRole = u.role === "admin" ? "user" : "admin";
                      run(() => mutations.update.mutateAsync({ id: u.id, role }), role === "admin" ? `${u.displayName} is now an administrator` : `${u.displayName} is now a reader`);
                    }}
                  >
                    {u.role === "admin" ? "Make reader" : "Make administrator"}
                  </MenuItem>
                  <MenuItem icon={<KeyRound />} onSelect={() => (close(), setResetting(u))}>
                    Reset password
                  </MenuItem>
                  {u.id !== currentUserId && (
                    <MenuItem
                      icon={<Trash2 />}
                      danger
                      onSelect={() => {
                        close();
                        if (window.confirm(`Remove ${u.displayName}? Their shelves and reading progress are deleted.`)) {
                          run(() => mutations.remove.mutateAsync(u.id), `Removed ${u.displayName}`);
                        }
                      }}
                    >
                      Remove
                    </MenuItem>
                  )}
                </>
              )}
            </Menu>
          </li>
        ))}
      </ul>
      <AddUserDialog open={adding} onClose={() => setAdding(false)} />
      <ResetPasswordDialog user={resetting} onClose={() => setResetting(null)} />
    </section>
  );
}

function AddUserDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { create } = useUserMutations();
  const toast = useToast();
  const [form, setForm] = useState({ username: "", displayName: "", password: "", role: "user" as UserRole });
  const [error, setError] = useState<string | null>(null);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    try {
      const user = await create.mutateAsync(form);
      toast.show(`Added ${user.displayName}`);
      setForm({ username: "", displayName: "", password: "", role: "user" });
      onClose();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Couldn't add that person.");
    }
  };

  return (
    <Dialog open={open} title="Add a person" onClose={onClose}>
      <form onSubmit={submit} style={{ display: "grid", gap: 14 }}>
        {error && <div className="form-error">{error}</div>}
        <label className="field">
          <span>Username</span>
          <input className="input" autoCapitalize="none" value={form.username} onChange={(e) => setForm({ ...form, username: e.target.value })} required autoFocus />
        </label>
        <label className="field">
          <span>Display name</span>
          <input className="input" value={form.displayName} onChange={(e) => setForm({ ...form, displayName: e.target.value })} placeholder="Optional" />
        </label>
        <label className="field">
          <span>Temporary password</span>
          <input className="input" type="text" minLength={8} value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} required />
        </label>
        <label className="field">
          <span>Role</span>
          <select className="select" value={form.role} onChange={(e) => setForm({ ...form, role: e.target.value as UserRole })}>
            <option value="user">Reader</option>
            <option value="admin">Administrator</option>
          </select>
        </label>
        <div style={{ display: "flex", justifyContent: "flex-end", gap: 10 }}>
          <button type="button" className="btn" onClick={onClose}>
            Cancel
          </button>
          <button className="btn btn-primary" disabled={create.isPending}>
            Add person
          </button>
        </div>
      </form>
    </Dialog>
  );
}

function ResetPasswordDialog({ user, onClose }: { user: User | null; onClose: () => void }) {
  const { resetPassword } = useUserMutations();
  const toast = useToast();
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!user) return;
    setError(null);
    try {
      await resetPassword.mutateAsync({ id: user.id, newPassword: password });
      toast.show(`Password reset for ${user.displayName}`);
      setPassword("");
      onClose();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Couldn't reset the password.");
    }
  };

  return (
    <Dialog open={user !== null} title={`Reset password for ${user?.displayName ?? ""}`} onClose={onClose}>
      <form onSubmit={submit} style={{ display: "grid", gap: 14 }}>
        {error && <div className="form-error">{error}</div>}
        <p className="muted" style={{ fontSize: 14 }}>
          They'll be signed out everywhere and can sign in with the new password.
        </p>
        <label className="field">
          <span>New password</span>
          <input className="input" type="text" minLength={8} value={password} onChange={(e) => setPassword(e.target.value)} required autoFocus />
        </label>
        <div style={{ display: "flex", justifyContent: "flex-end", gap: 10 }}>
          <button type="button" className="btn" onClick={onClose}>
            Cancel
          </button>
          <button className="btn btn-primary" disabled={resetPassword.isPending}>
            Reset password
          </button>
        </div>
      </form>
    </Dialog>
  );
}
