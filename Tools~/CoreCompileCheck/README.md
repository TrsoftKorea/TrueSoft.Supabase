# Core 컴파일 체크

Unity 없이 `TrueBase.Core` 레이어(`Runtime/Core/`)를 `dotnet`으로 컴파일해 구조적 오류를 즉시 잡는 도구입니다.

## 왜 필요한가

이 저장소는 Unity가 C# 소스를 직접 컴파일하므로 CLI 빌드가 없습니다. 그래서 리팩터링 중 생성자 시그니처 변경·참조 끊김 같은 오류를 Unity를 켜기 전에는 확인하기 어렵습니다.

`TrueBase.Core`는 `TrueBase.Core.asmdef`에 `noEngineReferences: true`가 설정되어 **UnityEngine 의존이 0**입니다(외부 의존은 Newtonsoft.Json뿐). 따라서 `dotnet build`만으로 컴파일 검증이 가능합니다.

## 사용법

```sh
dotnet build Tools~/CoreCompileCheck/CoreCompileCheck.csproj
```

- 종료 코드 0 + `경고 0` = Core 레이어 구조 정상.
- 산출 DLL(`bin/`)은 버리는 용도입니다. 목적은 컴파일 성공 여부뿐입니다.

## 범위와 한계

- **검증 대상**: `Runtime/Core/**/*.cs` 전체.
- **검증 불가**: `Runtime/Unity/`·`Editor/`는 UnityEngine에 의존하므로 이 도구로 컴파일되지 않습니다. 그쪽 변경은 여전히 Unity 재컴파일로 확인해야 합니다.
- CS0649(JSON DTO 필드)·CS1591(XML 주석 누락)은 이 코드베이스의 정상 패턴이라 `csproj`의 `NoWarn`으로 억제했습니다. 그 외 경고가 뜨면 실제 문제일 가능성이 높습니다.

## Unity 영향

폴더 이름이 `~`로 끝나 Unity가 완전히 무시합니다(`.meta` 미생성, AssetDatabase 제외).
