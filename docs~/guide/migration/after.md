# 플레이나누 제거 후

1. `PlayNANOO Migration` 샘플 폴더를 통째로 삭제합니다 — `PlayNanooRuntimeBase.cs` / `PlayNanooRuntime.cs` / `PlayNanooLegacyRuntime.cs` / `TrueBaseNanoo.cs`
2. 씬에 `SupabaseRuntime` 배치
3. 게임 코드에서 `TrueBaseNanoo.*` 호출을 지웁니다. `Supabase.*` 호출은 그대로 둡니다
