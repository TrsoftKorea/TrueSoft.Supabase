# 플레이나누 이관

플레이나누와 SDK를 병행 운영하다가 단계적으로 SDK로 전환하는 흐름입니다.

## 동작 방식 {#how-it-works}

플레이나누 기반 라이브 서비스를 SDK로 전환할 때 사용하는 브릿지 패턴입니다.  
두 백엔드를 동시에 운영하다가 플레이나누를 완전히 제거하는 흐름을 지원합니다.

`PlayNanooRuntimeBase`가 `SupabaseRuntime`을 상속하며, `Awake` 시점에 `SupabaseBridge`로 SDK 내부에 인터셉터를 등록합니다.  
이후 게임 코드가 `Supabase.SignInAnonymouslyAsync()` 등을 호출하면 **플레이나누 로그인과 SDK 로그인이 함께 실행됩니다.** 로그인은 둘을 동시에 보내고, 계정 연동은 플레이나누가 성공한 뒤 SDK 연동을 이어 갑니다.

브릿지가 없으면 인터셉터도 없으므로, **게임 코드는 이관 전·중·후 동일합니다.**

::: info 롤백
플레이나누 로그인은 성공했는데 Supabase 로그인·연동이 실패하면, 새로 받은 플레이나누 세션을 로그아웃하고 **그 전에 로그인돼 있던 플레이나누 세션을 되살립니다.** 플레이 중 연동이 실패하거나 게스트가 소셜 로그인 버튼을 눌러 거절돼도 원래 세션으로 계속 플레이할 수 있습니다. 로그인돼 있지 않았다면 로그아웃만 합니다.  
**로그인·계정 연동 시에만 적용**됩니다. 로그아웃·탈퇴는 한쪽 실패 시 롤백 없이 경고 로그만 출력됩니다. 게스트 연동은 플레이나누 계정 자체를 바꾸므로 플레이나누 쪽을 되돌리지 못합니다 — [게스트 계정 연동](/guide/migration/login#guest-link)을 참고하세요.
:::

## 이관 순서 {#steps}

<div class="tb-cards">

- [**설치**<br><small>런타임 배치</small>](/guide/migration/install)
- [**로그인**<br><small>게스트·소셜·자동 로그인</small>](/guide/migration/login)
- [**탈퇴 취소**<br><small>표준 취소 API·자동 나누 복구</small>](/guide/migration/withdrawal)
- [**인앱 결제**<br><small>플레이나누 → SDK 검증</small>](/guide/migration/iap)
- [**데이터 동기화**<br><small>updated_at 기반 동기화</small>](/guide/migration/sync)
- [**플레이나누 제거 후**<br><small>파일 삭제·정리</small>](/guide/migration/after)

</div>
