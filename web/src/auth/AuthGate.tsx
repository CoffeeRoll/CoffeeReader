import { useQueryClient } from "@tanstack/react-query";
import { useEffect, type ReactNode } from "react";
import { Navigate, useLocation } from "react-router";
import { onUnauthorized } from "../api/client";
import { queryKeys, useAuthStatus } from "../api/queries";

/** Routes to setup or sign-in until there's a signed-in user. */
export function AuthGate({ children }: { children: ReactNode }) {
  const { data, isLoading, isError, refetch } = useAuthStatus();
  const location = useLocation();
  const client = useQueryClient();

  useEffect(() => onUnauthorized(() => client.invalidateQueries({ queryKey: queryKeys.auth })), [client]);

  if (isLoading) {
    return (
      <div className="center-fill" style={{ minHeight: "100dvh" }}>
        <div className="spinner" />
      </div>
    );
  }

  if (isError || !data) {
    return (
      <div className="center-fill" style={{ minHeight: "100dvh", textAlign: "center", padding: 24 }}>
        <div style={{ display: "grid", gap: 12, justifyItems: "center" }}>
          <p>CoffeeReader's server isn't responding.</p>
          <button className="btn" onClick={() => refetch()}>
            Try again
          </button>
        </div>
      </div>
    );
  }

  if (data.setupRequired) {
    return location.pathname === "/setup" ? <>{children}</> : <Navigate to="/setup" replace />;
  }

  if (!data.user) {
    return location.pathname === "/login" ? <>{children}</> : <Navigate to="/login" replace state={{ from: location.pathname }} />;
  }

  if (location.pathname === "/login" || location.pathname === "/setup") {
    return <Navigate to="/" replace />;
  }

  return <>{children}</>;
}
