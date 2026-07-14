using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TrueBase.Core.Common;
using TrueBase.Core.Data;
using UnityEngine;
using SupabaseClient = global::TrueBase.Unity.Supabase;

/// <summary>
/// 우편함 분류(category) 예제 컴포넌트. SupabaseRuntime이 씬에 있어야 합니다.
///
/// 우편 발송은 service_role 전용이라 클라이언트에서 불가능합니다 — 키 1로 내 account_id/user_id를
/// 확인한 뒤, Retool 어드민(또는 Supabase SQL Editor)에서 그 계정으로 테스트 메일을 발송하고
/// 키 2~8로 조회·수령·삭제를 확인하세요. Inspector의 Demo Category·Demo Item Key를 발송한
/// 값과 맞춰야 분류 필터·수령 데모가 의미 있게 동작합니다.
///
/// 키보드 단축키 (Play Mode):
///   1 — 익명 로그인 + account_id/user_id 출력(발송 대상 확인)
///   2 — 전체 목록 조회        3 — 분류 필터 조회(Demo Category)
///   4 — 분류별 현황(ByCategory 덤프)
///   5 — 첫 미수령 메일 보상 수령
///   6 — 분류 일괄 수령(Demo Category)
///   7 — 읽은 우편 일괄 삭제(Demo Category)
///   8 — 첫 삭제 가능 메일 삭제
/// </summary>
public sealed class SampleMailbox : MonoBehaviour
{
    private const string Tag = "[Supabase.Mailbox]";

    [SerializeField] private string demoCategory = "event";
    [SerializeField] private string demoItemKey = "gold";

    // 마지막 조회 결과 — 수령/삭제 키가 이 중 첫 대상에 대해 동작합니다.
    private IReadOnlyList<Mail> _lastList;

