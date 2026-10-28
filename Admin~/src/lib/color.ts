// 색 계산 — 게임 색의 대비·거리와, 어두운 화면용으로 누그러뜨린 색.

export function rgbOf(hex: string): [number, number, number] {
  return [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16)) as [number, number, number]
}

/** WCAG 대비 — 흰 글자와 이 색 바탕. */
export function contrastWithWhite(hex: string): number {
  const lum = rgbOf(hex)
    .map((v) => v / 255)
    .map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4))
  const l = 0.2126 * lum[0]! + 0.7152 * lum[1]! + 0.0722 * lum[2]!
  return 1.05 / (l + 0.05)
}

export function colorDistance(a: string, b: string): number {
  const [x, y] = [rgbOf(a), rgbOf(b)]
  return Math.sqrt(x.reduce((s, v, i) => s + (v - y[i]!) ** 2, 0))
}

function toHsl([r, g, b]: [number, number, number]): [number, number, number] {
  const [R, G, B] = [r / 255, g / 255, b / 255]
  const max = Math.max(R, G, B)
  const min = Math.min(R, G, B)
  const l = (max + min) / 2
  if (max === min) return [0, 0, l]
  const d = max - min
  const s = l > 0.5 ? d / (2 - max - min) : d / (max + min)
  const h = max === R ? (G - B) / d + (G < B ? 6 : 0) : max === G ? (B - R) / d + 2 : (R - G) / d + 4
  return [h * 60, s, l]
}

function toHex([h, s, l]: [number, number, number]): string {
  const k = (n: number) => (n + h / 30) % 12
  const a = s * Math.min(l, 1 - l)
  const f = (n: number) => l - a * Math.max(-1, Math.min(k(n) - 3, 9 - k(n), 1))
  return '#' + [f(0), f(8), f(4)].map((v) => Math.round(v * 255).toString(16).padStart(2, '0')).join('')
}

const HEX_RE = /^#[0-9a-f]{6}$/i

/**
 * 어두운 화면용 게임 색. 밝은 화면에 맞춰 고른 진한 색은 어두운 바탕 위에서 눈이 시리게 튄다 —
 * 채도를 덜고 밝기를 낮춰 가라앉힌다. 색 계열(색상)은 그대로라 어느 게임인지는 그대로 알아본다.
 * 흰 글자가 얹히므로 흰색과 대비 4.5:1 이상이 될 때까지 더 어둡게 한다.
 */
export function darkVariant(hex: string): string {
  if (!HEX_RE.test(hex)) return hex
  const [h, s, l] = toHsl(rgbOf(hex.toLowerCase()))
  let next: [number, number, number] = [h, s * 0.6, Math.min(l, 0.5) * 0.82]
  for (let i = 0; i < 20 && contrastWithWhite(toHex(next)) < 4.5; i++) next = [h, next[1], next[2] - 0.02]
  return toHex(next)
}
