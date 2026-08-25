# 설치

1. Package Manager **Samples** 탭에서 **PlayNANOO Migration**을 Import합니다.
2. 씬에서 `SupabaseRuntime` 대신 SDK 버전에 맞는 컴포넌트를 하나 배치합니다.

| 구현체 | 사용 API |
|--------|---------|
| `PlayNanooRuntime` | `AccountManagerV20240401.*` (신버전) |
| `PlayNanooLegacyRuntime` | `AccountGuestSignIn` / `AccountManager.*` (구버전) |

씬에는 `PlayNanooRuntime` / `PlayNanooLegacyRuntime` 중 하나만 둡니다. `SupabaseRuntime`을 따로 배치하지 않습니다.
