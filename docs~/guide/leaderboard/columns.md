# 플레이어 데이터 필드

순위에 닉네임·점수 말고 레벨이나 길드명 같은 값을 함께 보여주고 싶을 때 씁니다. 유저 데이터처럼 **필드를 지정**해 두고, 점수를 기록할 때 값을 함께 보냅니다.

## 필드는 리더보드마다 따로 정합니다 {#per-leaderboard}

운영자가 Retool의 **리더보드 > 필드** 탭에서 리더보드마다 사용할 필드를 등록합니다. 등록하지 않은 필드를 보내면 기록이 거부되므로, 리더보드마다 필요한 값만 주고받습니다.

## 값 보내기

```csharp
await Supabase.SubmitLeaderboardScoreAsync(
    "arena",
    score: 1250,
    data: new Dictionary<string, object>
    {
        ["character_level"] = 42,
        ["guild_name"]      = "붉은검",
    });
```

## 값 읽기

순위·플레이어 조회 결과의 `.Data`에 등록된 필드만 담겨 옵니다.

```csharp
var result = await Supabase.GetLeaderboardRangeAsync("arena", 1, 50);
foreach (var entry in result.Data)
{
    var level = entry.Data.TryGetValue("character_level", out var v) ? v : null;
    AddRankRow(entry.Rank, entry.DisplayName, entry.Score, level);
}
```

## 전용 클래스 생성 {#generate}

사전을 직접 다루는 대신 리더보드마다 타입이 있는 클래스를 만들 수 있습니다.

메뉴에서 **TrueSoft > Supabase > 클래스 생성 > 리더보드**를 열고 드롭다운에서 리더보드를 고르면, 그 리더보드에 등록된 필드를 자동으로 불러와 클래스를 만듭니다. 코드나 필드 이름을 직접 입력할 필요가 없습니다.

```csharp
// 생성된 ArenaLeaderboardRow 사용 — 행을 그대로 넘기면 코드·데이터를 SDK가 읽습니다
var row = new ArenaLeaderboardRow { CharacterLevel = 42, GuildName = "붉은검" };
await Supabase.SubmitLeaderboardScoreAsync(1250, row);

// 조회 결과에서 복원
var parsed = ArenaLeaderboardRow.FromData(entry.Data);
Debug.Log(parsed.GuildName);
```

리더보드마다 별도 클래스가 생성되므로 서로의 필드가 섞이지 않습니다.

::: warning 필드 추가는 운영자만
필드를 새로 만들거나 지우는 것은 Retool에서만 가능합니다. 게임에서는 이미 등록된 필드에 값을 넣고 읽을 수만 있습니다.
:::
