# Supabase SDK — BasicSetup 샘플

자동 로그인 실패 시 로그인 UI를 자동으로 표시하는 **SDK 전용 Canvas** 시스템을 제공합니다.

- `SupabaseCanvas` — SDK UI 루트. `OnSessionRestored` 이벤트를 구독해 자동 로그인 실패 시 LoginUI를 표시
- `SupabaseLoginUI` — 구글/익명 로그인 버튼 처리

향후 중복 로그인 알림, 탈퇴 확인 등 SDK UI를 이 Canvas에서 확장할 수 있습니다.

---

## 샘플 가져오기

1. Unity 메뉴 **Window** > **Package Manager**
2. **Truesoft Supabase SDK** 선택
3. **Samples** 섹션에서 **BasicSetup** 옆 **Import** 클릭

Import 후 경로:

```
Assets/Samples/Truesoft Supabase SDK/<버전>/BasicSetup/
```

---

## 씬 설정

### 1. SupabaseRuntime 배치

씬에 빈 GameObject를 만들고 `SupabaseRuntime` 컴포넌트를 추가합니다.  
`SupabaseSettings` 에셋을 인스펙터 슬롯에 연결하거나 `Assets/Resources/SupabaseSettings.asset`으로 저장합니다.

### 2. Canvas 계층 구조 생성

아래 구조를 씬에 직접 만듭니다.

```
SupabaseCanvas            [Canvas - SortOrder: 999]
                          [CanvasScaler]
                          [GraphicRaycaster]
                          [SupabaseCanvas 컴포넌트]
  └── LoginPanel          [SupabaseLoginUI 컴포넌트 - 기본 비활성]
        ├── Background    [Image - 반투명 오버레이]
        └── Card
              ├── Title
              ├── GoogleLoginButton     [Button]
              ├── Separator
              └── AnonymousLoginButton  [Button]
```

> **SortOrder를 999 이상으로** 설정해 게임 UI보다 항상 위에 표시되게 합니다.

### 3. 컴포넌트 슬롯 연결

**SupabaseCanvas** 인스펙터:
| 슬롯 | 연결 대상 |
|------|-----------|
| Login UI | `LoginPanel` 오브젝트 |

**SupabaseLoginUI** 인스펙터:
| 슬롯 | 연결 대상 |
|------|-----------|
| Supabase Canvas | `SupabaseCanvas` 오브젝트 |
| Google Login Button | `GoogleLoginButton` |
| Anonymous Login Button | `AnonymousLoginButton` |
| Loading Indicator | 로딩 표시용 오브젝트 (선택) |

### 4. 버튼 OnClick 연결

- `GoogleLoginButton` OnClick → `SupabaseLoginUI.OnGoogleLoginButtonClicked`
- `AnonymousLoginButton` OnClick → `SupabaseLoginUI.OnAnonymousLoginButtonClicked`

---

## 동작 흐름

```
앱 시작
  └→ SupabaseRuntime: 세션 자동 복원 시도
        ├→ 성공: LoginPanel 표시 안 됨, 게임 진입
        └→ 실패: LoginPanel 자동 표시
              ├→ 구글 로그인 버튼 클릭 → TrySignInWithGoogleAsync()
              └→ 게스트 버튼 클릭 → TrySignInAnonymouslyAsync()
                    └→ 성공: LoginPanel 숨김
```

---

## 커스터마이징

스크립트는 Import 후 `Assets/Samples/.../BasicSetup/Scripts/`에 복사됩니다.  
비주얼(색상, 폰트, 레이아웃)은 씬의 Canvas 계층 구조를 직접 수정하세요.  
동작을 변경하려면 `SupabaseCanvas.cs` / `SupabaseLoginUI.cs`를 수정하면 됩니다.
