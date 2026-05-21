using System;
using Truesoft.Supabase.Unity;

/// <summary>
/// StaticUserSave 상속 최소 예시.
/// level·coins 컬럼을 읽고 씁니다.
///
/// 사용 예:
/// <code>
/// SamplePlayerSave.Level = 10;     // 변경 → MarkDirty 자동 호출
/// SamplePlayerSave.Coins += 100;
/// await Supabase.TrySaveAllAsync();  // 즉시 저장
/// </code>
///
/// 평상시 저장은 SupabaseRuntime이 쿨타임 주기로 자동 처리합니다.
/// 새 컬럼 추가 시 Row 클래스에 [DataColumn] 필드만 추가하면 됩니다.
/// </summary>
public sealed partial class SamplePlayerSave : StaticUserSave<SamplePlayerSave.Row>
{
    public static readonly SamplePlayerSave Instance = new();
    private SamplePlayerSave() : base() { }

    public static System.Threading.Tasks.Task<bool> TryLoadAsync() =>
        ((StaticUserSave<Row>)Instance).TryLoadAsync();

    [Serializable]
    public sealed class Row
    {
        [DataColumn("level")] public int level;
        [DataColumn("coins")] public int coins;
    }

    public static int Level
    {
        get => Instance.Current.level;
        set { if (Instance.Current.level == value) return; Instance.Current.level = value; Instance.MarkDirty(); }
    }

    public static int Coins
    {
        get => Instance.Current.coins;
        set { if (Instance.Current.coins == value) return; Instance.Current.coins = value; Instance.MarkDirty(); }
    }
}
