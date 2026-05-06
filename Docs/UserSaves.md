# 유저 세이브 (User Saves)

---

## [UserSaveColumn] 어노테이션

DB 컬럼과 C# 필드를 매핑합니다. 인자를 생략하면 멤버 이름이 컬럼명으로 사용됩니다.

```csharp
[Serializable]
public class MySave
{
    [UserSaveColumn] public int level;
    [UserSaveColumn] public int coins;
    [UserSaveColumn("last_login_at")] public string lastLoginAt;
}
```

## 로드

```csharp
var save = await Supabase.TryLoadUserSaveAttributedAsync<MySave>();
```

신규 유저 여부를 구분해야 할 때:

```csharp
var (success, hasRow, save) = await Supabase.TryLoadUserSaveAttributedWithRowStateAsync<MySave>();
// hasRow == false → 첫 접속 (DB에 행 없음)
// hasRow == true  → 기존 유저
```

## 저장 (변경분만 전송)

```csharp
// 로드 직후 스냅샷 보관
var prev = save.Clone(); // 또는 별도 복사

// 값 변경
save.level = 5;
save.coins = 200;

// 변경된 컬럼만 PATCH — 변경 없으면 네트워크 전송 없음
await Supabase.TryPatchUserSaveDiffAsync(prev, save);
```

## 자동 동기화 (UserSavesFacade)

매번 직접 저장하는 대신 쿨타임 배치로 자동 동기화합니다.

```csharp
// 쿨타임 내 dirty 발생 시 자동 전송
Supabase.MarkUserSaveStaticDirty(key);

// 중요한 타이밍에 즉시 전송
Supabase.RequestImmediateUserSaveStaticFlush(key);
await Supabase.TryFlushUserSaveImmediateAsync(key);

// 쿨타임 조정 (기본값: SDK 내부 설정)
Supabase.ConfigureUserSaveAutoSyncCooldown(seconds: 5f);
```

`SupabaseRuntime`이 씬에 있으면 앱 Pause/Quit 시 dirty가 있으면 즉시 전송을 시도합니다.

## 에디터 OpenAPI 클래스 생성기

메뉴 **TrueSoft > Supabase > 유저 데이터 클래스 생성**에서 DB 스키마를 기반으로 `[UserSaveColumn]`이 붙은 C# 클래스 초안을 자동 생성할 수 있습니다.

1. `Resources/SupabaseSettings`가 있으면 URL·테이블명이 자동으로 채워집니다.
2. **Secret 키**는 Supabase 대시보드에서 복사해 창에 직접 입력합니다 (에셋에 저장하지 마세요).
3. 테이블명·제외 컬럼·클래스 이름·네임스페이스를 조정한 뒤 미리보기를 확인합니다.
4. **프로젝트에 .cs 저장…**으로 `Assets` 아래에 저장합니다.

생성기가 타입을 추론하지 못한 컬럼(`string /* refine */`)은 직접 수정해야 합니다.

## 테이블 이름 변경

기본값은 `user_saves`입니다. `SupabaseSettings.userSavesTable`에서 변경할 수 있습니다.

## JsonUtility 주의사항

- PostgREST가 반환하는 JSON 키와 C# 필드 이름이 **정확히 일치**해야 값이 채워집니다.
- `[UserSaveColumn("other_name")]`은 select/PATCH 키만 바꿉니다. JSON 역직렬화 키는 바뀌지 않습니다.
- DB 컬럼명과 C# 이름이 다르게 두고 싶다면 Newtonsoft 등 별도 역직렬화가 필요합니다.
- `jsonb` 배열 등 복합 타입은 수동 설계가 필요합니다.
