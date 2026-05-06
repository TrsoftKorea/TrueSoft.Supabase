# Truesoft Supabase SDK

Unity에서 Supabase Auth, REST, Edge Functions를 사용하기 위한 UPM 패키지입니다.

---

## 설치

**Window > Package Manager > + > Add package from git URL**

```
https://github.com/your-org/com.truesoft.supabase.git
```

특정 버전 설치 시 `#버전` 을 추가합니다 (예: `...git#0.1.0`).  
사용 가능한 버전은 [CHANGELOG.md](CHANGELOG.md)를 확인하세요.

---

## 빠른 시작

1. 메뉴 **TrueSoft > Supabase > 설정 에셋 만들기** 로 `SupabaseSettings`를 생성합니다.
2. `projectUrl`과 `publishableKey`를 입력합니다.
3. **`Assets/Resources/SupabaseSettings.asset`** 으로 저장합니다.
4. (선택) `SupabaseRuntime`을 씬에 배치해 세션 자동 복원·RemoteConfig 폴링을 활성화합니다.

```csharp
// 앱 시작 시 초기화 + 세션 복원
await Supabase.TryStartAsync();
```

---

## 기능

### 인증 → [Docs/Auth.md](Docs/Auth.md)

```csharp
await Supabase.TrySignInAnonymouslyAsync();       // 익명 로그인
await Supabase.TrySignInWithGoogleAsync();        // Google 로그인 (Android)
await Supabase.TrySignInWithAppleAsync();         // Apple 로그인 (iOS)
await Supabase.TryLinkGoogleToCurrentAnonymousAsync(); // 익명 → Google 연동
await Supabase.TrySignOutFullyAsync();            // 로그아웃
```

### 유저 세이브 → [Docs/UserSaves.md](Docs/UserSaves.md)

```csharp
// 컬럼 어노테이션으로 로드 / 변경분만 저장
var save = await Supabase.TryLoadUserSaveAttributedAsync<MySave>();
await Supabase.TryPatchUserSaveDiffAsync(prev, current);
```

### Remote Config → [Docs/RemoteConfig.md](Docs/RemoteConfig.md)

```csharp
var (ok, cfg) = await Supabase.TryGetRemoteConfigAsync<GameConfig>("gameplay_v1");
```

### 공개 프로필 / 닉네임 → [Docs/PublicProfile.md](Docs/PublicProfile.md)

```csharp
await Supabase.TrySetMyDisplayNameAsync("Player123");
var profile = await Supabase.TryGetPublicProfileAsync(userId);
```

### 인앱 결제 (IAP) → [Docs/IAP.md](Docs/IAP.md)

Android(Google Play)와 iOS(Apple App Store)를 자동 감지합니다.  
`com.unity.purchasing` **5.2.1 이상** 필요.

```csharp
var iap = Supabase.CreateIAP();
iap.OnGrantItemAsync = async (order, response, isResuming) => {
    var productId = order.CartOrdered.Items()[0].Product.definition.id;
    await GiveItem(productId);
    return true; // true → 소모품 소비 완료
};
await iap.InitializeAsync(new[] { "com.mygame.item_1000" });
iap.Purchase("com.mygame.item_1000");
```

### Edge Functions → [Docs/EdgeFunctions.md](Docs/EdgeFunctions.md)

```csharp
var result = await Supabase.TryInvokeFunctionAsync<GachaResponse>(
    "gacha-draw", new { bannerId = "normal_banner", drawCount = 10 }
);
```

### 채팅

```csharp
await Supabase.TryJoinChatChannelAsync(channelId);
await Supabase.TrySendChatMessageAsync(channelId, "Hello");
```

---

## DB 스키마

`Sql/player/` 폴더의 SQL 파일을 번호 순으로 Supabase SQL Editor에서 실행합니다.  
테이블 구조·`account_id` vs `user_id` 설명·서버 이주·법적 데이터 보관 설계는 [Docs/DataSchema.md](Docs/DataSchema.md)를 참고하세요.

---

## 샘플

Package Manager의 **Samples** 탭에서 **Import** 후 사용합니다.  
`ExampleSupabaseScenarios.cs` — 키보드 단축키 기반 기능별 테스트 흐름.

---

## 문의 및 기여

이슈, 기능 제안, 버그 리포트는 GitHub Issue 탭을 통해 공유해 주세요.
