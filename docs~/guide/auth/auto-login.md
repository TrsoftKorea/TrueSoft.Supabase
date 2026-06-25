# 자동 로그인

씬에 `SupabaseRuntime` 컴포넌트를 배치하면 SDK가 초기화됩니다.  
로그인은 자동 실행되지 않으므로 원하는 타이밍에 직접 호출합니다.

```csharp
var result = await SupabaseRuntime.TriggerAutoLoginAsync();
if (result.Success)
{
    // 자동 로그인 성공 → 유저 세이브 로드도 완료된 상태
    InitGame();
}
else
{
    // 저장된 세션 없음 (첫 실행 또는 로그아웃 후) → 로그인 화면으로 이동
    ShowLoginScreen();
}
```

## 로그인 후 사용 가능한 값 {#after-login-values}

로그인이 성공하면 아래 프로퍼티를 바로 사용할 수 있습니다.

| 프로퍼티 | 설명 |
|---------|------|
| `Supabase.MyProfile.DisplayName` | 닉네임. 설정 전에는 빈 문자열 |
| `Supabase.MyProfile.ServerCode` | 서버 코드 (예: `"GLOBAL"`, `"KR1"`) |
| `Supabase.MyProfile.IsWithdrawn` | 탈퇴 예약 여부 |
| `Supabase.UserId` | 플레이어 고유 ID. 재로그인·계정 연동 후에도 변하지 않음 |
| `Supabase.IsAnonymous` | 익명 로그인 여부 |
| `Supabase.IsLinkedWithGoogle` | Google 연동 여부 |
| `Supabase.IsLinkedWithApple` | Apple 연동 여부 |
