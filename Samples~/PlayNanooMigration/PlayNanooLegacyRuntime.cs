// =============================================================================
// PlayNANOO → Supabase SDK 이관 런타임 — 구버전 (AccountGuestSignIn / AccountManager.*)
//
// [사용법]
// 씬에서 SupabaseRuntime 대신 이 컴포넌트를 배치합니다.
// PlayNANOO SDK 신버전(AccountManagerV20240401) 사용 시
// PlayNanooRuntime을 배치하세요.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PlayNANOO;

/// <summary>
/// PlayNANOO SDK 구버전(AccountGuestSignIn / AccountManager.*) 구현체.
/// SupabaseRuntime 대신 씬에 하나만 배치합니다.
/// </summary>
public class PlayNanooLegacyRuntime : PlayNanooRuntimeBase
{
    protected override void NanooGuestSignIn(Func<string, Dictionary<string, object>, Task> cb)
        => _plugin.AccountGuestSignIn(async (s, _, _, v) => await cb(s, v));

    protected override void NanooSocialSignIn(string token, string accountType, Func<string, Dictionary<string, object>, Task> cb)
        => _plugin.AccountSocialSignIn(token, accountType, async (s, _, _, v) => await cb(s, v));

    protected override void NanooTokenSignOut(string accessToken, Func<Task> cb)
        => _plugin.AccountTokenSignOut(accessToken, async (_, _, _, _) => await cb());

    protected override void NanooWithDrawal(int holdDays, Func<string, Task> cb)
        => _plugin.AccountManager.WithDrawal(holdDays, async (s, _, _, _) => await cb(s));

    protected override void NanooWithDrawalRestore(string key, Func<string, Task> cb)
        => _plugin.AccountManager.WithDrawalRestore(key, async (s, _, _, _) => await cb(s));
}
