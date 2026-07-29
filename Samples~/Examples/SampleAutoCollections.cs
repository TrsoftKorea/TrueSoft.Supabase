using System.Linq;
using Newtonsoft.Json;
using TrueBase.Core.Data;
using UnityEngine;

/// <summary>
/// 자동 확장 2D 컬렉션(<see cref="AutoList2D{T}"/> / <see cref="AutoDict2D{TKey1,TKey2,TValue}"/>) 사용 예제.
/// 네트워크·로그인·설정 불필요 — 빈 GameObject에 이 컴포넌트만 붙이고 Play 후 키를 누르면 Console에 결과가 찍힙니다.
///
/// 샘플을 여러 개 함께 쓰면 단축키가 겹칩니다. 그때는 <b>Tab</b> 으로 키를 받을 샘플을 고르세요.
/// 씬에 샘플이 하나뿐이면 그냥 눌러도 됩니다.
///
/// 키보드 단축키 (Play Mode):
///   1 — AutoList2D 데모   (스테이지 × 웨이브 최고점수)
///   2 — AutoDict2D 데모   (지역 × 몬스터 처치수)
///   3 — 저장 → 로드 왕복  (직렬화 + 로드 후 기본값 주입)
///   4 — 클래스 값 자동 생성·저장 (키마다 독립 인스턴스, 처음 읽은 순간 저장됨)
///   5 — 크기 지정          (EnsureSize/EnsureRows로 미리 확보 — 크기만 늘리면 기본값 슬롯이 실제로 채워짐)
///   6 — 폴백 병합          (로드 전 클라 초기값 + 서버값 병합 — 서버 우선, 빈/기본 슬롯만 채움. LoadAsync가 자동 수행)
///
/// 핵심: 읽기는 비파괴(없는 행/키는 기본값, 데이터를 만들지 않음), 쓰기만 그 시점에 생성·저장.
/// <c>grid[i]</c>는 List처럼(Add·Sort·FindAll·LINQ), <c>dict[k1]</c>는 Dictionary처럼 씁니다.
/// </summary>
public sealed class SampleAutoCollections : MonoBehaviour
{
    private const string Tag = "[Supabase.AutoCollections]";

    // [stage][wave] = 최고 점수, 미기록 = 0
    private readonly AutoList2D<int> _bestScore = new AutoList2D<int> { DefaultValue = 0 };
    // [region][monsterId] = 처치 수, 미기록 = 0
    private readonly AutoDict2D<string, int, int> _kills = new AutoDict2D<string, int, int> { DefaultValue = 0 };
    // [heroId] = 영웅 스탯(클래스 값). 없는 키를 읽으면 그 자리에 새 인스턴스를 저장하고 반환합니다 —
    // 서로 다른 키는 독립 인스턴스라 참조를 공유하지 않지만, 같은 키는 처음 읽은 그 객체를 계속 돌려줍니다.
    private readonly AutoDict<int, HeroStat> _heroStats = new AutoDict<int, HeroStat>();

    private void Awake()
    {
        // 실제 세이브에선 생성기가 CSV 기본값으로 [AutoDefault(1)]을 자동 생성해 이 호출을 대신합니다.
        // 여기서는 그 과정을 직접 재현합니다.
        ((IAutoDefaultable)_heroStats).SetDefaultValue(new object[] { 1 });
    }

    private sealed class HeroStat
    {
        public int level;
        public HeroStat(int level) => this.level = level;
    }

    private void Update()
    {
        // 여러 샘플을 한 씬에 놓으면 단축키가 겹친다. Tab 으로 고른 대상만 키를 읽는다.
        if (!SampleFocus.IsActive(this)) return;

        if (Input.GetKeyDown(KeyCode.Alpha1)) DemoList2D();
        if (Input.GetKeyDown(KeyCode.Alpha2)) DemoDict2D();
        if (Input.GetKeyDown(KeyCode.Alpha3)) DemoSaveLoad();
        if (Input.GetKeyDown(KeyCode.Alpha4)) DemoClassValueDefault();
        if (Input.GetKeyDown(KeyCode.Alpha5)) DemoEnsureSize();
        if (Input.GetKeyDown(KeyCode.Alpha6)) DemoFallbackMerge();
    }

