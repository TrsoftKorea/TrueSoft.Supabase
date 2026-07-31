---
name: project_iap_v4_v5_support
description: SDK IAP는 Unity IAP v4(4.x)와 v5(5.0+) 둘 다 지원. PlayNANOO SK1은 5.1+. versionDefine 3개 모두 IAP asmdef에(package.json 금지). v5-전용 아님.
metadata: 
  node_type: memory
  type: project
  originSessionId: 30cd5382-d7b4-4ee1-9991-960d6026619f
---

SDK IAP는 **Unity IAP v4(4.x)와 v5(5.0 이상)를 모두 지원**한다(`com.unity.purchasing` ≥4.0.0). v5 엔진이 쓰는 `UnityIAPServices.StoreController()`·Order API는 **5.0.0에서 정착**(CHANGELOG 확인, 5.2.1까지 breaking 없음) — 이전에 "5.2.1"이라 적은 건 사용자가 '확인한 최신 버전'일 뿐 실제 최소가 아니었음. **PlayNANOO 영수증 검증은 SK1 필요** → v5에서 `StoreKitSelector.forceStoreKit1`은 **5.1+**라, PlayNANOO/SK1 용도는 **4.x 또는 5.1 이상**(5.0.x는 SK1 강제 불가; 샘플 `PlayNanooRuntimeBase`가 `#if UNITY_IAP_V5_1`로 가드 + 5.0.x 에러로그).

레거시(v4·SK1 전용) 프로젝트 지원이 사용자의 명시적 목표(2026-06-24). v4 엔진 파일(`BaseIAPFacadeV4`·`IAPFacadeV4`·`GooglePlayIAPFacadeV4`·`AppleIAPFacadeV4`, `#if !UNITY_IAP_V5`)은 **활성 지원 대상 — 삭제 금지, dormant 아님**.

**게이트 위치(중요):** IAP versionDefine은 **3개 모두 반드시 `Runtime/Unity/IAP/TrueBase.Unity.IAP.asmdef`** 에 둔다 — `TRUESOFT_IAP_AVAILABLE`(`com.unity.purchasing` `4.0.0`≥), `UNITY_IAP_V5`(`5.0.0`≥), `UNITY_IAP_V5_1`(`5.1.0`≥). 이 asmdef가 `defineConstraints: ["TRUESOFT_IAP_AVAILABLE"]`로 자기 컴파일을 게이트하고, v4/v5 코드 분기(`#if UNITY_IAP_V5`)도 이 어셈블리 안에서만 일어나기 때문. **Unity versionDefine은 asmdef 전용 + 어셈블리 경계를 넘지 않는다.** `TrueBase.Unity.asmdef`에 두면 IAP 어셈블리가 심볼을 못 받아 영영 컴파일 안 됨(2026-06-25 DefenceR에서 발견·수정).

**`package.json` versionDefines 금지(2026-06-30 회귀):** `UNITY_IAP_V5`/`UNITY_IAP_V5_1`이 한동안 **`package.json`의 `versionDefines` 블록**에 들어가 있었음. **Unity는 `package.json`의 versionDefines를 읽지 않는다(asmdef 전용 기능)** → 두 심볼이 **어디서도 정의 안 됨** → **IAP 5.x 프로젝트에서도 UNITY_IAP_V5가 안 잡혀 v4 코드 경로(`IStoreListener`·`ConfigurationBuilder`·`StandardPurchasingModule`)가 5.x 어셈블리에 컴파일 → 네임스페이스/타입 오류.** 사용자가 "v4 IAP 프로젝트 네임스페이스 오류"로 보고. **수정: package.json에서 제거하고 IAP asmdef로 이동.** package.json엔 versionDefines를 절대 두지 말 것.

