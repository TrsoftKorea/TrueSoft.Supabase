import { getTarget } from '../../lib/projectTarget'
import { useProjectLook } from '../../lib/projectLook'

/**
 * 창 안에 "대상 게임: ○○"을 게임 색으로 보여 준다. 되돌리기 어려운 작업을 엉뚱한 게임에 하지 않도록
 * 확인 창과 값을 바로 저장하는 창에 넣는다. 게임은 이 탭이 열고 있는 게임이다.
 */
export function TargetGameStrip() {
  const look = useProjectLook()
  const target = getTarget()
  return (
    <div className="rounded-md px-3 py-2 text-sm font-medium text-white" style={{ backgroundColor: look.color(target) }}>
      대상 게임: {look.name(target)}
    </div>
  )
}