    private void Awake()
    {
        // 보상 지급 핸들러 등록(로그만 남기고 성공 반환) — 실제 게임은 인벤토리·재화 지급.
        MailItemHandlerRegistry.Register(new LoggingMailItemHandler(demoItemKey));
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) _ = SignInAndPrintIdAsync();
        if (Input.GetKeyDown(KeyCode.Alpha2)) _ = ListAsync(null);
        if (Input.GetKeyDown(KeyCode.Alpha3)) _ = ListAsync(demoCategory);
        if (Input.GetKeyDown(KeyCode.Alpha4)) _ = DumpCountsAsync();
        if (Input.GetKeyDown(KeyCode.Alpha5)) _ = ClaimFirstUnclaimedAsync();
        if (Input.GetKeyDown(KeyCode.Alpha6)) _ = ClaimAllInCategoryAsync();
        if (Input.GetKeyDown(KeyCode.Alpha7)) _ = DeleteReadInCategoryAsync();
        if (Input.GetKeyDown(KeyCode.Alpha8)) _ = DeleteFirstDeletableAsync();
    }

    /// <summary>1 — 익명 로그인 후 발송 대상 식별자를 출력합니다.</summary>
    private async Task SignInAndPrintIdAsync()
    {
        if (!SupabaseClient.IsLoggedIn)
        {
            var ok = await SupabaseClient.SignInAnonymouslyAsync();
            if (!ok) { Debug.LogWarning($"{Tag} 로그인 실패: {ok.ErrorCode}"); return; }
        }

        Debug.Log($"{Tag} 로그인됨. 이 account_id로 테스트 메일을 발송하세요(Retool/SQL Editor).\n" +
                  $"  UserId = {SupabaseClient.UserId}\n" +
                  $"  발송 시 category='{demoCategory}', items[].key='{demoItemKey}'로 맞추면 데모가 동작합니다.");
    }

    /// <summary>2·3 — 목록 조회. category=null이면 전체.</summary>
    private async Task ListAsync(string category)
    {
        var r = await SupabaseClient.GetMyMailsAsync(category: category);
        if (!r.IsSuccess) { Debug.LogWarning($"{Tag} 목록 조회 실패: {r.ErrorCode}"); return; }

        _lastList = r.Data;
        var label = string.IsNullOrEmpty(category) ? "전체" : $"분류 '{category}'";
        Debug.Log($"{Tag} {label} {_lastList.Count}건");
        foreach (var m in _lastList)
            Debug.Log($"{Tag}  [{m.Category}] {m.Title} — 읽음={m.IsRead}, 미수령보상={m.HasUnclaimedItems}");
    }

    /// <summary>4 — 미읽음·미수령 전체 집계 + 분류별 세부.</summary>
    private async Task DumpCountsAsync()
    {
        var r = await SupabaseClient.GetMailInboxCountsAsync();
        if (!r.IsSuccess) { Debug.LogWarning($"{Tag} 현황 조회 실패: {r.ErrorCode}"); return; }

        var c = r.Data;
        Debug.Log($"{Tag} 전체 — 미읽음 {c.Unread}, 미수령 {c.UnclaimedMails}");
        foreach (var kv in c.ByCategory)
            Debug.Log($"{Tag}  분류 '{kv.Key}' — 미읽음 {kv.Value.Unread}, 미수령 {kv.Value.UnclaimedMails}");
    }

    /// <summary>5 — 마지막 조회 목록에서 미수령 보상이 있는 첫 메일을 수령합니다.</summary>
    private async Task ClaimFirstUnclaimedAsync()
    {
        var target = _lastList?.FirstOrDefault(m => m.HasUnclaimedItems);
        if (target == null) { Debug.Log($"{Tag} 수령할 메일이 없습니다. 먼저 2·3으로 목록을 조회하세요."); return; }

        var r = await SupabaseClient.ClaimMailItemsAsync(target.Id);
        if (!r.IsSuccess) { Debug.LogWarning($"{Tag} 수령 실패: {r.ErrorCode}"); return; }

        Debug.Log($"{Tag} 수령 완료 — {string.Join(", ", r.Data.Select(x => $"{x.ItemKey}x{x.Count}"))}");
    }

    /// <summary>6 — Demo Category 분류의 보상을 일괄 수령합니다.</summary>
    private async Task ClaimAllInCategoryAsync()
    {
        var r = await SupabaseClient.ClaimAllMailItemsAsync(demoCategory);
        if (!r.IsSuccess) { Debug.LogWarning($"{Tag} 일괄 수령 실패: {r.ErrorCode}"); return; }

        Debug.Log($"{Tag} 분류 '{demoCategory}' 일괄 수령 — 총 {r.Data.Count}건 지급");
    }

    /// <summary>7 — Demo Category 분류의 읽은 우편을 일괄 삭제합니다.</summary>
    private async Task DeleteReadInCategoryAsync()
    {
        var r = await SupabaseClient.DeleteReadMailsAsync(demoCategory);
        if (!r.IsSuccess) { Debug.LogWarning($"{Tag} 일괄 삭제 실패: {r.ErrorCode}"); return; }

        Debug.Log($"{Tag} 분류 '{demoCategory}' 읽은 우편 {r.Data}건 삭제");
    }

    /// <summary>8 — 마지막 조회 목록에서 삭제 가능한 첫 메일을 삭제합니다.</summary>
    private async Task DeleteFirstDeletableAsync()
    {
        var target = _lastList?.FirstOrDefault(m => m.CanDeleteFromInbox);
        if (target == null) { Debug.Log($"{Tag} 삭제할 메일이 없습니다. 먼저 2·3으로 목록을 조회하세요."); return; }

        var r = await SupabaseClient.DeleteMailAsync(target.Id);
        if (!r.IsSuccess) { Debug.LogWarning($"{Tag} 삭제 실패: {r.ErrorCode}"); return; }

        Debug.Log($"{Tag} 삭제 완료 — {target.Title}");
    }

    /// <summary>보상을 로그로만 지급하는 데모 핸들러.</summary>
    private sealed class LoggingMailItemHandler : IMailItemHandler
    {
        public LoggingMailItemHandler(string itemKey) => ItemKey = itemKey;

        public string ItemKey { get; }

        public Task<SupabaseResult<ClaimResult>> HandleAsync(string mailId, int itemIndex, string itemKey, int count)
        {
            Debug.Log($"{Tag} 지급(데모): {itemKey} x{count}");
            return Task.FromResult(SupabaseResult<ClaimResult>.Success(new ClaimResult
            {
                MailId = mailId, ItemIndex = itemIndex, ItemKey = itemKey, Count = count
            }));
        }
    }
}
