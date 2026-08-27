# 초기 설정

## 1. API 키 확인

아래 세 항목을 미리 복사해 둡니다.

| 항목 | 찾는 위치 | 사용처 |
|------|-----------|--------|
| **Project URL** | 프로젝트&nbsp;오버뷰&nbsp;URL&nbsp;옆&nbsp;**Copy**&nbsp;드롭다운 | `SupabaseSettings.projectUrl` |
| **Publishable key** | **Settings&nbsp;>&nbsp;API&nbsp;Keys**&nbsp;→&nbsp;Publishable&nbsp;key&nbsp;섹션&nbsp;복사&nbsp;버튼 | `SupabaseSettings.publishableKey` |
| **Secret key** | **Settings&nbsp;>&nbsp;API&nbsp;Keys**&nbsp;→&nbsp;Secret&nbsp;keys&nbsp;섹션&nbsp;복사&nbsp;버튼 | 유저 데이터·Remote Config 클래스 생성기 전용. EditorPrefs 저장, 빌드 미포함 |

## 2. 설정 에셋 생성

1. 메뉴 **TrueSoft > Supabase > 설정 에셋 만들기** 를 클릭합니다. `Assets/Resources/SupabaseSettings.asset`이 자동 생성됩니다.
2. Inspector에서 **Project URL**, **Publishable Key**를 입력합니다.

에셋을 옮긴다면 `Assets/Resources/` 하위에 두세요. 런타임은 이 경로에서 설정을 로드합니다.

## 3. 런타임 배치

메뉴 **TrueSoft > Supabase > 씬에 런타임 오브젝트 만들기** 를 클릭합니다.  
앱의 첫 씬에 `SupabaseSDK` 게임 오브젝트가 생성되고 `SupabaseRuntime` 컴포넌트와 `SupabaseSettings`가 자동으로 연결됩니다.

자동 로그인 타이밍 제어와 이벤트 콜백 사용법은 [자동 로그인](/guide/auth/auto-login)을 참고하세요.
