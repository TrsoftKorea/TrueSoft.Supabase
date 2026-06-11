// =============================================================================
// PlayNANOO → Supabase SDK 이관 런타임 — 신버전 (AccountManagerV20240401)
//
// [사용법]
// 씬에서 SupabaseRuntime 대신 이 컴포넌트를 배치합니다.
// PlayNANOO SDK 구버전(AccountGuestSignIn / AccountManager.*) 사용 시
// PlayNanooLegacyRuntime을 배치하세요.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PlayNANOO;

/// <summary>
/// PlayNANOO SDK 신버전(AccountManagerV20240401) 구현체.
/// SupabaseRuntime 대신 씬에 하나만 배치합니다.
/// </summary>
public class PlayNanooRuntime : PlayNanooRuntimeBase
{
    protected override void NanooGuestSignIn(Func<string, Dictionary<string, object>, Task> cb)
        => _plugin.AccountManagerV20240401.GuestSignIn(async (s, _, _, v) => await cb(s, v));

    protected override void NanooSocialSignIn(string token, string accountType, Func<string, Dictionary<string, object>, Task> cb)
        => _plugin.AccountManagerV20240401.SocialSignIn(token, accountType, async (s, _, _, v) => await cb(s, v));

    protected override void NanooTokenSignOut(string accessToken, Func<Task> cb)
        => _plugin.AccountManagerV20240401.TokenSignOut(accessToken, async (_, _, _, _) => await cb());

    protected override void NanooWithDrawal(int holdDays, Func<string, Task> cb)
        => _plugin.AccountManagerV20240401.WithDrawal(holdDays, async (s, _, _, _) => await cb(s));

    protected override void NanooWithDrawalRestore(string key, Func<string, Task> cb)
        => _plugin.AccountManagerV20240401.WithDrawalRestore(key, async (s, _, _, _) => await cb(s));

    protected override void NanooSetNickname(string nickname, Func<string, Task> cb)
        => _plugin.AccountManagerV20240401.NicknamePut(nickname, true, async (s, _, _, _) => await cb(s));
}
