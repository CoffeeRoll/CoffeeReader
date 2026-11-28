import { useQueryClient } from "@tanstack/react-query";
import { useCallback } from "react";
import { api } from "../../api/client";
import { moveBookLocally, queryKeys, type MoveBookArgs } from "../../api/queries";
import type { Shelf } from "../../api/types";
import { useToast } from "../../components/Toaster";

/**
 * Applies shelf changes to the cache immediately (so drag and drop never flickers), sends them to the
 * server, and rolls back if the server refuses.
 */
export function useShelfActions() {
  const client = useQueryClient();
  const toast = useToast();

  const commit = useCallback(
    (change: (shelves: Shelf[]) => Shelf[], request: () => Promise<unknown>) => {
      void client.cancelQueries({ queryKey: queryKeys.shelves });
      const previous = client.getQueryData<Shelf[]>(queryKeys.shelves) ?? [];
      client.setQueryData(queryKeys.shelves, change(previous));
      return request()
        .catch((error) => {
          client.setQueryData(queryKeys.shelves, previous);
          toast.error(error);
        })
        .finally(() => {
          void client.invalidateQueries({ queryKey: queryKeys.shelves });
          void client.invalidateQueries({ queryKey: ["books"], predicate: (q) => q.queryKey.length === 2 });
        });
    },
    [client, toast],
  );

  return {
    move: (args: MoveBookArgs) => commit((s) => moveBookLocally(s, args), () => api.post("/api/shelves/move", args)),

    arrange: (shelfId: number, bookIds: number[]) =>
      commit(
        (s) => s.map((shelf) => (shelf.id === shelfId ? { ...shelf, bookIds } : shelf)),
        () => api.put(`/api/shelves/${shelfId}/books`, { bookIds }),
      ),

    toggle: (shelfId: number, bookId: number, add: boolean) =>
      add
        ? commit((s) => moveBookLocally(s, { bookId, fromShelfId: null, toShelfId: shelfId, position: null }), () => api.post(`/api/shelves/${shelfId}/books`, { bookId }))
        : commit((s) => moveBookLocally(s, { bookId, fromShelfId: shelfId, toShelfId: null, position: null }), () => api.delete(`/api/shelves/${shelfId}/books/${bookId}`)),

    reorder: (shelfIds: number[]) =>
      commit(
        (s) => shelfIds.map((id, index) => ({ ...s.find((shelf) => shelf.id === id)!, sortOrder: index })),
        () => api.put("/api/shelves/order", { shelfIds }),
      ),

    rename: (shelfId: number, name: string) =>
      commit((s) => s.map((shelf) => (shelf.id === shelfId ? { ...shelf, name } : shelf)), () => api.put(`/api/shelves/${shelfId}`, { name })),

    remove: (shelfId: number) => commit((s) => s.filter((shelf) => shelf.id !== shelfId), () => api.delete(`/api/shelves/${shelfId}`)),

    create: async (name: string, bookIds: number[] = []) => {
      try {
        const shelf = await api.post<Shelf>("/api/shelves", { name, bookIds });
        client.setQueryData<Shelf[]>(queryKeys.shelves, (current) => [...(current ?? []), shelf]);
        void client.invalidateQueries({ queryKey: queryKeys.shelves });
        return shelf;
      } catch (error) {
        toast.error(error);
        return null;
      }
    },
  };
}
