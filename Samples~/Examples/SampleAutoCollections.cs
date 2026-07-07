using System.Linq;
using Newtonsoft.Json;
using TrueBase.Core.Data;
using UnityEngine;

/// <summary>
/// 자동 확장 2D 컬렉션(<see cref="AutoList2D{T}"/> / <see cref="AutoDict2D{TKey1,TKey2,TValue}"/>) 사용 예제.
/// 네트워크·로그인·설정 불필요 — 빈 GameObject에 이 컴포넌트만 붙이고 Play 후 키를 누르면 Console에 결과가 찍힙니다.
///
/// 키보드 단축키 (Play Mode):
///   1 — AutoList2D 데모   (스테이지 × 웨이브 최고점수)
///   2 — AutoDict2D 데모   (지역 × 몬스터 처치수)
///   3 — 저장 → 로드 왕복  (직렬화 + 로드 후 기본값 주입)
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

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) DemoList2D();
        if (Input.GetKeyDown(KeyCode.Alpha2)) DemoDict2D();
        if (Input.GetKeyDown(KeyCode.Alpha3)) DemoSaveLoad();
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
}
