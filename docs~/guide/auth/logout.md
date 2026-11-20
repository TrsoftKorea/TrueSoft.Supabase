# 로그아웃

```csharp
Task<SupabaseResult> Supabase.SignOutFullyAsync()
```

로그아웃하고 세션을 정리합니다. 익명 계정이면 기기 지문 기반 복구 토큰을 서버에 저장해 동일 기기에서 재접속 시 같은 계정으로 이어집니다.  
Google 로그인을 사용 중이라면 Android Play Services 계정 선택기도 함께 초기화됩니다.

```csharp
var result = await Supabase.SignOutFullyAsync();
if (result.IsSuccess)
{
    ShowLoginScreen();   // 로그아웃 완료
}
else
{
    // 실패 처리
}
```

iOS는 앱이 들고 있는 Google 계정 상태가 없어 다음 로그인 때 계정을 다시 고릅니다.

**실사용 예시**

설정 화면의 로그아웃 버튼에서 남은 변경을 저장한 뒤 로그아웃하고 타이틀로 돌아갑니다.

```csharp
public async void OnClickLogout()
{
    var save = await Supabase.SaveNowAsync();
    if (!save.IsSaved) { ShowToast("저장하지 못해 로그아웃을 멈췄습니다"); return; }

    var result = await Supabase.SignOutFullyAsync();
    if (!result.IsSuccess) { ShowToast("로그아웃하지 못했습니다. 다시 시도해 주세요"); return; }
    SceneManager.LoadScene("Title");
}
```
