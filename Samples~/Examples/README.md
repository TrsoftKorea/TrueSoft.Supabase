# Supabase SDK — Examples 샘플

하나의 컴포넌트에서 Supabase 주요 기능 예시를 **함수별로 분리**해 제공합니다.

- 로그인 예시
- 로그아웃 예시 (`TrySignOutFullyAsync` — Android면 Google 네이티브 로그아웃 시도 후 Supabase 로그아웃)
- 중복 로그인 감지 예시 (`OnDuplicateLoginDetected` 구독 + 콘솔 안내)
- 유저 세이브 로드/저장 예시 (`SampleStaticUserSave` — `TryLoadAsync`로 서버 데이터 로드, 신규 유저 초기값은 DB 기본값 기반, `TrySaveIfChangedAsync`로 diff만 PATCH)
- 공개 displayName(`display_names` + Edge Functions) 예시 — SQL 적용 + Edge Functions 배포 후 **Run Public DisplayName Example** 또는 전체 실행 시 포함
- RemoteConfig 조회 예시
- Edge Function 호출 예시
- 서버 샤드 예시 — 로컬 서버 코드(`GetCurrentServerCode` / `SetCurrentServerCode`)와 DB의 `ts_my_server_id` 조회, 선택적으로 `TryTransferMyServerAsync`(RPC `ts_transfer_my_server`). 운영·Retool용 `ts_admin_transfer_user_server`는 루트 README 참고.

우편함 API는 런타임 `Supabase.TryGetMyMailsAsync` 등과 SQL `Sql/player/11_mails.sql` 을 참고합니다(별도 Unity 샘플 없음).

**Truesoft Analytics**(`com.truesoft.analytics`)와 함께 쓸 때 서버 시각·`GameEvent` 연동은 두 패키지가 서로를 참조하지 않으므로 **호스트 프로젝트**에서만 연결하면 됩니다. 루트 README **「Truesoft Analytics와 함께 쓸 때」** 절을 참고하세요.

## 샘플 가져오기

1. Unity 메뉴 **Window** > **Package Manager**
2. **Truesoft Supabase SDK** 선택
3. **Samples** 섹션에서 **Examples** 옆 **Import** 클릭

Import 후 예시 경로:

```text
Assets/Samples/Truesoft Supabase SDK/<버전>/Examples/
```

## 실행 전 준비

1. **TrueSoft** > **Supabase** > **설정 에셋 만들기**로 `SupabaseSettings` 생성
2. `projectUrl`, `publishableKey` 입력
3. Android 네이티브 Google 로그인을 쓸 경우 `googleWebClientId` 입력
4. 에셋을 **`Assets/Resources/SupabaseSettings.asset`** 으로 저장
5. (선택) 게스트 로그인 흐름을 쓰면 Supabase 대시보드에서 **Anonymous sign-ins** 활성화
6. (선택) 중복 로그인·`user_sessions`를 쓰려면 `Sql/player/05_user_sessions.sql`을 적용하고, `SupabaseSettings`에서 **Enable Duplicate Session Monitor**를 켭니다.
7. (선택) 서버 이주 샘플은 `Sql/player/01_game_servers.sql`·`08_transfer_server.sql`(및 선행 파일)로 `game_servers`·RPC(`ts_my_server_id`, `ts_transfer_my_server`)가 적용된 뒤, **로그인한 상태**에서 **Run Server Shard Example** 또는 키 **N**으로 실행합니다. 다른 월드로 옮기려면 DB에 목표 `server_code` 행을 추가하고 인스펙터에서 **Server Shard Attempt Transfer**를 켭니다.
8. (선택) `SampleStaticUserSaveRow`의 `[DataTable("custom_saves")]`를 실제 테이블명으로 변경하고, 테이블에 `level int`, `coins int`, `updated_at timestamptz` 컬럼이 있어야 합니다. 신규 유저 초기값은 DB 컬럼 `DEFAULT` 값이 자동 적용됩니다. **Run Load User Save Example** 또는 키 **R**: `TryLoadAsync`로 서버 데이터 로드. **Run Save User Save Example** 또는 키 **V**: 로드로 스냅샷을 맞춘 뒤 인스펙터 값을 반영하고 `TrySaveIfChangedAsync`로 **서버와 다른 컬럼만** PATCH합니다(같으면 요청 없음). 쿨타임 자동 저장은 **Supabase 런타임**(`SupabaseRuntime`)과 프로퍼티 `MarkDirty` 경로를 쓰면 됩니다.
9. 실제 프로젝트에서는 OpenAPI **유저 데이터 클래스 생성** 메뉴로 동일 패턴(`TryLoadAsync` / `TrySaveIfChangedAsync`)을 생성할 수 있습니다.

## 씬에서 실행

1. 테스트 씬을 엽니다.
2. GameObject에 **`ExampleSupabaseScenarios`** 컴포넌트를 붙입니다.
3. 실행 방법
   - 전체 실행: 인스펙터 **`Run All On Start`** 체크 후 Play
   - 개별 실행: 인스펙터 **키보드 테스트**가 켜져 있으면 아래 단축키(기본값, 인스펙터에서 변경 가능)
     - 로그인(익명) **Q**, 구글 **I**, 구글 연동 **P**, 로그아웃 **W**
     - 유저 세이브 로드 **R**, 저장(변경분만) **V**
     - RemoteConfig **T**, 즉시 동기화 **U**, Edge Function **Y**
     - 공개 닉네임 **E**, 서버 시각 **S**, 서버 샤드 **N**, 중복 로그인 안내(콘솔) **L** 등 — 나머지 키는 인스펙터 **키보드 테스트** 블록 참고

## 확인

- Console에 `[Supabase.*]` Try API 로그와 `[Sample] ...` 로그가 출력되면 정상입니다.
- 로그인 필요 예시는 미로그인 상태에서 건너뛰도록 되어 있습니다.
- 중복 로그인은 실제로 **다른 기기(또는 에뮬+실기)** 에서 같은 계정으로 다시 로그인해야 콘솔에 감지 로그가 뜹니다. 인스펙터에서 `subscribeDuplicateLoginOnEnable`(Duplicate login / Logout 섹션)이 켜져 있어야 `OnDuplicateLoginDetected`가 구독됩니다.

## 샘플 삭제

`Assets/Samples/.../Examples` 폴더를 삭제하면 됩니다.

## 문의 및 기여

이슈, 기능 제안, 버그 리포트는 GitHub Issue 탭을 통해 공유해 주세요.  
내부 프로젝트 확장이나 기능 추가가 필요한 경우 담당자에게 직접 문의 바랍니다.
