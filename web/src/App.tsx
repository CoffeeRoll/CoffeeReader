import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { lazy, Suspense } from "react";
import { BrowserRouter, Navigate, Route, Routes } from "react-router";
import { ApiError } from "./api/client";
import { AuthGate } from "./auth/AuthGate";
import { LoginPage, SetupPage } from "./auth/AuthPages";
import { AppShell } from "./components/AppShell";
import { ToastProvider } from "./components/Toaster";
import { BookPage } from "./features/library/BookPage";
import { HomePage } from "./features/library/HomePage";
import { LibraryPage } from "./features/library/LibraryPage";
import { SettingsPage } from "./features/settings/SettingsPage";
import { UploadProvider } from "./features/upload/UploadProvider";

const ReaderPage = lazy(() => import("./features/reader/ReaderPage"));

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: (count, error) => !(error instanceof ApiError && error.status >= 400 && error.status < 500) && count < 2,
      refetchOnWindowFocus: true,
    },
  },
});

const fallback = (
  <div className="center-fill" style={{ minHeight: "100dvh" }}>
    <div className="spinner" />
  </div>
);

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <ToastProvider>
        <BrowserRouter>
          <AuthGate>
            <Routes>
              <Route path="/setup" element={<SetupPage />} />
              <Route path="/login" element={<LoginPage />} />
              <Route
                path="/read/:bookId"
                element={
                  <Suspense fallback={fallback}>
                    <ReaderPage />
                  </Suspense>
                }
              />
              <Route
                element={
                  <UploadProvider>
                    <AppShell />
                  </UploadProvider>
                }
              >
                <Route index element={<HomePage />} />
                <Route path="/library" element={<LibraryPage />} />
                <Route path="/books/:bookId" element={<BookPage />} />
                <Route path="/settings" element={<SettingsPage />} />
              </Route>
              <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
          </AuthGate>
        </BrowserRouter>
      </ToastProvider>
    </QueryClientProvider>
  );
}
