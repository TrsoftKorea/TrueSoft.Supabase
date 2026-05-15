# Truesoft Supabase SDK

Unity에서 Supabase Auth, REST, Edge Functions를 사용하기 위한 UPM 패키지입니다.

📖 **[전체 문서 보기](https://trsoftkorea.github.io/TrueSoft.Supabase/)**

---

## 설치

**Window > Package Manager > + > Add package from git URL**

```
https://github.com/trsoftkorea/TrueSoft.Supabase.git
```

특정 버전 설치 시 `#버전`을 추가합니다 (예: `...git#0.1.0`).

---

## 빠른 시작

1. 메뉴 **TrueSoft > Supabase > 설정 에셋 만들기** 로 `SupabaseSettings`를 생성합니다.
2. `Project URL`과 `Publishable Key`를 입력합니다.
3. **`Assets/Resources/SupabaseSettings.asset`** 으로 저장합니다.
4. 메뉴 **TrueSoft > Supabase > 씬에 런타임 오브젝트 만들기** 로 `SupabaseRuntime`을 배치합니다.

자세한 설정 방법은 [빠른 시작 가이드](https://trsoftkorea.github.io/TrueSoft.Supabase/guide/getting-started)를 참고하세요.

---

## 기능

| 기능 | 가이드 |
|------|--------|
| 익명·Google 로그인, 세션 복원, 소셜 연동 | [인증](https://trsoftkorea.github.io/TrueSoft.Supabase/guide/auth) |
| diff-patch 자동 동기화, StaticUserSave 패턴 | [유저 세이브](https://trsoftkorea.github.io/TrueSoft.Supabase/guide/user-saves) |
| Reader·Binding·Listener 세 가지 패턴 | [Remote Config](https://trsoftkorea.github.io/TrueSoft.Supabase/guide/remote-config) |
| Google Play · Apple App Store 서버 검증 | [인앱 결제 (IAP)](https://trsoftkorea.github.io/TrueSoft.Supabase/guide/iap) |
| JWT 인증 포함 서버 함수 호출 | [Edge Functions](https://trsoftkorea.github.io/TrueSoft.Supabase/guide/edge-functions) |
| 닉네임, 프로필 조회 | [공개 프로필](https://trsoftkorea.github.io/TrueSoft.Supabase/guide/public-profile) |
| 테이블 구조, account_id vs user_id | [데이터 스키마](https://trsoftkorea.github.io/TrueSoft.Supabase/guide/data-schema) |

---

## 문의 및 기여

이슈, 기능 제안, 버그 리포트는 [GitHub Issue](https://github.com/trsoftkorea/TrueSoft.Supabase/issues) 탭을 통해 공유해 주세요.
