# 유저 데이터

게임 진행 상황(레벨, 골드, 아이템 등)을 서버에 저장하고 기기 간 동기화하는 기능입니다.

## 동작 방식 {#how-it-works}

`StaticUserSave<TRow>`는 유저 데이터를 관리하는 기반 클래스입니다. 변경된 필드만 서버에 전송하며, 쿨타임 배치로 불필요한 요청을 줄입니다.

| 동작 | 설명 |
|------|------|
| Diff 패치 | 이전 상태와 비교해 변경된 필드만 서버에 전송합니다. 변경이 없으면 네트워크 요청을 생략합니다. |
| 쿨타임 자동 저장 | 프로퍼티 값을 바꾸거나 컬렉션을 수정하면 `SupabaseRuntime`이 쿨타임 주기로 자동 업로드합니다. |
| 즉시 저장 | `Supabase.SaveNowAsync()`는 쿨타임을 무시하고 변경분을 즉시 전송합니다. |

세이브 클래스는 [클래스 생성](/guide/user-data/class-gen)으로 만들고, 값은 [읽기 / 쓰기](/guide/user-data/read-write)로 다룹니다.

## 메서드

| 메서드 | 설명 |
|--------|------|
| [`LoadUserSaveAsync`](/guide/user-data/load) | 서버에서 세이브 불러오기 |
| [`SaveNowAsync`](/guide/user-data/save-now) | 변경분 전송 후 완료 대기 |
| [`RequestSave`](/guide/user-data/request-save) | 대기 없이 저장 요청 |
| [`SaveIfChangedAsync`](/guide/user-data/save-if-changed) | 변경된 필드만 저장 |
| [`DeleteUserSaveAsync`](/guide/user-data/delete) | 내 세이브 행 삭제 |
