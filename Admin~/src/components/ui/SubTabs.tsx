import { useSearchParams } from 'react-router-dom'
import { Segmented } from './Segmented'

type TabList<T extends string> = readonly (readonly [T, string])[]

/**
 * 큰 탭(`Tabs`) 바로 아래 둘째 줄 탭 — 한 화면에 안 들어가는 큰 탭 안을 카드 묶음으로 가른다.
 * 한 화면에 들어가는 탭에는 두지 않는다. 값은 `useSubTab` 으로 주소 `?sub=` 에 둔다.
 */
export function SubTabs<T extends string>({ value, onChange, tabs }: { value: T; onChange: (v: T) => void; tabs: TabList<T> }) {
  return <Segmented value={value} onChange={onChange} options={tabs.map(([v, label]) => ({ v, label }))} />
}

/** 주소의 `sub` 를 읽고 쓴다. 첫 탭이면 주소에서 뺀다. 큰 탭을 바꿀 때는 그쪽에서 `sub` 를 지운다. */
export function useSubTab<T extends string>(tabs: TabList<T>): [T, (v: T) => void] {
  const [params, setParams] = useSearchParams()
  const first = tabs[0]![0]
  const raw = params.get('sub')
  const value = tabs.find(([v]) => v === raw)?.[0] ?? first
  const set = (v: T) => setParams((p) => {
    const next = new URLSearchParams(p)
    if (v === first) next.delete('sub')
    else next.set('sub', v)
    return next
  }, { replace: true })
  return [value, set]
}
