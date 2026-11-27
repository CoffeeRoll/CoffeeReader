import { useMutation, useQuery, useQueryClient, type QueryClient } from "@tanstack/react-query";
import { api } from "./client";
import type {
  AuthStatus,
  BookDetails,
  BookSummary,
  LibraryFormats,
  LibraryStatus,
  Shelf,
  UpdateBookRequest,
  User,
  UserRole,
} from "./types";

export const queryKeys = {
  auth: ["auth"] as const,
  books: ["books"] as const,
  book: (id: number) => ["books", id] as const,
  shelves: ["shelves"] as const,
  users: ["users"] as const,
  libraryStatus: ["library", "status"] as const,
  libraryFormats: ["library", "formats"] as const,
  manifest: (id: number) => ["reader", id, "manifest"] as const,
  chapter: (id: number, index: number) => ["reader", id, "chapter", index] as const,
};

// ---- Auth -------------------------------------------------------------------------------------------

export function useAuthStatus() {
  return useQuery({
    queryKey: queryKeys.auth,
    queryFn: () => api.get<AuthStatus>("/api/auth/status"),
    staleTime: 60_000,
  });
}

export function useCurrentUser(): User {
  const { data } = useAuthStatus();
  if (!data?.user) {
    throw new Error("useCurrentUser must be used inside the signed-in app.");
  }

  return data.user;
}

// ---- Books ------------------------------------------------------------------------------------------

export function useBooks() {
  return useQuery({
    queryKey: queryKeys.books,
    queryFn: () => api.get<BookSummary[]>("/api/books"),
    staleTime: 30_000,
  });
}

export function useBook(id: number) {
  return useQuery({
    queryKey: queryKeys.book(id),
    queryFn: () => api.get<BookDetails>(`/api/books/${id}`),
  });
}

export function useUpdateBook(id: number) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (request: UpdateBookRequest) => api.put<BookDetails>(`/api/books/${id}`, request),
    onSuccess: (book) => {
      client.setQueryData(queryKeys.book(id), book);
      client.invalidateQueries({ queryKey: queryKeys.books });
    },
  });
}

export function useDeleteBook() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => api.delete<void>(`/api/books/${id}`),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: queryKeys.books });
      client.invalidateQueries({ queryKey: queryKeys.shelves });
    },
  });
}

export function useSetFinished(id: number) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (finished: boolean) => api.put<unknown>(`/api/books/${id}/progress/finished`, { finished }),
    onSuccess: () => invalidateBook(client, id),
  });
}

export function invalidateBook(client: QueryClient, id: number) {
  client.invalidateQueries({ queryKey: queryKeys.books });
  client.invalidateQueries({ queryKey: queryKeys.book(id) });
  client.invalidateQueries({ queryKey: queryKeys.manifest(id) });
}

// ---- Shelves ----------------------------------------------------------------------------------------

export function useShelves() {
  return useQuery({
    queryKey: queryKeys.shelves,
    queryFn: () => api.get<Shelf[]>("/api/shelves"),
    staleTime: 30_000,
  });
}

/** Wraps a shelf mutation with an optimistic update that's rolled back if the server says no. */
function useShelfMutation<TArgs>(
  mutationFn: (args: TArgs) => Promise<unknown>,
  optimistic?: (shelves: Shelf[], args: TArgs) => Shelf[],
) {
  const client = useQueryClient();
  return useMutation({
    mutationFn,
    onMutate: async (args: TArgs) => {
      await client.cancelQueries({ queryKey: queryKeys.shelves });
      const previous = client.getQueryData<Shelf[]>(queryKeys.shelves);
      if (previous && optimistic) {
        client.setQueryData(queryKeys.shelves, optimistic(previous, args));
      }

      return { previous };
    },
    onError: (_error, _args, context) => {
      if (context?.previous) {
        client.setQueryData(queryKeys.shelves, context.previous);
      }
    },
    onSettled: () => {
      client.invalidateQueries({ queryKey: queryKeys.shelves });
      client.invalidateQueries({ queryKey: ["books"], predicate: (q) => q.queryKey.length === 2 });
    },
  });
}

