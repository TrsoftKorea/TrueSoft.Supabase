# Supabase SDK — Examples 샘플

인증·유저 데이터·RemoteConfig·IAP 핵심 기능을 키보드 단축키로 빠르게 테스트할 수 있는 예제입니다.

**파일 구성:**
- `ExampleSupabaseScenarios.cs` — 단축키 기반 테스트 컴포넌트 (`StaticUserSave` 상속 예시인 `SamplePlayerSave` 클래스 포함)
- `SampleIAPScenarios.cs` — IAP 서버 검증 예제 (`com.unity.purchasing` 설치 시 자동 컴파일)
- `SampleAutoCollections.cs` — 자동 확장 2D 컬렉션(`AutoList2D`/`AutoDict2D`) 예제 (로그인·네트워크 불필요)
- `SampleFriend.cs` — 친구 검색·요청·수락·목록·귓속말 예제
- `SampleMatchLobby.cs` — 매치 로비 예제

---

## 샘플 가져오기

1. **Window > Package Manager** → **TrueBase** 선택
2. **Samples** 탭 → **Examples** 옆 **Import** 클릭

Import 후 경로:
```
Assets/Samples/TrueBase/<버전>/Examples/
```

---

## 실행 전 준비

1. **TrueSoft > Supabase > 설정 에셋 만들기**로 `SupabaseSettings` 생성
2. `projectUrl`, `publishableKey` 입력 후 `Assets/Resources/SupabaseSettings.asset`으로 저장
3. (Google 로그인 사용 시) `googleWebClientId` 입력 + Supabase 대시보드 Google Provider 활성화
4. Supabase SQL Editor에서 `SQL/player/install.sql` 실행 (표준 `user_data` 테이블 포함)

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
| **O** | 세션 복원 |
| **R** | 유저 데이터 로드 |
| **V** | 즉시 저장 (변경 없으면 전송 생략) |
| **F** | 레벨 +1 (로컬 변경 + MarkDirty 시연) |
| **T** | RemoteConfig Reader |
| **U** | RemoteConfig Binding (.Value 읽기) |
| **E** | RemoteConfig Listener 시작/종료 토글 |
| **N** | 닉네임 가용성 확인 + 설정 + 프로필 조회 |
| **A** | 내 세션 상태 출력 |
| **J** | 서버 시간 조회 |
| **G** | 차단 정보 조회 |
| **D** | 탈퇴 신청 |
| **S** | 탈퇴 상태 조회 |
| **C** | 탈퇴 취소 |

**SampleIAPScenarios** (`com.unity.purchasing` 필요 — 최신 권장, 최소 5.0.0):

IAP는 로그인 감지 시 자동으로 초기화됩니다.

| 키 | 기능 |
|----|------|
| **M** | 아이템 구매 (결제창 표시, IAP 초기화 완료 후 동작) |

**SampleAutoCollections** (로그인·네트워크·설정 불필요):

빈 GameObject에 이 컴포넌트만 붙이면 됩니다. `SupabaseRuntime` 없이 단독으로 동작합니다.

| 키 | 기능 |
|----|------|
| **1** | AutoList2D 데모 (스테이지 × 웨이브 최고점수) |
| **2** | AutoDict2D 데모 (지역 × 몬스터 처치수) |
| **3** | 저장 → 로드 왕복 (직렬화) |

**SampleFriend** (`SupabaseRuntime` 필요, 두 계정이 있어야 요청→수락 왕복까지 확인 가능):

| 키 | 기능 |
|----|------|
| **1** | 익명 로그인 |
| **2** | 닉네임 검색 |
| **3** | 검색 결과에 친구 요청 보내기 |
| **4** | 받은 요청 목록 |
| **5** | 받은 요청 중 첫 번째 수락 |
| **6** | 친구 목록 |
| **7** | 친구 목록 중 첫 번째에게 귓속말 보내기 |
| **8** | 그 친구와의 대화 조회 |

**SampleMatchLobby** (`SupabaseRuntime` 필요, 초대 대상은 친구가 아니어도 됨 — Inspector의 `inviteAccountId`에 계정 ID 입력):

| 키 | 기능 |
|----|------|
| **1** | 익명 로그인 |
| **2** | 로비 생성 + 초대 — Inspector의 `lobbyName`·`maxMembers` 적용 |
| **3** | 내 로비 목록 |
| **4** | 내가 초대받은 로비 중 첫 번째 수락 |
| **5** | 방금 만든 로비에 추가 초대 |
| **6** | 방금 만든 로비에서 내 참가자 칸 지정 |
| **7** | 방금 만든 로비 시작 |
| **8** | 방금 만든 로비 취소 |
| **9** | 방금 만든 로비 나가기 |
| **0** | 로비 대화 보내기 — Inspector의 `chatMessage` 내용 |
| **-** | 로비 대화 읽기 |

선택 기능(Edge Function, 공개 프로필 등)은 `ExampleSupabaseScenarios.cs`의 주석을 해제하면 사용할 수 있습니다.

---

## 확인 방법

- Console에 `[Supabase.*]` 로그가 출력되면 정상입니다.
- 로그인이 필요한 기능은 로그인 전에 실행하면 경고 메시지가 출력됩니다.
- 중복 로그인 감지는 **다른 기기 또는 브라우저**에서 같은 계정으로 로그인해야 확인할 수 있습니다.