**소비자(게임) #if 자동화:** 게임 코드의 `#if TRUESOFT_IAP_AVAILABLE`는 `Editor/IAPDefineSync.cs`(`[InitializeOnLoad]`)가 **전역 Scripting Define을 자동 설정**해 동작시킨다. IAP 어셈블리 존재 여부(`Type.GetType("TrueBase.Unity.SupabaseIAP, TrueBase.Unity.IAP")`)를 모든 BuildTargetGroup의 전역 define에 멱등 미러링. **프로젝트별 수동 설정 불필요**(간편 사용 원칙). 수동으로 asmdef/Scripting Define 추가하라고 안내하지 말 것 — 이 자동 스크립트가 처리함.

**어셈블리 이름이 4.x↔5.x에서 다름 + core/stores 2개로 분리(2026-06-30 ProjectNS_Android):** Unity IAP **5.0에서 어셈블리 이름이 `UnityEngine.Purchasing*` → `Unity.Purchasing*`로 변경**됨(네임스페이스 `UnityEngine.Purchasing`은 5.x에도 유지). 게다가 IAP는 **core/stores 2개 어셈블리로 분리**돼 있다:
- **4.x:** `UnityEngine.Purchasing`(core — `ConfigurationBuilder`·`UnityPurchasing`·`IStoreListener`·`Product`·`PurchaseEventArgs` 등) + `UnityEngine.Purchasing.Stores`(**`StandardPurchasingModule`**).
- **5.x:** `Unity.Purchasing`(core) + `Unity.Purchasing.Stores`.

`Unity.Purchasing`만 참조하면 4.x에서 `UnityEngine.Purchasing` 네임스페이스 자체를 못 찾고(CS0234), core만 참조하면 `StandardPurchasingModule`을 못 찾음(CS0103). **수정: asmdef `references`에 4개 모두 넣는다** — `Unity.Purchasing`·`Unity.Purchasing.Stores`(5.x)·`UnityEngine.Purchasing`·`UnityEngine.Purchasing.Stores`(4.x). 이름 기반 참조라 미존재 쪽은 Unity 2019+에서 경고만(비치명) — 버전마다 존재하는 짝이 연결됨. **4개 중 일부만 넣지 말 것.** (어셈블리 이름은 [needle-mirror/com.unity.purchasing] 태그 4.12.2·5.0.4 `Runtime/Purchasing`·`Runtime/Stores`에서 확인.)

`Unity.Services.Core`는 **Unity IAP 4.x도 `com.unity.services.core`에 의존하므로 v4에도 존재**(웹 확인) — 이 참조는 깨짐 원인 아님. **Unity 컴파일로 최종 확인 필요**(이 환경에선 빌드 불가).

**반환 타입 통일(2026-07-14):** `SupabaseIAP.Create*IAPAsync`는 `Task<SupabaseResult<TFacade>>`(성공 시 `.Data`=facade, 실패 시 사유), `BaseIAPFacade.InitializeAsync`(v4·v5 둘 다)는 `Task<SupabaseResult>` 반환. 실패 사유 상수 `IapProductIdsEmpty`·`IapDisposed`·`IapServicesInitFailed`·`IapInitTimeout`·`IapInitFailed`. **`Purchase()`(동기 bool)와 콜백(`onGrant Func<...,Task<bool>>`·`onFailed Action`)은 스토어 이벤트 모델이라 그대로 둠**(SupabaseResult로 안 바꿈 — 구매 결과는 콜백으로 옴, pending/resume 때문). 호출처: DefenceR `SupabaseManager.InitializeIAPAsync`·샘플 `SampleIAPScenarios`·문서 iap/usage·samples/examples·CLAUDE. DefenceR는 패키지 갱신 후 컴파일.

**문서·주석 표기 규칙:** **권장은 최신 버전(최소 5.1)** — 전체 기능(SK1 강제 포함). 최소 지원은 4.0.0(레거시 v4 호환). PlayNANOO/SK1은 "**4.x 또는 5.1 이상**(5.0.x SK1 불가)". **"5.2.1"·"5.0~5.2.0 미지원"으로 쓰지 말 것 — 틀림.** iOS는 SK2(v5, iOS15+) + SK1 폴백(v4 또는 v5 `forceStoreKit1` 5.1+, 검증함수 `purchase-verify-apple-legacy`). [[feedback_verify_before_asserting]]
