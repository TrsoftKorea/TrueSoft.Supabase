# 예약 상태 확인

탈퇴 예약 중에는 로그인이 `WithdrawalGateBlocked`로 막히고, **로그인 결과에 삭제 예정 시각이 실려 옵니다.** 별도 조회 없이 그 값으로 남은 시간을 계산해 안내 UI를 띄웁니다.

```csharp
var login = await Supabase.TriggerAutoLoginAsync();
if (login.Reason == SupabaseReason.WithdrawalGateBlocked && login.WithdrawnAt.HasValue)
{
    var now = (await Supabase.GetServerNowAsync()).Data;   // 서버 시각
    var remaining = login.WithdrawnAt.Value - now;   // 남은 시간
    ShowWithdrawalBanner(remaining);
}
```

## 로그인 결과 필드

| 프로퍼티 | 타입 | 설명 |
|---------|------|------|
| `.WithdrawnAt` | `DateTimeOffset?` | 삭제 예정 시각. 게이트 차단이 아니면 null |
| `.WithdrawalCancelToken` | `string` | 비로그인 취소 토큰. [탈퇴 취소](./cancel)에서 사용 |

::: info
남은 시간은 기기 시계가 아니라 **서버 시각 기준**으로 계산해야 정확합니다. `GetServerNowAsync()`는 로그인 없이도 호출됩니다.
:::
