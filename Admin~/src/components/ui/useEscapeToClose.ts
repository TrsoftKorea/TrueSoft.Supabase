import { useEffect, useRef } from 'react'

// 열려 있는 창의 순서. Esc 는 맨 위 창 하나만 닫는다 — 편집 창 위에 확인 창이 떴을 때 둘 다 닫히지 않게.
const openStack: symbol[] = []

/** 창이 열려 있는 동안 Esc 로 닫는다. busy(저장 중)에는 닫지 않는다 — 요청은 나갔는데 창만 사라지면 결과를 못 본다. */
export function useEscapeToClose(open: boolean, busy: boolean, onClose: () => void) {
  // 값이 바뀔 때마다 다시 등록하면 쌓인 순서가 뒤바뀌므로 최신 값은 ref 로 읽는다.
  const busyRef = useRef(busy)
  const onCloseRef = useRef(onClose)
  busyRef.current = busy
  onCloseRef.current = onClose

  useEffect(() => {
    if (!open) return
    const id = Symbol('dialog')
    openStack.push(id)
    const onKey = (e: KeyboardEvent) => {
      // 한글 조합 중 Esc 는 조합 취소일 뿐이다 — 쓰던 값과 함께 창을 닫지 않는다.
      if (e.isComposing || e.keyCode === 229) return
      if (e.key !== 'Escape' || openStack[openStack.length - 1] !== id || busyRef.current) return
      onCloseRef.current()
    }
    window.addEventListener('keydown', onKey)
    return () => {
      window.removeEventListener('keydown', onKey)
      const at = openStack.indexOf(id)
      if (at >= 0) openStack.splice(at, 1)
    }
  }, [open])
}
