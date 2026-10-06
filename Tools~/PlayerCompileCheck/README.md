# 플레이어 조건 컴파일 체크

SDK를 **실제 Android·iOS 빌드와 같은 조건**으로 컴파일해, 플랫폼 전용 코드의 컴파일 오류를 기기 빌드 전에 잡는 도구입니다.

## 왜 필요한가

유니티 에디터는 빌드 대상을 iOS로 바꿔도 에디터 어셈블리로 컴파일하므로 `UNITY_EDITOR`가 항상 켜져 있습니다. 그래서 `#if UNITY_IOS && !UNITY_EDITOR`·`#if UNITY_ANDROID && !UNITY_EDITOR` 안쪽(네이티브 로그인 호출 등)은 **에디터 컴파일에서 한 번도 컴파일되지 않고**, 콘솔은 "오류 0"을 보여 줍니다. 배치 모드로 `-buildTarget iOS` 컴파일을 돌려도 마찬가지입니다.

이 도구는 SDK Host가 남긴 컴파일 인자(`Library/Bee/artifacts/*.dag/*.rsp`)에서 에디터 기호만 빼고, 유니티에 들어 있는 Roslyn 컴파일러로 직접 컴파일합니다. iOS 판은 Android 인자에서 플랫폼 기호만 바꿔 만듭니다.

## 사용법

Git Bash에서:

```sh
bash "Tools~/PlayerCompileCheck/run.sh"
```

- 마지막 줄이 `결과: OK`이고 종료 코드가 0이면 통과입니다.
- 각 단계의 오류·경고가 파일 위치와 함께 바로 아래 줄에 나옵니다.

| 단계 | 대상 |
|------|------|
| Core | `TrueBase.Core` |
| Unity Android·iOS | `TrueBase.Unity` |
| IAP Android·iOS | `TrueBase.Unity.IAP` — SDK Host에 `com.unity.purchasing` 5.x가 있을 때만 |
| 나누샘플 Android·에디터 | `Samples~/PlayNanooMigration` — PlayNANOO DLL이 있을 때만 |

## 대조

컴파일이 통과해도 기호가 기대대로 바뀌지 않았다면 아무것도 검사하지 않은 것입니다. 그래서 iOS 전용 메서드(`GoogleLoginBridge.StartIos`)가 **iOS 판에만** 들어갔는지 결과물에서 확인하고, 어긋나면 `대조 실패`로 실패시킵니다. 이 표지를 다른 이름으로 바꿀 때는 `#if UNITY_IOS && !UNITY_EDITOR` 안에만 있는 멤버를 골라야 합니다.

## 준비 조건

- **SDK Host가 Android 대상으로 한 번 이상 컴파일돼 있어야 합니다.** 인자 파일이 없으면 그 사실을 알리고 멈춥니다.
- SDK Host가 유니티에서 열려 있어도 됩니다. 배치 모드와 달리 프로젝트 잠금과 무관합니다.
- 경로는 환경 변수로 바꿀 수 있습니다.

| 변수 | 기본값 |
|------|--------|
| `SDK_HOST` | `D:/Project/SDK Host` |
| `NANOO_DLL` | `D:/Project/ProjectNS_Android/Assets/PlayNANOO/PlayNANOOAndroid.dll` |

## 범위와 한계

- 컴파일만 봅니다. Xcode·Gradle 빌드, 네이티브 플러그인(`.mm`·`.aar`) 링크, 실기기 동작은 확인하지 않습니다.
- 유니티가 쓰는 경고 억제(`/nowarn:0649` 등)를 그대로 따르므로, 유니티 콘솔에 안 뜨는 경고는 여기서도 안 뜹니다.
- 산출물은 `out/`에 남으며 저장소에 들어가지 않습니다.

## Unity 영향

폴더 이름이 `~`로 끝나 Unity가 완전히 무시합니다(`.meta` 미생성, AssetDatabase 제외).
