---
name: project-defencer-consumes-sdk-via-github
description: DefenceR는 SDK를 GitHub Git URL UPM으로 참조. 로컬 SDK 수정은 푸시 전까지 DefenceR에 반영 안 됨.
metadata: 
  node_type: memory
  type: project
  originSessionId: 78f41135-f0e2-49db-a33c-a17d8d8417d5
  modified: 2026-07-27T09:02:40.657Z
---

DefenceR 게임(`D:\Project\DefenceR`)은 com.truesoft.supabase SDK를 GitHub Git URL로 참조한다:
`"com.truesoft.supabase": "https://github.com/trsoftkorea/TrueSoft.Supabase.git"` (Packages/manifest.json).

**의미:**
- DefenceR가 쓰는 SDK는 GitHub에 **마지막으로 푸시된 커밋**이며 `Library/PackageCache/`에 읽기전용으로 캐시된다.
- 로컬 `D:\Project\TrueSoft.Supabase\`의 SDK 수정(asmdef, 생성기, Runtime 코드 등)은 **커밋+푸시 후 DefenceR가 패키지를 재해석(업데이트)해야** 반영된다.
- SDK 생성기(유저 데이터 클래스 생성)도 설치된 패키지 버전이 실행되므로, 로컬 생성기 수정은 푸시 전까지 무의미.

**DefenceR에서 즉시 적용 가능한 것:** `Assets/` 하위의 자체 파일(생성된 PlayerSave.cs 등)은 직접 편집 가능. SDK 변경이 푸시되기 전 임시 컴파일 통과용으로 활용.

게임 측 생성 파일(PlayerSave.cs)은 `using TrueBase.Unity;`만으로 동작한다([[feedback_file_delete]] 관련: 미러 attribute `TrueBase.Unity.DataColumnAttribute` 덕분).