export function useCreateShelf() {
  return useShelfMutation((args: { name: string; bookIds?: number[] }) => api.post<Shelf>("/api/shelves", args));
}

export function useRenameShelf() {
  return useShelfMutation(
    (args: { id: number; name: string }) => api.put<Shelf>(`/api/shelves/${args.id}`, { name: args.name }),
    (shelves, args) => shelves.map((s) => (s.id === args.id ? { ...s, name: args.name } : s)),
  );
}

export function useDeleteShelf() {
  return useShelfMutation(
    (id: number) => api.delete<void>(`/api/shelves/${id}`),
    (shelves, id) => shelves.filter((s) => s.id !== id),
  );
}

export function useReorderShelves() {
  return useShelfMutation(
    (shelfIds: number[]) => api.put<Shelf[]>("/api/shelves/order", { shelfIds }),
    (shelves, ids) => ids.map((id, index) => ({ ...shelves.find((s) => s.id === id)!, sortOrder: index })),
  );
}

export function useSetShelfBooks() {
  return useShelfMutation(
    (args: { id: number; bookIds: number[] }) => api.put<Shelf>(`/api/shelves/${args.id}/books`, { bookIds: args.bookIds }),
    (shelves, args) => shelves.map((s) => (s.id === args.id ? { ...s, bookIds: args.bookIds } : s)),
  );
}

export interface MoveBookArgs {
  bookId: number;
  fromShelfId: number | null;
  toShelfId: number | null;
  position: number | null;
}

export function moveBookLocally(shelves: Shelf[], args: MoveBookArgs): Shelf[] {
  return shelves.map((shelf) => {
    let ids = shelf.bookIds;
    if (shelf.id === args.fromShelfId && args.fromShelfId !== args.toShelfId) {
      ids = ids.filter((id) => id !== args.bookId);
    }

    if (shelf.id === args.toShelfId) {
      ids = ids.filter((id) => id !== args.bookId);
      const position = Math.min(args.position ?? ids.length, ids.length);
      ids = [...ids.slice(0, position), args.bookId, ...ids.slice(position)];
    }

    return ids === shelf.bookIds ? shelf : { ...shelf, bookIds: ids };
  });
}

export function useMoveBook() {
  return useShelfMutation((args: MoveBookArgs) => api.post<Shelf[]>("/api/shelves/move", args), moveBookLocally);
}

// ---- Library & users --------------------------------------------------------------------------------

export function useLibraryStatus(enabled = true) {
  return useQuery({
    queryKey: queryKeys.libraryStatus,
    queryFn: () => api.get<LibraryStatus>("/api/library/status"),
    enabled,
    refetchInterval: (query) => (query.state.data?.isScanning ? 1500 : false),
  });
}

export function useLibraryFormats() {
  return useQuery({
    queryKey: queryKeys.libraryFormats,
    queryFn: () => api.get<LibraryFormats>("/api/library/formats"),
    staleTime: Infinity,
  });
}

export function useUsers() {
  return useQuery({ queryKey: queryKeys.users, queryFn: () => api.get<User[]>("/api/users") });
}

export function useUserMutations() {
  const client = useQueryClient();
  const onSuccess = () => client.invalidateQueries({ queryKey: queryKeys.users });
  return {
    create: useMutation({
      mutationFn: (args: { username: string; displayName: string; password: string; role: UserRole }) => api.post<User>("/api/users", args),
      onSuccess,
    }),
    update: useMutation({
      mutationFn: (args: { id: number; displayName?: string; role?: UserRole }) => api.put<User>(`/api/users/${args.id}`, args),
      onSuccess,
    }),
    resetPassword: useMutation({
      mutationFn: (args: { id: number; newPassword: string }) => api.post<void>(`/api/users/${args.id}/reset-password`, { newPassword: args.newPassword }),
    }),
    remove: useMutation({
      mutationFn: (id: number) => api.delete<void>(`/api/users/${id}`),
      onSuccess,
    }),
  };
}
