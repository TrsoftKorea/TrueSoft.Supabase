#!/usr/bin/env bash
# 플레이어 조건 컴파일 체크 — 사용법·이유는 README.md.
#
# 유니티 에디터 컴파일은 빌드 대상이 iOS 여도 UNITY_EDITOR 가 켜져 `#if UNITY_IOS && !UNITY_EDITOR` 같은
# 플랫폼 전용 코드를 건너뛴다. 그래서 SDK Host 가 남긴 컴파일 인자(Library/Bee)에서 에디터 기호만 빼고
# 유니티 번들 Roslyn 으로 직접 컴파일한다.
set -u

REPO="$(cygpath -m "$(cd "$(dirname "$0")/../.." && pwd)")"
SDK_HOST="${SDK_HOST:-D:/Project/SDK Host}"
NANOO_DLL="${NANOO_DLL:-D:/Project/ProjectNS_Android/Assets/PlayNANOO/PlayNANOOAndroid.dll}"
# csc 는 윈도 경로만 안다 — Git Bash 의 /tmp 꼴 경로를 넘기면 쓰기에 실패한다.
OUT="$(cygpath -m "$(cd "$(dirname "$0")" && pwd)")/out"

fail() { echo "오류: $*" >&2; exit 2; }

[ -d "$SDK_HOST/Library/Bee/artifacts" ] || fail "SDK Host 컴파일 산출물이 없습니다: $SDK_HOST/Library/Bee/artifacts — SDK Host 를 한 번 열어 컴파일하세요."
VER=$(sed -n 's/^m_EditorVersion: //p' "$SDK_HOST/ProjectSettings/ProjectVersion.txt" | tr -d '\r')
EDITOR="C:/Program Files/Unity/$VER/Editor"
DOTNET="$EDITOR/Data/NetCoreRuntime/dotnet.exe"
CSC="$EDITOR/Data/DotNetSdkRoslyn/csc.dll"
[ -f "$CSC" ] || fail "유니티 $VER 의 컴파일러를 찾지 못했습니다: $CSC"

