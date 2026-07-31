---
name: project_retool_env_switching
description: Retool React 앱 환경 전환 제약과 Supabase pooler Username 함정
metadata: 
  node_type: memory
  type: project
  originSessionId: d25e5781-e65c-4b06-a749-94eee8c1f114
---

TrueSoft Retool 통합 앱(트루베이스 = 마왕 앱 `b518a11a-5d80-...`)은 리소스 환경 전환(방식 A)으로 마왕(production=DefenceR)/데빌 슬레이어(staging=DevilSlayer) 두 DB를 오간다. 두 가지 비자명한 제약이 있다.

**1. Retool React 앱은 `?_environment=` URL 전환이 무효.** 환경은 Retool 플랫폼이 내부 세션 상태로 관리하며 **좌측 하단 네이티브 환경 스위처로만** 전환된다. React 앱 코드는 **현재 활성 환경을 읽는 API도 없다**(retoolContext.environment 미노출). 따라서 사이드바에 `<a href="?_environment=...">` 전환 버튼이나 환경 기반 이름 표시는 **동작하지 않으니 만들지 말 것**. 추가로 퍼블릭 배포 도메인(`*.retool.app`)은 **항상 production 고정** — 전환하려면 로그인된 표준 URL(`https://truesoft.retool.com/apps/<uuid>`) 사용.

**2. Supabase Transaction pooler 호스트는 리전 공용.** `aws-1-ap-northeast-1.pooler.supabase.com`은 같은 리전 모든 프로젝트가 공유하는 동일 주소. 프로젝트는 **Host가 아니라 Username 접미사(프로젝트 ref)로 구분**된다. `Supabase DefenceR` 리소스 staging 환경 설정 시 Host만 바꾸고 Username을 production(`postgres.wxivrmvtpufeczltward`) 그대로 두면 staging도 마왕 DB에 인증된다 — 반드시 Username `postgres.owumqjyctqhuyailqutd`(DevilSlayer) + DevilSlayer 비밀번호로. 두 환경 차이는 Username 접미사·Password 뿐.

**Why:** 2026-06-24 staging 전환 시 계속 마왕 데이터가 나와 디버깅. 원인이 ① 직접 만든 사이드바 링크가 URL만 바꿈(네이티브 스위처로는 정상 전환됨) ② pooler 공용 호스트였음. 깨진 사이드바 전환 버튼/이름 로직 제거하고 중립 이름으로 정리.

**How to apply:** Retool React 앱 환경 전환 UI를 코드로 만들려 하지 말 것 — 네이티브 스위처로 안내. pooler 연결은 Username 접미사 확인. 관련: [[project_retool_resource_bindings]]
