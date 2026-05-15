# Edge Functions

Edge Function은 Supabase가 제공하는 서버리스 함수입니다. 확률 계산, 재화 검증, 영수증 검증처럼 **클라이언트를 신뢰할 수 없는 로직**을 서버에서 안전하게 실행할 때 사용합니다.

함수는 **Deno 런타임에서 실행되는 TypeScript 코드**로 작성합니다. Node.js와 문법이 비슷하지만 패키지 관리자 없이 URL로 모듈을 임포트하는 방식이 다릅니다.

---

## 함수 파일 구조 및 배포

Edge Function은 Unity 프로젝트 **외부**의 별도 디렉터리에서 관리합니다.

```
my-supabase/                     ← 별도 작업 폴더
└── supabase/
    └── functions/
        └── gacha-draw/
            └── index.ts
```

[Supabase CLI](https://supabase.com/docs/guides/cli)를 설치한 뒤 아래 명령으로 초기화하고 배포합니다.

```bash
supabase init          # supabase/ 폴더 생성 (최초 1회)
supabase login         # Supabase 계정 인증 (최초 1회)
supabase functions deploy gacha-draw
```

---

## 기본 사용법

> [!WARNING]
> Edge Function 호출에는 유효한 로그인 세션이 필요합니다.  
> JWT가 없거나 만료된 경우 `http_401` 오류가 반환됩니다.

```csharp
[Serializable]
public class GachaRequest  { public string bannerId; public int drawCount; }

[Serializable]
public class DrawReward    { public string id; public string rarity; }

[Serializable]
public class GachaResponse { public DrawReward[] rewards; public string serverTime; }

var result = await Supabase.TryInvokeFunctionAsync<GachaResponse>(
    "gacha-draw",
    new GachaRequest { bannerId = "normal_banner", drawCount = 10 }
);

if (result != null)
    ApplyRewards(result.rewards);
```

응답 루트는 JSON 객체(`{`로 시작)를 권장합니다.

---

## Edge Function 예시: 뽑기 결과 서버 계산

클라이언트는 요청만 보내고 확률·결과 계산은 서버에서 수행합니다.

`supabase/functions/gacha-draw/index.ts`:

```ts
import { createClient } from "jsr:@supabase/supabase-js@2";

type DrawRequest = { bannerId: string; drawCount: number; };

function pickItemByWeight(r: number) {
  if (r < 0.01) return { id: "legendary_001", rarity: "legendary" };
  if (r < 0.10) return { id: "epic_001",      rarity: "epic" };
  return           { id: "common_001",       rarity: "common" };
}

Deno.serve(async (req: Request) => {
  if (req.method !== "POST")
    return new Response(JSON.stringify({ error: "method_not_allowed" }), { status: 405 });

  const authHeader = req.headers.get("Authorization");
  if (!authHeader)
    return new Response(JSON.stringify({ error: "unauthorized" }), { status: 401 });

  const supabase = createClient(
    Deno.env.get("SUPABASE_URL")!,
    Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!,
    { global: { headers: { Authorization: authHeader } } }
  );

  const { data: userData, error } = await supabase.auth.getUser();
  if (error || !userData?.user)
    return new Response(JSON.stringify({ error: "invalid_user" }), { status: 401 });

  const { bannerId, drawCount: rawCount } = await req.json() as DrawRequest;
  const drawCount = Math.max(1, Math.min(rawCount ?? 1, 10));

  const now = Date.now();
  const rewards = Array.from({ length: drawCount }, (_, i) =>
    pickItemByWeight(((now + i * 7919) % 10000) / 10000)
  );

  return new Response(
    JSON.stringify({ bannerId, drawCount, rewards, serverTime: new Date().toISOString() }),
    { status: 200, headers: { "Content-Type": "application/json" } }
  );
});
```

위 구조대로 파일을 만든 뒤 배포합니다.

```bash
supabase functions deploy gacha-draw
```

---

## 401 Invalid JWT 디버깅

Unity에서 `http_401:body={"code":401,"message":"Invalid JWT"}`가 발생할 때, 아래 디버그 함수를 임시 배포해 원인을 확인합니다.

```ts
Deno.serve(async (req: Request) => {
  const authHeader = req.headers.get("Authorization");
  const token = authHeader?.startsWith("Bearer ") ? authHeader.slice(7).trim() : "";

  const debug = {
    hasAuthHeader: !!authHeader,
    hasBearer: authHeader?.startsWith("Bearer "),
    tokenSegments: token ? token.split(".").length : 0,  // 정상이면 3
  };

  if (!token || token.split(".").length !== 3)
    return new Response(JSON.stringify({ code: 401, debug }), { status: 401 });

  const supabase = createClient(
    Deno.env.get("SUPABASE_URL")!,
    Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!,
    { global: { headers: { Authorization: authHeader! } } }
  );

  const { error } = await supabase.auth.getUser(token);
  if (error)
    return new Response(JSON.stringify({ code: 401, debug, getUserError: error.message }), { status: 401 });

  // 정상 로직 ...
});
```

> [!NOTE]
> 원인 해결 후 `debug` 필드를 제거하세요.

---

## 보안 체크리스트

> [!IMPORTANT]
> 서버 함수는 클라이언트가 신뢰할 수 없다는 전제 하에 작성하세요.

- **확률·결과 계산은 서버에서만** 수행합니다. 클라이언트 값을 그대로 신뢰하지 마세요.
- **재화 차감·검증 로직**은 Edge Function 내부에서 처리합니다.
- **중복 요청 방지**를 위해 멱등 키 사용을 고려합니다. 클라이언트가 요청마다 고유한 `requestId`를 함께 보내고, 서버는 DB에서 해당 ID가 이미 처리됐는지 확인한 뒤 재처리를 막습니다.
- **뽑기 등 중요 이벤트**는 `gacha_draw_logs` 테이블에 `user_id`, `banner_id`, `rewards`, `created_at`을 기록하는 것을 권장합니다.