    // 폴백 병합 데모용 최소 Row([DataColumn] 컬렉션 하나). 실제 세이브의 Row와 같은 구조입니다.
    private sealed class FallbackRow
    {
        [DataColumn("items")] public AutoList<int> items = new AutoList<int>();
    }

    /// <summary>1 — 2D 리스트: 읽기 비파괴, 쓰기 자동 확장, grid[i]를 List처럼.</summary>
    private void DemoList2D()
    {
        // 최고점 갱신(기존보다 높을 때만) — 읽기와 쓰기가 자연스럽게 섞입니다.
        void Record(int stage, int wave, int score)
        {
            if (score > _bestScore[stage][wave])   // 읽기: 없으면 0(비파괴, 데이터 안 만듦)
                _bestScore[stage][wave] = score;   // 쓰기: 그때 행·열 생성·저장
        }

        Record(0, 0, 100);
        Record(0, 1, 250);
        Record(0, 1, 200);   // 낮음 → 무시
        Record(5, 0, 999);   // 먼 스테이지(희소) — 사이 행은 자동 생성됨

        // 조회 — 읽기는 데이터를 만들지 않습니다.
        int rowsBefore = _bestScore.Count;
        int cleared = _bestScore[0].FindAll(s => s > 0).Count;   // 클리어한(점수>0) 웨이브 수
        int total   = _bestScore[0].Where(s => s > 0).Sum();      // LINQ
        int peek    = _bestScore[99][99];                          // 미기록 조회
        Debug.Log($"{Tag} 스테이지0 클리어={cleared}, 점수합={total}, 미기록[99][99]={peek}, " +
                  $"조회 후 행수={_bestScore.Count}(조회 전 {rowsBefore} → 읽기는 비파괴)");

        // grid[i]를 List처럼 다루기
        AutoRow<int> s0 = _bestScore[0];   // 프록시 타입을 이름으로 담아도 됨
        s0.Add(400);
        s0.Sort();
        Debug.Log($"{Tag} 스테이지0 정렬 = [{string.Join(", ", s0)}]");
    }

    /// <summary>2 — 2D 딕셔너리: dict[k1][k2] 안전 접근, Values 집계.</summary>
    private void DemoDict2D()
    {
        void Kill(string region, int monsterId) => _kills[region][monsterId] += 1;

        Kill("forest", 1); Kill("forest", 1); Kill("forest", 2);
        Kill("cave", 5);

        int forestTotal = _kills["forest"].Values.Sum();
        int desertPeek  = _kills["desert"][3];   // 미기록 조회 → 0, 키 안 생김
        Debug.Log($"{Tag} forest 총처치={forestTotal}, forest 몬스터1={_kills["forest"][1]}, " +
                  $"미기록 desert[3]={desertPeek}, desert 키생성됨={_kills.ContainsKey("desert")}");
    }

    /// <summary>3 — 저장 → 로드 왕복(직렬화). 로드 후 기본값 주입까지.</summary>
    private void DemoSaveLoad()
    {
        string scoreJson = JsonConvert.SerializeObject(_bestScore);
        string killsJson = JsonConvert.SerializeObject(_kills);
        Debug.Log($"{Tag} 저장 bestScore = {scoreJson}");
        Debug.Log($"{Tag} 저장 kills     = {killsJson}");

        var loadedScore = JsonConvert.DeserializeObject<AutoList2D<int>>(scoreJson);
        // 실제 세이브에선 SDK가 로드 후 [AutoDefault] 기본값을 자동 주입합니다(StaticUserSave 경로).
        // 여기서는 그 과정을 직접 재현합니다.
        ((IAutoDefaultable)loadedScore).SetDefaultValue(new object[] { 0 });
        Debug.Log($"{Tag} 로드 후 스테이지0[1]={loadedScore[0][1]}, 미기록[42][42]={loadedScore[42][42]}(기본값)");
    }

