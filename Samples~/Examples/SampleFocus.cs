using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 여러 샘플을 한 씬에 놓았을 때 단축키가 겹치지 않게 합니다.
/// 대부분의 샘플이 1·2·3… 을 쓰므로, 그대로 두면 키 하나에 여러 동작이 동시에 실행됩니다.
///
/// 각 샘플은 <see cref="IsActive"/>가 true일 때만 키를 읽습니다.
/// 씬에 샘플이 하나뿐이면 항상 활성이라 아무것도 누를 필요가 없습니다.
/// 둘 이상이면 <b>Tab</b> 으로 대상을 넘깁니다.
///
/// 등록은 자동입니다 — 샘플이 <see cref="IsActive"/>를 부르는 순간 목록에 들어갑니다.
/// </summary>
public static class SampleFocus
{
    private static readonly List<MonoBehaviour> Samples = new List<MonoBehaviour>();
    private static int _index;
    private static int _pumpedFrame = -1;

    /// <summary>지금 키 입력을 받는 샘플. 없으면 null.</summary>
    public static MonoBehaviour Current =>
        _index >= 0 && _index < Samples.Count ? Samples[_index] : null;

    /// <summary>
    /// 이 샘플이 지금 키 입력을 받아야 하는지. 샘플 <c>Update</c> 맨 위에서 호출하세요.
    /// </summary>
    public static bool IsActive(MonoBehaviour sample)
    {
        if (sample == null)
            return false;

        Register(sample);
        Pump();

        // 하나뿐이면 전환할 이유가 없다.
        if (Samples.Count <= 1)
            return true;

        return ReferenceEquals(Current, sample);
    }

    private static void Register(MonoBehaviour sample)
    {
        if (Samples.Contains(sample))
            return;

        Samples.Add(sample);
        // 씬 배치 순서는 들쭉날쭉하므로 이름순으로 고정해 Tab 순서를 예측 가능하게 둔다.
        Samples.Sort((a, b) => string.CompareOrdinal(a.GetType().Name, b.GetType().Name));

        if (Samples.Count == 2)
            Debug.Log($"[SampleFocus] 샘플이 여러 개입니다. Tab 으로 대상을 바꾸세요. 현재 대상: {Name(Current)}");
    }

    /// <summary>한 프레임에 한 번만 입력을 처리합니다. 샘플마다 IsActive 를 부르기 때문입니다.</summary>
    private static void Pump()
    {
        if (_pumpedFrame == Time.frameCount)
            return;

        _pumpedFrame = Time.frameCount;

        // 파괴된 샘플 정리. Unity 의 null 비교가 파괴 여부까지 잡아 준다.
        Samples.RemoveAll(s => s == null);
        if (Samples.Count == 0)
        {
            _index = 0;
            return;
        }

        if (_index >= Samples.Count)
            _index = 0;

        if (Samples.Count > 1 && Input.GetKeyDown(KeyCode.Tab))
        {
            _index = (_index + 1) % Samples.Count;
            Debug.Log($"[SampleFocus] 대상: {Name(Current)}  ({_index + 1}/{Samples.Count})");
        }
    }

    private static string Name(MonoBehaviour s) => s == null ? "(없음)" : s.GetType().Name;
}
