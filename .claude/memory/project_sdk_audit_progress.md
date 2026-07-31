---
name: project_sdk_audit_progress
description: "SDK 정기 점검 회차별 진행 상태 — 끝난 축, 남은 축, 사용자 결정 대기 항목. 세션이 끊겨도 여기서 이어간다."
metadata: 
  node_type: memory
  type: project
  originSessionId: 2d84c5d4-80fb-4e7d-be34-79931c54df32
  modified: 2026-07-30T08:07:02.760Z
---

SDK 전체를 축 단위로 점검·개선하는 작업이 2026-07-30 시작됐다. 축 목록과 자동화 여부는 `Tools~/SdkAudit/README.md`의 "정기 점검 체크리스트"에 있다. **이 파일은 회차 진행 상태만 기록한다.**

## 1회차 (2026-07-30~)

끝난 축:

- **검사기 구축** — `Tools~/SdkAudit` 신규. R1 공개표면 · R2 리셋대칭 · R3 문서커버리지 · R4 시그니처 · R5 문서형식 · R6 샘플 · R7 미참조 · R8 문서값 · R9 SQL 설치순서
- **공개 표면** — 공개 타입 38 → 19. 파사드 7종·인프라 9종을 `internal`로. `SupabaseBridge` 신설(어셈블리 밖 배선 전용)
- **규칙 모순·모호** — CLAUDE.md 코드 불일치 8건 수정. `RemoteConfig<T>` 적용 범위 확정
- **미사용 코드** — 전수 조사 후 3건 제거. `SetCurrentServerCode`는 기능 누락으로 판명돼 `Supabase.SetServerCode`/`ServerCode`로 노출

- **규칙 위반 — 로그 레벨 분류** — `IsExpectedFailureReason` 12 → 28개. 유저 취소·첫 실행·닉네임 거절 등이 빨간 로그로 나가던 것을 고침. 하드코딩된 `errorOnFail: false` 2곳 제거
- **문서 서술 정확성** — 수치 주장(60초·64자·100건·8자·300초·30분) 전수 대조, 전부 일치
- **문서 구조·톤** — R10 신설(헤딩→시그니처 인접, 파라미터 표 2열)로 자동화
- **중복 코드** — Core 서비스 7개의 `ExtractErrorCode`·`CreateAuthHeaders` 중복을 `SupabaseRestHelpers` 한 벌로
- **비효율 코드** — 매 프레임 Task·List 할당 제거(`HasDuePoll`·`HasDueSubscription` 게이트 + 버퍼 재사용)

- **DB 정합성** — ProjectR 라이브와 `install.sql` 대조. 테이블 29개·함수 94개 일치, 컬럼 이름 전수 확인. 드리프트 1건(`user_data_logs.source` + 트리거의 `app.log_source` 기록)을 `install.sql`에 반영

**1회차 완료 (2026-07-30).** 미결 없음. 판단해 그대로 두기로 한 항목:

- `NotAuthenticated` 로그 레벨 — 세션 만료(정상)와 키 오류(비정상)가 섞이지만 **Error 유지로 확정**(사용자 결정). SDK가 토큰을 자동 갱신하므로 그 뒤에도 거부당하면 비정상 쪽에 가깝다

**문서 톤**도 정리했다 — 용도 열거 문장 2건, 반복되던 타입·동격 병기 괄호(`내 프로필(닉네임·서버 코드 등)` 등 12곳), API 색인 표의 부연 괄호를 가운뎃점으로. 인덱스 42개·RLS 정책 22개·RLS 활성화도 라이브와 대조해 일치 확인.

**표 칸 부연 괄호는 자동 검사를 넣었다가 뺐다** — 수식 표기 `(i)`·`(r, c)`, 버전 `(4.x)`, 파라미터 목록과 구분할 수 없어 오탐이 20건 넘게 나왔다. 노이즈 있는 검사는 없느니만 못하다.

## 규칙 — 실수는 그 자리에서 반영한다

사용자 지시(2026-07-30): 점검 중 실수하거나 실패하면 **다시 하지 않도록 그때그때 규칙으로 남긴다.** 검사로 막을 수 있으면 규칙 대신 검사를 만들고, 재발 범위에 따라 위치를 정한다 — 이 리포지토리 특유면 `/sdk-audit` 스킬의 함정 목록, 어디서든 나는 도구 실수면 전역 `~/.claude/CLAUDE.md`의 "도구 사용" 절.

## 이번 회차에서 배운 것

- **CLAUDE.md를 믿고 코드를 고치면 안 된다.** "SupabaseSDK는 MonoBehaviour singleton"이라는 잘못된 서술 때문에 샘플이 `internal` 클래스를 부르도록 바꿔 컴파일이 깨졌다. 코드로 먼저 확인할 것
- **Samples~는 import 전까지 컴파일되지 않아** Unity 컴파일 통과가 샘플 검증이 아니다. R6이 이 공백을 메운다
- 검사기가 오탐을 내면 규칙의 예외를 검사기에 반영한다. 예외 목록은 SdkAudit README의 표에 있다
