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
    // 구버전 스토리지 키: $"ServerData_{UserID}" — 로그인 시 획득
    private string _nanooUserId;

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

    protected override void NanooSetNickname(string nickname, Func<string, Task> cb)
        => _plugin.AccountNickanmePut(nickname, true, async (s, _, _, _) => await cb(s));

    // 로그인 성공 시 UserID를 저장합니다 (스토리지 키에 사용).
    protected override void OnNanooLoginSuccess(Dictionary<string, object> values)
        => _nanooUserId = values["uuid"]?.ToString();

    // ── 구버전 스토리지: 키 = "ServerData_{UserID}", isPrivate = false ──────────

    protected override Task<string> LoadRawFromNanoo()
    {
        var tcs = new TaskCompletionSource<string>();
        var key = $"ServerData_{_nanooUserId}";
        _plugin.Storage.Load(key, (status, _, _, values) =>
        {
            if (status != Configure.PN_API_STATE_SUCCESS) { tcs.SetResult(null); return; }
            tcs.SetResult(values["StorageValue"]?.ToString());
        });
        return tcs.Task;
    }

    public override void SaveToNanoo(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        var key = $"ServerData_{_nanooUserId}";
        _plugin.Storage.Save(key, json, false,
            (status, _, _, _) =>
            {
                if (status != Configure.PN_API_STATE_SUCCESS)
                    UnityEngine.Debug.LogWarning("[PlayNanooRuntime] PlayNANOO 저장 실패");
            });
    }
}
