# 닉네임

닉네임 설정·변경과 공개 프로필 조회 기능입니다. 닉네임은 **전역 고유**입니다 — 서버가 여러 개여도 이름 하나로 플레이어를 식별할 수 있습니다.

## 내 닉네임 {#mine}

내 닉네임은 로그인 결과 `.Profile`의 `.Name`으로 읽습니다.

::: info 기본 닉네임 자동 부여
계정은 최초 로그인(프로필 생성) 시 기본 닉네임 `Player_<계정ID 앞 8자>`가 자동으로 부여됩니다. 이 값은 본인 화면 표시, 다른 플레이어 조회, 운영 대시보드 집계에서 항상 동일하게 유지되며, [닉네임 설정](/guide/display-name/nickname/set)으로 바꾸면 세 곳 모두 함께 갱신됩니다.
:::

## 메서드

| 메서드 | 설명 |
|--------|------|
| [`IsNameAvailableAsync`](/guide/display-name/nickname/check) | 닉네임 중복 확인 |
| [`SetNameAsync`](/guide/display-name/nickname/set) | 닉네임 설정 |
| [`GetPublicNameAsync`](/guide/display-name/nickname/get-other) | 다른 플레이어 닉네임 조회 |
