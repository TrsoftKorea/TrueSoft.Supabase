# 탈퇴 취소 — 토큰 방식

유예 기간이 지나 이미 탈퇴가 완료된 경우, 토큰을 이용해 계정을 복구할 수 있습니다.  
서버에서 토큰을 발급받아 이메일 등으로 전달하고, 플레이어가 해당 토큰으로 취소를 완료하는 방식입니다. [Edge Function 배포](/guide/start/database-setup#edge-function-deploy)가 선행되어야 합니다.

## 취소 토큰 발급

```csharp
Task<string> Supabase.TryRequestWithdrawalCancelTokenAsync(string defaultValue = null)
```

탈퇴 취소 토큰을 발급합니다. 실패 시 `defaultValue`를 반환합니다.

**파라미터**

| 파라미터 | 설명 | 타입 |
|----------|------|------|
| `defaultValue` | 토큰 발급 실패 시 반환할 기본값 (기본값: `null`) | `string` |

---

## 취소 토큰 사용

```csharp
Task<SupabaseCallResult> Supabase.TryRedeemWithdrawalCancelAsync(string cancelToken = null)
```

탈퇴 취소 토큰을 사용해 탈퇴를 취소합니다.

**파라미터**

| 파라미터 | 설명 | 타입 |
|----------|------|------|
| `cancelToken` | 플레이어가 입력한 취소 토큰 | `string` |

**실패 원인**

| Reason | 설명 |
|--------|------|
| `SupabaseFailReason.NotSignedIn` | 로그인 상태가 아닙니다 |
| `SupabaseFailReason.NetworkError` | 네트워크 오류 또는 타임아웃 |
