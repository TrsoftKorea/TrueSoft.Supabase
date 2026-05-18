# Supabase SDK — Examples 샘플

인증·유저 데이터·RemoteConfig·IAP 핵심 기능을 키보드 단축키로 빠르게 테스트할 수 있는 예제입니다.

**파일 구성:**
- `SamplePlayerSave.cs` — `StaticUserSave<Row>` 상속 예시 (유저 세이브 클래스 정의 방법)
- `ExampleSupabaseScenarios.cs` — 단축키 기반 테스트 컴포넌트
- `SampleIAPScenarios.cs` — IAP 서버 검증 예제 (`TRUESOFT_IAP_AVAILABLE` 심볼 필요)

---

## 샘플 가져오기

1. **Window > Package Manager** → **Truesoft Supabase SDK** 선택
2. **Samples** 탭 → **Examples** 옆 **Import** 클릭

Import 후 경로:
```
Assets/Samples/Truesoft Supabase SDK/<버전>/Examples/
```

---

## 실행 전 준비

1. **TrueSoft > Supabase > 설정 에셋 만들기**로 `SupabaseSettings` 생성
2. `projectUrl`, `publishableKey` 입력 후 `Assets/Resources/SupabaseSettings.asset`으로 저장
3. (Google 로그인 사용 시) `googleWebClientId` 입력 + Supabase 대시보드 Google Provider 활성화
4. `SamplePlayerSave.cs`의 `[DataTable("basic")]` 실제 테이블명으로 수정
   - 테이블은 `admin_create_user_table` RPC로 생성하면 필수 구조가 자동 적용됩니다

---

## 씬 설정

1. 씬에 빈 GameObject 생성
2. `SupabaseRuntime` 컴포넌트 추가 (자동 로그인·자동 저장 담당)
3. 같은 GameObject에 `ExampleSupabaseScenarios` 컴포넌트 추가
4. (IAP 테스트 시) 같은 GameObject에 `SampleIAPScenarios` 컴포넌트 추가 후 Inspector에서 `productId` 입력
5. Play Mode 진입 후 아래 단축키로 기능 테스트

---

## 키보드 단축키

| 키 | 기능 |
|----|------|
| **Q** | 익명 로그인 |
| **I** | Google 로그인 (Android) |
| **P** | Google 연동 (익명 계정 필요) |
| **W** | 로그아웃 (Google 포함, 로그아웃 전 자동 저장) |
| **R** | 유저 데이터 로드 |
| **V** | 즉시 저장 (변경 없으면 전송 생략) |
| **F** | 레벨 +1 (로컬 변경 + MarkDirty 시연) |
| **T** | RemoteConfig Reader |
| **U** | RemoteConfig Binding (.Value 읽기) |
| **E** | RemoteConfig Listener 시작/종료 토글 |

**SampleIAPScenarios** (`com.unity.purchasing` 5.2.1 이상 필요):

| 키 | 기능 |
|----|------|
| **O** | IAP 초기화 (로그인 후 호출) |
| **B** | 아이템 구매 (결제창 표시) |

선택 기능(Edge Function, 공개 프로필 등)은 `ExampleSupabaseScenarios.cs`의 주석을 해제하면 사용할 수 있습니다.

---

## 확인 방법

- Console에 `[Supabase.*]` 로그가 출력되면 정상입니다.
- 로그인이 필요한 기능은 로그인 전에 실행하면 경고 메시지가 출력됩니다.
- 중복 로그인 감지는 **다른 기기 또는 브라우저**에서 같은 계정으로 로그인해야 확인할 수 있습니다.
