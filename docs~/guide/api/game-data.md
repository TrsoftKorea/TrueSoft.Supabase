# 게임 데이터 API

## 유저 데이터

데이터 읽기·쓰기는 생성된 세이브 클래스의 정적 프로퍼티(`PlayerSave.Level` 등)로, 로드·저장·삭제는 아래 파사드 API로 합니다.

| 멤버 | 설명 |
|------|------|
| [`Supabase.LoadUserSaveAsync`](/guide/user-data/load) | 생성된 세이브 클래스 로드 |
| [`Supabase.DeleteUserSaveAsync`](/guide/user-data/delete) | 세이브 삭제(기본값 리셋) |
| [`Supabase.SaveNowAsync`](/guide/user-data/save-now) | 변경분 즉시 전송 후 완료 대기 |
| [`Supabase.RequestSave`](/guide/user-data/request-save) | 대기 없이 즉시 전송 요청 |
| [`Supabase.SaveIfChangedAsync`](/guide/user-data/save-if-changed) | 변경된 필드만 PATCH |
| [`Supabase.EnsureUserSaveRowAsync`](/guide/user-data/ensure-row) | DB 본인 행 생성 보장 |

::: tip
프로퍼티 읽기/쓰기, 컬럼 추가, 클래스 생성은 [유저 데이터 가이드](/guide/user-data/how-it-works)를 참고하세요.
:::

## 원격 설정

| 멤버 | 설명 |
|------|------|
| [`RemoteConfig<T>.CreateReader`](/guide/remote-config/reader) | 요청 시 값을 읽는 Reader 생성 |
| [`RemoteConfig<T>.CreateBinding`](/guide/remote-config/binding) | 자동 갱신 Binding 생성 |
| [`RemoteConfig<T>.CreateListener`](/guide/remote-config/listener) | 값 변경 콜백 Listener 생성 |
