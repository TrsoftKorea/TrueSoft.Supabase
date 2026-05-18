-- =============================================================================
-- 탈퇴 취소 RPC — ts_withdrawal_cancel_redeem
-- 선행: 02_profiles.sql (user_profiles.withdrawn_at)
--
-- withdrawal-cancel-redeem Edge Function이 사용자 JWT로 이 RPC를 호출합니다.
-- Edge Function이 cancel_token을 검증한 뒤 이 RPC를 호출하므로
-- RPC 자체는 auth.uid() 기반으로 탈퇴 예약만 취소합니다.
-- =============================================================================

create or replace function public.ts_withdrawal_cancel_redeem()
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_uid          uuid;
  v_withdrawn_at timestamptz;
begin
  v_uid := auth.uid();
  if v_uid is null then
    return jsonb_build_object('ok', false, 'reason', 'not_authenticated');
  end if;

  select withdrawn_at into v_withdrawn_at
  from public.user_profiles
  where account_id = v_uid;

  if not found then
    return jsonb_build_object('ok', false, 'reason', 'profile_not_found');
  end if;

  -- 탈퇴 예약이 없거나 이미 만료된 경우
  if v_withdrawn_at is null or v_withdrawn_at <= now() then
    return jsonb_build_object('ok', false, 'reason', 'withdrawal_not_scheduled');
  end if;

  -- 탈퇴 예약 취소
  update public.user_profiles
  set withdrawn_at = null
  where account_id = v_uid;

  return jsonb_build_object('ok', true);
end;
$$;

comment on function public.ts_withdrawal_cancel_redeem() is
  '탈퇴 취소 RPC. withdrawal-cancel-redeem Edge Function에서 사용자 JWT로 호출. withdrawn_at을 초기화합니다.';

grant execute on function public.ts_withdrawal_cancel_redeem() to authenticated;