# Android 대상으로 컴파일된 dag 를 고른다(iOS 판은 거기서 기호만 바꿔 만든다).
DAG=""
for d in "$SDK_HOST"/Library/Bee/artifacts/*.dag; do
  [ -f "$d/TrueBase.Unity.rsp" ] && grep -q '^-define:UNITY_ANDROID$' "$d/TrueBase.Unity.rsp" && DAG="$d" && break
done
[ -n "$DAG" ] || fail "Android 대상 컴파일 인자가 없습니다 — SDK Host 의 빌드 대상을 Android 로 두고 한 번 컴파일하세요."

rm -rf "$OUT"; mkdir -p "$OUT/and" "$OUT/ios" "$OUT/sample"
cd "$SDK_HOST" || fail "SDK Host 로 이동 실패"   # rsp 안의 Library/... 상대 경로 기준

ERRORS=0
strip() { grep -v "^-define:UNITY_EDITOR\|^/additionalfile:\|^-out:\|^-refout:\|TrueBase.Core.ref.dll\|TrueBase.Unity.ref.dll" "$1"; }
to_ios() { grep -v "^-define:.*ANDROID"; echo "-define:UNITY_IOS"; echo "-define:PLATFORM_IOS"; }
run() {
  "$DOTNET" exec "$CSC" "@$1" > "$1.out" 2>&1
  local e=$? errs; errs=$(grep -c 'error CS' "$1.out")
  printf '%-16s %s  오류 %s  경고 %s\n' "$2" "$([ $e -eq 0 ] && echo 통과 || echo 실패)" "$errs" "$(grep -c 'warning CS' "$1.out")"
  grep -E "error CS|warning CS" "$1.out" | sed 's#.*TrueSoft.Supabase.##' | head -8 | sed 's/^/    /'
  [ $e -eq 0 ] || ERRORS=$((ERRORS + 1))
}
# 대조군: 기호가 정말 바뀌었는지 — 플랫폼 전용 멤버 이름이 결과물 메타데이터에 있는지/없는지 본다.
expect() {
  local has; has=$(grep -c -a "$2" "$1" 2>/dev/null)
  if { [ "$3" = yes ] && [ "$has" -gt 0 ]; } || { [ "$3" = no ] && [ "$has" -eq 0 ]; }; then return 0; fi
  echo "    대조 실패: $(basename "$(dirname "$1")")/$(basename "$1") 에 '$2' 가 $([ "$3" = yes ] && echo 없음 || echo 있음) — 플랫폼 기호가 기대대로 안 걸렸다"
  ERRORS=$((ERRORS + 1))
}

{ strip "$DAG/TrueBase.Core.rsp"; echo "-out:\"$OUT/TrueBase.Core.dll\""; echo "-refout:\"$OUT/TrueBase.Core.ref.dll\""; } > "$OUT/core.rsp"
run "$OUT/core.rsp" "Core"

for p in and ios; do
  label=$([ $p = and ] && echo Android || echo iOS)
  { if [ $p = ios ]; then strip "$DAG/TrueBase.Unity.rsp" | to_ios; else strip "$DAG/TrueBase.Unity.rsp"; fi
    echo "-out:\"$OUT/$p/TrueBase.Unity.dll\""; echo "-refout:\"$OUT/$p/TrueBase.Unity.ref.dll\""; echo "-r:\"$OUT/TrueBase.Core.ref.dll\""; } > "$OUT/unity-$p.rsp"
  run "$OUT/unity-$p.rsp" "Unity $label"

  if [ -f "$DAG/TrueBase.Unity.IAP.rsp" ]; then
    { if [ $p = ios ]; then strip "$DAG/TrueBase.Unity.IAP.rsp" | to_ios; else strip "$DAG/TrueBase.Unity.IAP.rsp"; fi
      echo "-out:\"$OUT/$p/TrueBase.Unity.IAP.dll\""; echo "-r:\"$OUT/TrueBase.Core.ref.dll\""; echo "-r:\"$OUT/$p/TrueBase.Unity.ref.dll\""; } > "$OUT/iap-$p.rsp"
    run "$OUT/iap-$p.rsp" "IAP $label"
  else
    echo "IAP $label       건너뜀 — SDK Host 에 com.unity.purchasing 5.x 가 없어 IAP 어셈블리 인자가 없음"
  fi
done

# 대조: iOS 판에만 iOS 전용 코드가 들어가야 한다. 기호 교체는 모든 어셈블리에 같은 방식으로 걸리므로 한 쌍이면 된다.
# 표지는 "#if UNITY_IOS && !UNITY_EDITOR" 안에만 있는 멤버여야 한다 — 다른 곳에서도 쓰는 이름(예: jwsRepresentation 은
# 플랫폼 분기 없는 AppleIAPFacade 에도 있다)을 고르면 대조가 거짓 실패한다.
expect "$OUT/ios/TrueBase.Unity.dll" "StartIos" yes   # GoogleLoginBridge 의 iOS 전용 메서드
expect "$OUT/and/TrueBase.Unity.dll" "StartIos" no

# PlayNanoo 샘플: Samples~ 는 SDK Host 가 컴파일하지 않으므로 여기서 본다. 나누 DLL 이 있을 때만.
if [ -f "$NANOO_DLL" ]; then
  for v in and editor; do
    label=$([ $v = and ] && echo Android || echo 에디터)
    { strip "$DAG/TrueBase.Unity.rsp" | grep -v '\.cs"$'
      [ $v = editor ] && echo "-define:UNITY_EDITOR"
      echo "-out:\"$OUT/sample/PlayNanoo-$v.dll\""; echo "-r:\"$OUT/TrueBase.Core.dll\""
      echo "-r:\"$OUT/and/TrueBase.Unity.dll\""; echo "-r:\"$NANOO_DLL\""
      for f in "$REPO"/Samples~/PlayNanooMigration/*.cs; do echo "\"$f\""; done; } > "$OUT/sample-$v.rsp"
    run "$OUT/sample-$v.rsp" "나누샘플 $label"
  done
else
  echo "나누샘플        건너뜀 — PlayNANOO DLL 없음($NANOO_DLL). NANOO_DLL 로 경로를 지정할 수 있음"
fi

echo
[ $ERRORS -eq 0 ] && echo "결과: OK" || echo "결과: 실패 — $ERRORS 건"
exit $([ $ERRORS -eq 0 ] && echo 0 || echo 1)
