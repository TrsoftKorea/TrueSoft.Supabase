# 리더보드 클래스와 필드

리더보드는 코드 문자열이 아니라 **생성한 타입**으로 지정합니다. 순위에 닉네임·점수 말고 레벨이나 길드명 같은 값을 함께 보여주고 싶다면, 그 값들도 이 클래스 안의 `Row`로 다룹니다.

## 클래스 생성 {#generate}

메뉴에서 **TrueSoft > Supabase > 클래스 생성 > 리더보드**를 열고 드롭다운에서 리더보드를 고른 뒤 **필드 목록 가져오기 → 소스 생성 → 저장**을 누릅니다. 코드나 필드 이름을 직접 입력할 필요가 없습니다.

필드를 등록하지 않은 리더보드도 클래스를 만듭니다. 점수만 기록하는 리더보드라도 대상을 가리킬 타입이 필요하기 때문입니다.

```csharp
// 필드가 없는 리더보드
[Leaderboard("arena")]
public sealed partial class ArenaLeaderboard : ILeaderboard { }
```

```csharp
// 필드가 있는 리더보드
[Leaderboard("guild")]
public sealed partial class GuildLeaderboard : ILeaderboard
{
    public sealed partial class Row
    {
        [DataColumn("guild_name")] public string GuildName;
        [DataColumn("char_level")] public int    CharacterLevel;
    }
}
```

생성 파일에는 **메서드가 없습니다.** 속성과 필드 선언뿐이라 SDK가 바뀌어도 깨지지 않고, **DB 컬럼이 바뀔 때만** 다시 생성하면 됩니다. 조작은 전부 `Supabase` 파사드에 있습니다.

::: info partial 이라 덧붙일 수 있습니다
생성 파일은 건드리지 말고 별도 파일에 같은 이름의 `partial` 클래스를 두어 헬퍼를 추가하세요. 재생성해도 사라지지 않습니다.
:::

## 필드는 리더보드마다 따로 정합니다 {#per-leaderboard}

운영자가 Retool의 **리더보드 > 필드** 탭에서 리더보드마다 사용할 필드를 등록합니다. 등록하지 않은 필드를 보내면 기록이 거부되므로, 리더보드마다 필요한 값만 주고받습니다.

리더보드마다 별도 클래스가 생성되므로 서로의 필드가 섞이지 않습니다.

## 값 보내기

`Row`를 만들어 점수와 함께 넘깁니다. 어느 리더보드인지는 행 타입에서 읽습니다.

```csharp
await Supabase.SubmitScoreAsync(1250,
    new GuildLeaderboard.Row { CharacterLevel = 42, GuildName = "붉은검" });
```

점수를 바꾸지 않고 값만 고치려면 [추가 데이터 수정](./set-player-data)을 사용합니다.

## 값 읽기 {#to-row}

```csharp
TRow Supabase.ToRow<TRow>(LeaderboardEntry entry)
TRow Supabase.ToRow<TRow>(LeaderboardPlayerEntry entry)
```

순위·플레이어 조회 결과의 `.Data`를 생성 타입의 행으로 되돌립니다. 네트워크 호출이 없습니다.

```csharp
var result = await Supabase.GetRanksAsync<GuildLeaderboard>(1, 50);
foreach (var entry in result.Data)
{
    var row = Supabase.ToRow<GuildLeaderboard.Row>(entry);
    AddRankRow(entry.Rank, entry.DisplayName, entry.Score, row.GuildName);
}
```

::: warning 필드 추가는 운영자만
필드를 새로 만들거나 지우는 것은 Retool에서만 가능합니다. 게임에서는 이미 등록된 필드에 값을 넣고 읽을 수만 있습니다. 필드가 바뀌면 클래스를 다시 생성하세요.
:::
