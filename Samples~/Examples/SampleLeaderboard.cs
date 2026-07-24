using System.Collections.Generic;
using System.Threading.Tasks;
using TrueBase.Core.Data;
using TrueBase.Unity;
using UnityEngine;

/// <summary>
/// 리더보드 예제 컴포넌트. SupabaseRuntime이 씬에 있어야 합니다.
///
/// 리더보드 생성은 운영(Retool) 전용입니다 — Retool의 리더보드 페이지에서 먼저 리더보드를 만들고,
/// Inspector의 Leaderboard Code를 그 코드와 맞춘 뒤 Play Mode에서 키를 눌러 확인하세요.
/// 순위에 함께 표시할 값을 쓰려면 Retool 리더보드 &gt; 필드 탭에서 필드를 등록하고
/// Demo Field Name 도 그 필드명으로 맞춰야 합니다.
///
/// 키보드 단축키 (Play Mode):
///   1 — 익명 로그인
///   2 — 리더보드 목록 조회
///   3 — 리더보드 상세(회차·남은 시간·참여자 수)
///   4 — 랜덤 점수 기록
///   5 — 상위 10위 조회
///   6 — 내 순위 조회
///   7 — 지난 회차 상위 10위 조회
///   8 — 추가 데이터 수정
///   9 — 내 기록 삭제
/// </summary>
public sealed class SampleLeaderboard : MonoBehaviour
{
    private const string Tag = "[Supabase.Leaderboard]";

    [SerializeField] private string leaderboardCode = "arena";

