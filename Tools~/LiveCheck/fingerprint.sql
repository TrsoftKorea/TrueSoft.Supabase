-- 실제 서버 대조(check.mjs)가 프로젝트마다 돌리는 구조 요약. 한 줄 = 대상 하나(axis·name)와 그 내용의 요약값.
-- 값(행·비밀값)은 읽지 않는다. 함수 본문은 주석과 공백을 빼고 요약한다 — install.sql 과 같은 방법으로 대조하려고.
with
ext_objs as (select objid from pg_depend where deptype = 'e'),
roles(r) as (values ('anon'), ('authenticated'), ('service_role'), ('PUBLIC')),
fn as (
  select p.oid as foid, p.proname::text as pname,
         p.proname || '(' || pg_get_function_identity_arguments(p.oid) || ')' as name,
         p.prosrc, p.prosecdef, p.provolatile, p.proconfig, p.proacl, p.proowner
    from pg_proc p join pg_namespace n on n.oid = p.pronamespace
   where n.nspname = 'public' and p.oid not in (select objid from ext_objs)
),
tbl as (
  select c.oid, c.relname::text as name, c.relkind, c.relrowsecurity, c.relacl, c.relowner
    from pg_class c join pg_namespace n on n.oid = c.relnamespace
   where n.nspname = 'public' and c.relkind in ('r', 'p', 'v', 'm') and c.oid not in (select objid from ext_objs)
)
select 'function' as axis, name, pname as owner_name,
       md5(regexp_replace(regexp_replace(prosrc, '--[^\n]*', '', 'g'), '\s+', '', 'g')) as body,
       md5(pg_get_function_result(foid) || '|' || prosecdef::text || '|' || provolatile::text || '|' || coalesce(array_to_string(proconfig, ','), '')) as meta
  from fn
union all
select 'function_grant', name, pname,
       coalesce((select string_agg(g, ',' order by g)
                   from (select (case when a.grantee = 0 then 'PUBLIC' else pg_get_userbyid(a.grantee) end) || ':' || a.privilege_type as g
                           from aclexplode(coalesce(proacl, acldefault('f', proowner))) a) s
                  where split_part(g, ':', 1) in (select r from roles)), ''), ''
  from fn
union all
select 'table', name, name, relkind::text || '|rls=' || relrowsecurity::text, '' from tbl
union all
select 'table_grant', name, name,
       coalesce((select string_agg(g, ',' order by g)
                   from (select (case when a.grantee = 0 then 'PUBLIC' else pg_get_userbyid(a.grantee) end) || ':' || a.privilege_type as g
                           from aclexplode(coalesce(relacl, acldefault('r', relowner))) a) s
                  where split_part(g, ':', 1) in (select r from roles)), ''), ''
  from tbl
union all
select 'column', t.name || '.' || a.attname, t.name,
       md5(format_type(a.atttypid, a.atttypmod) || '|' || a.attnotnull::text || '|' || coalesce(pg_get_expr(d.adbin, d.adrelid), '') || '|' || a.attgenerated::text || '|' || a.attidentity::text), ''
  from tbl t join pg_attribute a on a.attrelid = t.oid and a.attnum > 0 and not a.attisdropped
  left join pg_attrdef d on d.adrelid = a.attrelid and d.adnum = a.attnum
union all
select 'constraint', t.name || '.' || co.conname, t.name, md5(pg_get_constraintdef(co.oid)), ''
  from tbl t join pg_constraint co on co.conrelid = t.oid
union all
select 'index', t.name || '.' || i.relname, t.name, md5(pg_get_indexdef(i.oid)), ''
  from tbl t join pg_index x on x.indrelid = t.oid join pg_class i on i.oid = x.indexrelid
 where not exists (select 1 from pg_constraint co where co.conindid = i.oid)
union all
select 'policy', t.name || '.' || p.polname, t.name,
       md5(p.polcmd::text || '|' || p.polpermissive::text || '|' || coalesce(array_to_string(array(select rolname from pg_roles where oid = any(p.polroles) order by 1), ','), '') || '|' ||
           coalesce(pg_get_expr(p.polqual, p.polrelid), '') || '|' || coalesce(pg_get_expr(p.polwithcheck, p.polrelid), '')), ''
  from tbl t join pg_policy p on p.polrelid = t.oid
union all
select 'trigger', t.name || '.' || tg.tgname, t.name, md5(pg_get_triggerdef(tg.oid)), tg.tgenabled::text
  from tbl t join pg_trigger tg on tg.tgrelid = t.oid and not tg.tgisinternal
union all
select 'view', name, name, md5(pg_get_viewdef(oid)), '' from tbl where relkind in ('v', 'm')
union all
select 'cron', j.jobname, j.jobname, md5(j.schedule || '|' || regexp_replace(j.command, '\s+', ' ', 'g')), j.active::text
  from cron.job j
union all
select 'extension', e.extname, e.extname, n.nspname, '' from pg_extension e join pg_namespace n on n.oid = e.extnamespace
union all
select 'type', t.typname, t.typname, md5(coalesce((select string_agg(enumlabel, ',' order by enumsortorder) from pg_enum where enumtypid = t.oid), '')), ''
  from pg_type t join pg_namespace n on n.oid = t.typnamespace
 where n.nspname = 'public' and t.typtype = 'e' and t.oid not in (select objid from ext_objs)
union all
select 'storage_bucket', b.id, b.id, md5(b.public::text || '|' || coalesce(b.file_size_limit::text, '') || '|' || coalesce(array_to_string(b.allowed_mime_types, ','), '')), ''
  from storage.buckets b
union all
select 'storage_policy', p.polname, p.polname,
       md5(p.polcmd::text || '|' || coalesce(pg_get_expr(p.polqual, p.polrelid), '') || '|' || coalesce(pg_get_expr(p.polwithcheck, p.polrelid), '')), ''
  from pg_policy p where p.polrelid = 'storage.objects'::regclass
union all
select 'vault_name', s.name, s.name, '', '' from vault.secrets s
