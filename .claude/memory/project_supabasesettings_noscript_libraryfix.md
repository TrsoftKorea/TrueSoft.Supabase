---
name: project-supabasesettings-noscript-libraryfix
description: "SupabaseSettings 에셋 \"No script asset\"/m_Script 0 + 컴파일 에러 없음 → Library 캐시 어긋남. Library 삭제·재실행으로 해결."
metadata: 
  node_type: memory
  type: project
  originSessionId: f28c0381-b824-4f64-b9e3-85a06aa987ec
---

소비 프로젝트에서 `SupabaseSettings.asset`의 스크립트 할당이 풀리고(`m_Script: {fileID: 0}`) "No script asset for SupabaseSettings ... compiles properly" 경고가 뜨는데 **콘솔에 CS 컴파일 에러가 없고, 에셋을 재생성해도 똑같이 깨진** 경우:

**원인:** 코드/컴파일 문제가 아니라 **Unity Library(AssetDatabase) 캐시의 타입↔MonoScript 매핑이 어긋난 상태.** `TrueBase.Unity.dll`은 빌드돼 있어도(타입 존재) 로드된 도메인이 스크립트 매핑을 못 함. SDK가 이름 변경(코드 리네임) 이력이 있어, GitHub 패키지를 갱신한 프로젝트에서 발생하기 쉬움.

**해결:** Unity 종료 → 프로젝트 **`Library` 폴더 삭제** → 재실행(전체 재임포트) → `TrueSoft > Supabase > 설정 에셋 만들기`로 재생성 → URL·Key 입력. (가볍게는 `Library/ScriptAssemblies`·`PackageCache/com.truesoft.supabase@*`·`ArtifactDB*`·`SourceAssetDB`만 삭제.)

확인 순서: GUID 충돌·중복 타입·Newtonsoft·CS 에러를 먼저 배제(이들이 다 정상인데도 깨지면 Library 캐시 문제). 현재 SupabaseSettings.cs GUID는 `6cc5e8132c087a744b47fa87b49d01c3`로 안정적. [[project_defencer_consumes_sdk_via_github]]
