# Google 추가 연동 · Android

```csharp
Task<SupabaseResult> Supabase.LinkGoogleNativeAsync()
```

이미 로그인된 계정(익명 포함)에 Android Play Services Google 계정을 추가 연동합니다.

```csharp
var result = await Supabase.LinkGoogleNativeAsync();
if (result.IsSuccess)
{
    // 연동 완료
}
```
