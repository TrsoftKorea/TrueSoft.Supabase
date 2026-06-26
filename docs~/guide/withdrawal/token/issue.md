# 취소 토큰 발급

```csharp
Task<string> Supabase.TryRequestWithdrawalCancelTokenAsync(string defaultValue = null)
```

탈퇴 취소 토큰을 발급합니다. 실패 시 `defaultValue`를 반환합니다.

**파라미터**

| 파라미터 | 설명 |
|----------|------|
| `defaultValue` | 토큰 발급 실패 시 반환할 기본값 (기본값: `null`) |

**반환**

발급된 탈퇴 취소 토큰 문자열. 발급 실패 시 `defaultValue`.
