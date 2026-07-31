---
name: project_apple_services_id_naming
description: "Apple Services ID 네이밍 컨벤션 — `번들ID.Services` 형식으로 통일"
metadata: 
  node_type: memory
  type: project
  originSessionId: 19daf180-2fe2-4a6e-9012-9588ccfe3104
---

회사 프로젝트의 Apple **Services ID**(웹/Android Sign in with Apple용)는 `번들ID.Services` 형식으로 통일한다. 예: 번들 ID가 `com.TrueSoft.DefenceR`이면 Services ID는 `com.TrueSoft.DefenceR.Services`.

**Why:** 프로젝트마다 Services ID 식별자를 일관되게 두기 위한 사내 규칙(사용자가 통일했다고 명시).
**How to apply:** Apple 로그인 문서·예시에서 Services ID 예시를 `번들ID.Services`로 적는다(`*.signin` 등 다른 접미사 쓰지 말 것). Supabase Apple provider의 Client IDs에 넣는 Android용 audience도 이 값. 관련: [[project_defencer_consumes_sdk_via_github]].

## Supabase Apple Client IDs 순서 (확정, 실증됨)

Supabase Apple provider의 **Client IDs**는 쉼표 목록인데, **Android(웹) OAuth의 client_id로 목록의 첫 값**이 쓰인다. 그래서 **Services ID를 맨 앞**에 두고 iOS 번들 ID를 뒤에 둔다: `com.TrueSoft.DefenceR.Services,com.TrueSoft.DefenceR`.
- 번들 ID가 첫 값이면 Apple이 `invalid_request: Invalid client id or web redirect url`로 거부(번들 ID엔 웹 redirect 설정이 없음).
- iOS 네이티브는 id_token의 `aud`(=번들 ID)가 **목록에 포함**돼 있으면 검증 통과(순서 무관).
- Secret Key(client_secret JWT)의 `sub`는 Services ID여야 하고, Domains·Return URLs도 그 Services ID에 설정.
- **Supabase "Secret Key (for OAuth)" 필드에는 `.p8` 파일 원문이 아니라 그것으로 생성한 client_secret(JWT, `eyJ...`)을 넣는다.** 생성은 Supabase 가이드 생성기(Team ID·Key ID·Services ID·`.p8` 입력 → JWT 출력) 사용. 6개월 만료.