    /// <summary>4 — 클래스 값 자동 생성·저장: 처음 읽은 키는 그 자리에 저장되어 필드를 바로 고칠 수 있고,
    /// 서로 다른 키는 여전히 독립 인스턴스입니다.</summary>
    private void DemoClassValueDefault()
    {
        var a = _heroStats[100];   // 없는 키 → 새 HeroStat(level=1)을 그 자리에 저장하고 반환
        var b = _heroStats[200];   // 다른 키 → 별도의 새 HeroStat(level=1), a와 공유하지 않음
        Debug.Log($"{Tag} a==b(참조)? {ReferenceEquals(a, b)} (false여야 정상 — 서로 다른 키는 독립 인스턴스)");

        a.level = 99;   // 이미 저장된 객체를 직접 수정 — 별도 대입 없이 그대로 반영됨
        var aAgain = _heroStats[100];
        Debug.Log($"{Tag} a==aAgain(참조)? {ReferenceEquals(a, aAgain)}, aAgain.level={aAgain.level} " +
                  "(둘 다 true/99여야 정상 — 처음 읽을 때 저장된 그 객체를 다시 돌려줌)");

        var bAgain = _heroStats[200];
        Debug.Log($"{Tag} a를 고친 뒤 b.level={bAgain.level} (1이어야 정상 — a 수정이 b로 안 새야 함)");

        Debug.Log($"{Tag} ContainsKey(100)={_heroStats.ContainsKey(100)} — 조회만 해도 키가 존재하게 되므로, " +
                  "키 존재 여부를 '보유 여부' 같은 의미로 쓰면 안 됩니다.");
    }

    /// <summary>5 — 크기 지정: EnsureSize로 R×C를 기본값으로 확보. 크기만 늘린 슬롯도 실제 원소라 FindAll이 찾습니다.</summary>
    private void DemoEnsureSize()
    {
        var grid = new AutoList2D<int> { DefaultValue = 0 };
        grid.EnsureSize(3, 4);   // 3행 × 4열을 기본값 0으로 실제 채움
        grid[1][2] = 50;         // 한 칸만 실제 값

        // 크기만 늘린 슬롯도 실제로 저장된 원소 → FindAll이 기본값 슬롯까지 전부 찾습니다.
        int zeros   = grid[0].FindAll(v => v == 0).Count;   // 4 (전부 기본값 0)
        int nonZero = grid[1].FindAll(v => v != 0).Count;   // 1 ([1][2]=50만)
        Debug.Log($"{Tag} EnsureSize(3,4) → 행수={grid.Count}, 0행 열수={grid[0].Count}, " +
                  $"0행 기본값(0) 슬롯 수={zeros}, 1행 실제값 수={nonZero}");

        // 행만 확보(EnsureRows) + 특정 행만 열 확보(EnsureCount)
        grid.EnsureRows(5);       // 5행까지 확보
        grid[4].EnsureCount(2);   // 4행만 2열 확보
        Debug.Log($"{Tag} EnsureRows(5)+[4].EnsureCount(2) → 행수={grid.Count}, 4행 열수={grid[4].Count}");
    }

    /// <summary>6 — 로드 전 폴백 병합: 서버 우선, 서버가 비었거나 기본값인 슬롯만 클라이언트 초기값으로 채웁니다.
    /// 실제 세이브에선 <c>LoadAsync</c>가 jsonb 컬럼이 SQL NULL이거나 기본값일 때 이 병합을 자동 수행합니다.</summary>
    private void DemoFallbackMerge()
    {
        // 로그인 후·로드 전에 클라이언트가 세팅해 둔 초기값(fallback).
        var fallback = new FallbackRow();
        fallback.items.EnsureCount(3);
        fallback.items[0] = 10; fallback.items[1] = 20; fallback.items[2] = 30;

        // 서버에서 온 값 — [0]만 실제값, [1][2]는 기본값(0).
        var server = new FallbackRow();
        server.items.EnsureCount(3);
        server.items[0] = 999;

        var merged = new FallbackRow();
        DataSchema.MergeServerOverFallback(merged, server, fallback);
        Debug.Log($"{Tag} 병합(서버+fallback) items = [{string.Join(", ", merged.items)}] " +
                  "(예상 [999, 20, 30] — 서버 실제값 999 유지, 기본값 0 슬롯만 fallback으로 채움)");

        // 서버 컬럼이 통째로 NULL(SQL NULL)이면 fallback이 전부 적용됩니다.
        var mergedFromNull = new FallbackRow();
        DataSchema.MergeServerOverFallback(mergedFromNull, new FallbackRow { items = null }, fallback);
        Debug.Log($"{Tag} 서버 NULL → items = [{string.Join(", ", mergedFromNull.items)}] (예상 [10, 20, 30] — fallback 전체 적용)");
    }
}
