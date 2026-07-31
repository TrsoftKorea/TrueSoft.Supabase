---
name: project_remoteconfig_type_set
description: "RemoteConfig Retool '항목 추가' 타입 드롭다운은 8종으로 한정 (12종 전체 아님)"
metadata: 
  node_type: memory
  type: project
  originSessionId: 30cd5382-d7b4-4ee1-9991-960d6026619f
---

RemoteConfig 관리(Retool "트루베이스" 앱, `defence-r`)의 "항목 추가" 타입 드롭다운은 **8종으로 한정**한다: `string, int, long, bool, float, double, DateTime, json`.

SDK 생성기(`GeneratorTypeCatalog.TypeOptions`)는 스칼라 12종(+ short/ulong/DateTimeOffset/DateOnly/TimeOnly)을 `__meta`로 전부 인식하지만, **RemoteConfig 설정값엔 short·ulong·날짜 세분 타입이 거의 안 쓰여** UI를 줄였다. SDK는 12종 전부 받으므로 필요 시 `__meta`를 수동으로 다른 타입으로 넣어도 동작한다.

타입은 `remote_config.value_json` 안 `__meta` 객체에 키별로 저장된다(별도 컬럼/테이블 아님). `addRemoteConfigItem.ts`가 항목 추가 시 자동 기록하고, SDK strip + 생성기가 읽는다. [[project_retool_resource_bindings]]

**적용 시 주의:** 다음에 이 드롭다운을 다룰 때 12/13종으로 도로 늘리지 말 것. 사용자가 명시적으로 8종으로 축소를 지시함(2026-06-24).
