-- 15_user_data_logs.sql
-- user_data 변경 diff 로그 테이블 및 트리거
-- 변경된 필드의 이전 값(OLD)만 저장하고, 역추적으로 특정 시점 상태를 재구성한다.

-- ── 테이블 ────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS public.user_data_logs (
  id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  account_id uuid   NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
  diff       jsonb  NOT NULL,   -- 변경된 필드의 OLD 값만 포함
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS user_data_logs_account_id_created_idx
  ON public.user_data_logs (account_id, created_at DESC);

ALTER TABLE public.user_data_logs ENABLE ROW LEVEL SECURITY;
-- 플레이어 직접 접근 없음 (서비스 롤 + 어드민 전용)

-- ── 트리거 함수 ────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION ts_log_user_data_diff()
RETURNS TRIGGER LANGUAGE plpgsql SECURITY DEFINER AS $$
DECLARE
  v_old  jsonb := to_jsonb(OLD);
  v_new  jsonb := to_jsonb(NEW);
  v_diff jsonb := '{}';
  v_key  text;
  -- 시스템 컬럼은 diff 제외
  v_skip text[] := ARRAY['id','account_id','user_id','server_id','created_at','updated_at'];
BEGIN
  FOR v_key IN SELECT jsonb_object_keys(v_new)
  LOOP
    IF v_key = ANY(v_skip) THEN CONTINUE; END IF;
    IF (v_old->v_key) IS DISTINCT FROM (v_new->v_key) THEN
      v_diff := v_diff || jsonb_build_object(v_key, v_old->v_key);
    END IF;
  END LOOP;

  IF v_diff != '{}'::jsonb THEN
    INSERT INTO public.user_data_logs (account_id, diff)
    VALUES (NEW.account_id, v_diff);
  END IF;
  RETURN NEW;
END;
$$;

-- ── 트리거 ────────────────────────────────────────────────────────────────
DROP TRIGGER IF EXISTS trg_user_data_log ON public.user_data;
CREATE TRIGGER trg_user_data_log
AFTER UPDATE ON public.user_data
FOR EACH ROW EXECUTE FUNCTION ts_log_user_data_diff();

-- ── 7일 초과 로그 자동 정리 (pg_cron 필요) ───────────────────────────────
SELECT cron.schedule(
  'cleanup-user-data-logs',
  '0 3 * * *',
  $$DELETE FROM public.user_data_logs WHERE created_at < now() - interval '7 days'$$
);
