import { useQueryClient } from "@tanstack/react-query";
import { useState, type FormEvent } from "react";
import { useNavigate } from "react-router";
import { api, ApiError } from "../api/client";
import { queryKeys } from "../api/queries";
import type { User } from "../api/types";
import styles from "./AuthPages.module.css";

function useSignedIn() {
  const client = useQueryClient();
  const navigate = useNavigate();
  return async (user: User) => {
    client.setQueryData(queryKeys.auth, { setupRequired: false, user });
    navigate("/", { replace: true });
  };
}

export function LoginPage() {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [rememberMe, setRememberMe] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const signedIn = useSignedIn();

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await signedIn(await api.post<User>("/api/auth/login", { username, password, rememberMe }));
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Couldn't reach the server.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className={styles.page}>
      <form className={styles.card} onSubmit={submit}>
        <img className={styles.logo} src="/favicon.svg" alt="" />
        <div className={styles.heading}>
          <h1 className={styles.title}>Welcome back</h1>
          <p className="muted">Sign in to pick up where you left off.</p>
        </div>
        {error && <div className="form-error">{error}</div>}
        <label className="field">
          <span>Username</span>
          <input className="input" autoComplete="username" autoCapitalize="none" value={username} onChange={(e) => setUsername(e.target.value)} required autoFocus />
        </label>
        <label className="field">
          <span>Password</span>
          <input className="input" type="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} required />
        </label>
        <label className={styles.remember}>
          <input type="checkbox" checked={rememberMe} onChange={(e) => setRememberMe(e.target.checked)} />
          Keep me signed in on this device
        </label>
        <button className="btn btn-primary btn-lg" disabled={busy}>
          {busy ? "Signing in…" : "Sign in"}
        </button>
      </form>
    </div>
  );
}

export function SetupPage() {
  const [username, setUsername] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const signedIn = useSignedIn();

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (password !== confirm) {
      setError("The passwords don't match.");
      return;
    }

    setBusy(true);
    setError(null);
    try {
      await signedIn(await api.post<User>("/api/auth/setup", { username, displayName, password }));
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Couldn't reach the server.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className={styles.page}>
      <form className={styles.card} onSubmit={submit}>
        <img className={styles.logo} src="/favicon.svg" alt="" />
        <div className={styles.heading}>
          <h1 className={styles.title}>Set up CoffeeReader</h1>
          <p className="muted">Create the administrator account. You can invite others from Settings afterwards.</p>
        </div>
        {error && <div className="form-error">{error}</div>}
        <label className="field">
          <span>Username</span>
          <input className="input" autoComplete="username" autoCapitalize="none" value={username} onChange={(e) => setUsername(e.target.value)} required autoFocus />
        </label>
        <label className="field">
          <span>Display name</span>
          <input className="input" autoComplete="name" value={displayName} onChange={(e) => setDisplayName(e.target.value)} placeholder="Optional" />
        </label>
        <label className="field">
          <span>Password</span>
          <input className="input" type="password" autoComplete="new-password" minLength={8} value={password} onChange={(e) => setPassword(e.target.value)} required />
        </label>
        <label className="field">
          <span>Confirm password</span>
          <input className="input" type="password" autoComplete="new-password" minLength={8} value={confirm} onChange={(e) => setConfirm(e.target.value)} required />
        </label>
        <button className="btn btn-primary btn-lg" disabled={busy}>
          {busy ? "Creating account…" : "Create account"}
        </button>
      </form>
    </div>
  );
}