    [Tooltip("Retool 리더보드 > 필드 탭에 등록한 필드명. 비우면 필드 없이 기록합니다.")]
    [SerializeField] private string demoColumnName = "";

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) _ = SignInAsync();
        if (Input.GetKeyDown(KeyCode.Alpha2)) _ = ListTablesAsync();
        if (Input.GetKeyDown(KeyCode.Alpha3)) _ = ShowTableAsync();
        if (Input.GetKeyDown(KeyCode.Alpha4)) _ = SubmitAsync();
        if (Input.GetKeyDown(KeyCode.Alpha5)) _ = ShowRangeAsync(null);
        if (Input.GetKeyDown(KeyCode.Alpha6)) _ = ShowMyRankAsync();
        if (Input.GetKeyDown(KeyCode.Alpha7)) _ = ShowPreviousRotationAsync();
        if (Input.GetKeyDown(KeyCode.Alpha8)) _ = EditMyDataAsync();
        if (Input.GetKeyDown(KeyCode.Alpha9)) _ = DeleteMyScoreAsync();
    }

    /// <summary>1 — 익명 로그인.</summary>
    private async Task SignInAsync()
    {
        var ok = await Supabase.SignInAnonymouslyAsync();
        if (!ok) { Debug.LogWarning($"{Tag} 로그인 실패: {ok.ErrorCode}"); return; }

        Debug.Log($"{Tag} 로그인됨. UserId = {Supabase.UserId}");
    }

    /// <summary>2 — 사용 가능한 리더보드 목록. 운영이 추가하면 빌드 없이 늘어납니다.</summary>
    private async Task ListTablesAsync()
    {
        var r = await Supabase.GetLeaderboardTablesAsync();
        if (!r.IsSuccess) { Debug.LogWarning($"{Tag} 목록 조회 실패: {r.ErrorCode}"); return; }

        Debug.Log($"{Tag} 리더보드 {r.Data.Count}개");
        foreach (var t in r.Data)
            Debug.Log($"{Tag}  {t.Code} — {t.DisplayName} / {t.RecordType} / {t.SortType} / 회차 {t.RotationCount}");
    }

    /// <summary>3 — 리더보드 상세. 남은 시간·참여자 수·등록 필드를 확인합니다.</summary>
    private async Task ShowTableAsync()
    {
        var r = await Supabase.GetLeaderboardTableAsync(leaderboardCode);
        if (!r.IsSuccess) { Debug.LogWarning($"{Tag} 상세 조회 실패: {r.ErrorCode}"); return; }

        var t = r.Data;
        var left = t.RotationTimeLeft.HasValue ? $"{t.RotationTimeLeft.Value}초 남음" : "주기 없음";
        var cols = t.Columns == null || t.Columns.Count == 0 ? "(없음)" : string.Join(", ", t.Columns);
        Debug.Log($"{Tag} {t.DisplayName} — 회차 {t.RotationCount}, {left}, 참여 {t.TotalIds}명, 종료={t.IsEnded}");
        Debug.Log($"{Tag}  등록 필드: {cols}");
    }

    /// <summary>4 — 랜덤 점수를 기록합니다. 최고·최저·최신·누적 중 어떻게 반영될지는 서버 설정을 따릅니다.</summary>
    private async Task SubmitAsync()
    {
        var score = Random.Range(1, 10000);

        IReadOnlyDictionary<string, object> data = null;
        if (!string.IsNullOrWhiteSpace(demoColumnName))
            data = new Dictionary<string, object> { [demoColumnName.Trim()] = Random.Range(1, 100) };

        var r = await Supabase.SubmitLeaderboardScoreAsync(leaderboardCode, score, $"제출 {score}", data);
        if (!r.IsSuccess) { Debug.LogWarning($"{Tag} 기록 실패: {r.ErrorCode}"); return; }

        Debug.Log($"{Tag} {score} 제출 → 반영된 점수 {r.Data.Score} (회차 {r.Data.RotationCount})");
    }

    /// <summary>5·7 — 상위 10위 조회. rotationCount가 null이면 현재 회차.</summary>
    private async Task ShowRangeAsync(int? rotationCount)
    {
        var r = await Supabase.GetLeaderboardRangeAsync(leaderboardCode, 1, 10, rotationCount);
        if (!r.IsSuccess) { Debug.LogWarning($"{Tag} 순위 조회 실패: {r.ErrorCode}"); return; }

        var label = rotationCount.HasValue ? $"{rotationCount.Value}회차" : "현재 회차";
        Debug.Log($"{Tag} {label} 상위 {r.Data.Count}명");
        foreach (var e in r.Data)
        {
            var extra = e.Data != null && e.Data.Count > 0 ? $" {FormatData(e.Data)}" : "";
            Debug.Log($"{Tag}  {e.Rank}위 {e.DisplayName ?? "(이름 없음)"} — {e.Score}{extra}");
        }
    }

    /// <summary>6 — 내 순위. 기록이 없어도 실패가 아니라 Registered=false로 옵니다.</summary>
    private async Task ShowMyRankAsync()
    {
        var r = await Supabase.GetLeaderboardPlayerAsync(leaderboardCode);
        if (!r.IsSuccess) { Debug.LogWarning($"{Tag} 내 순위 조회 실패: {r.ErrorCode}"); return; }

        if (!r.Data.Registered)
        {
            Debug.Log($"{Tag} 아직 기록이 없습니다. 4번으로 점수를 먼저 올리세요.");
            return;
        }

        Debug.Log($"{Tag} 내 순위 {r.Data.Rank}위 — {r.Data.Score}점 (회차 {r.Data.RotationCount})");
    }

    /// <summary>7 — 지난 회차 조회. 회차가 1이면 지난 회차가 없습니다.</summary>
    private async Task ShowPreviousRotationAsync()
    {
        var table = await Supabase.GetLeaderboardTableAsync(leaderboardCode);
        if (!table.IsSuccess) { Debug.LogWarning($"{Tag} 상세 조회 실패: {table.ErrorCode}"); return; }

        var prev = table.Data.RotationCount - 1;
        if (prev < 1)
        {
            Debug.Log($"{Tag} 아직 지난 회차가 없습니다(현재 1회차).");
            return;
        }

        await ShowRangeAsync(prev);
    }

    /// <summary>8 — 추가 데이터만 수정합니다. 점수는 바뀌지 않습니다.</summary>
    private async Task EditMyDataAsync()
    {
        IReadOnlyDictionary<string, object> data = null;
        if (!string.IsNullOrWhiteSpace(demoColumnName))
            data = new Dictionary<string, object> { [demoColumnName.Trim()] = Random.Range(1, 100) };

        var r = await Supabase.SetLeaderboardPlayerDataAsync(leaderboardCode, $"수정 {Time.frameCount}", data);
        if (!r.IsSuccess) { Debug.LogWarning($"{Tag} 데이터 수정 실패: {r.ErrorCode}"); return; }

        Debug.Log($"{Tag} 추가 데이터를 수정했습니다. 5번으로 확인하세요.");
    }

    /// <summary>9 — 내 기록 삭제.</summary>
    private async Task DeleteMyScoreAsync()
    {
        var r = await Supabase.DeleteMyLeaderboardScoreAsync(leaderboardCode);
        if (!r.IsSuccess) { Debug.LogWarning($"{Tag} 삭제 실패: {r.ErrorCode}"); return; }

        Debug.Log($"{Tag} 내 기록을 삭제했습니다.");
    }

    private static string FormatData(IReadOnlyDictionary<string, object> data)
    {
        var parts = new List<string>();
        foreach (var kv in data)
            parts.Add($"{kv.Key}={kv.Value}");
        return "[" + string.Join(", ", parts) + "]";
    }
}
