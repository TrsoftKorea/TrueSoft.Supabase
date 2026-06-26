# 로그아웃

```csharp
Task<SupabaseCallResult> Supabase.TrySignOutFullyAsync()
```

로그아웃하고 세션을 정리합니다. 익명 계정이면 기기 지문 기반 복구 토큰을 서버에 저장해 동일 기기에서 재접속 시 같은 계정으로 이어집니다.  
Google 로그인을 사용 중이라면 Android Play Services 계정 선택기도 함께 초기화됩니다.
