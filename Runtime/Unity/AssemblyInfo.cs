using System.Runtime.CompilerServices;

// IAP 어셈블리는 별개이지만 SDK 내부 구현이므로, 게임 코드에 숨긴 internal API에 접근할 수 있어야 합니다.
[assembly: InternalsVisibleTo("TrueBase.Unity.IAP")]
