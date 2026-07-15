# 게임 데이터 API

## 유저 데이터

유저 세이브는 생성된 `StaticUserSave<T>` 클래스(예: `PlayerSave`)를 통해 다루는 것이 기본입니다.

| 멤버 | 설명 |
|------|------|
| [`PlayerSave.LoadAsync`](/guide/user-data/load) | 생성된 세이브 클래스 로드 |
| [`PlayerSave.DeleteAsync`](/guide/user-data/delete) | 세이브 삭제(기본값 리셋) |
| [`Supabase.SaveAllAsync`](/guide/user-data/immediate-save) | 변경된 세이브 즉시 저장 |

::: tip
프로퍼티 읽기/쓰기, 컬럼 추가, 클래스 생성은 [유저 데이터 가이드](/guide/user-data/how-it-works)를 참고하세요.
:::

## 원격 설정

| 멤버 | 설명 |
|------|------|
| [`RemoteConfig<T>.CreateReader`](/guide/remote-config/reader) | 요청 시 값을 읽는 Reader 생성 |
| [`RemoteConfig<T>.CreateBinding`](/guide/remote-config/binding) | 자동 갱신 Binding 생성 |
| [`RemoteConfig<T>.CreateListener`](/guide/remote-config/listener) | 값 변경 콜백 Listener 생성 |
