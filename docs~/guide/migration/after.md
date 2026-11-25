# 플레이나누 제거 후

1. 씬의 `PlayNanooRuntime`(또는 `PlayNanooLegacyRuntime`)을 `SupabaseRuntime`으로 바꿉니다
2. 게임 코드에서 `TrueBaseNanoo.*` 호출을 지웁니다. `Supabase.*` 호출은 그대로 둡니다
3. PlayNANOO 플러그인을 지웁니다. SDK의 병행 런타임은 PlayNANOO가 없어지면 저절로 꺼집니다

::: warning 순서를 지키세요
PlayNANOO를 먼저 지우면 병행 런타임이 꺼져, 씬의 컴포넌트가 `Missing`이 되고 `TrueBaseNanoo`를 부르는 게임 코드가 컴파일되지 않습니다.
:::
