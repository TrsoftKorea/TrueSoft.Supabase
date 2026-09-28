// supabase-js 흉내. 테이블은 메모리, 사용자는 globalThis.__user.
export const db = globalThis.__db ??= { apple_auth_tokens: new Map() };

export function createClient() {
  return {
    auth: { getUser: async () => ({ data: { user: globalThis.__user ?? null }, error: null }) },
    from(table) {
      const t = db[table];
      return {
        select() {
          return { eq(_c, v) { return { maybeSingle: async () => ({ data: t.get(v) ?? null, error: null }) }; } };
        },
        delete() { return { eq: async (_c, v) => { t.delete(v); return { error: null }; } }; },
        upsert: async (row) => { t.set(row.account_id, row); return { error: null }; },
      };
    },
  };
}
