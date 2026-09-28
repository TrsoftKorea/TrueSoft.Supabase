// "npm:@supabase/supabase-js" 를 메모리 흉내(supabase-stub.mjs)로 바꿔 끼운다.
const stub = new URL("./supabase-stub.mjs", import.meta.url).href;

export async function resolve(specifier, context, next) {
  if (specifier.startsWith("npm:@supabase/supabase-js")) return { url: stub, shortCircuit: true };
  return next(specifier, context);
}
