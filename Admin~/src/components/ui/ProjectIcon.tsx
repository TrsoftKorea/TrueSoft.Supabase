import type { ProjectTarget } from '../../lib/projectTarget'
import { useProjectLook } from '../../lib/projectLook'

/** 게임 아이콘. 그림을 정하지 않았으면 게임 색 바탕에 이름 첫 글자. */
export function ProjectIcon({
  target,
  size = 40,
  dim = false,
  className = '',
}: {
  target: ProjectTarget
  size?: number
  dim?: boolean
  className?: string
}) {
  const look = useProjectLook()
  const icon = look.icon(target)
  const style = { width: size, height: size, opacity: dim ? 0.5 : 1 }
  const radius = size >= 32 ? 'rounded-lg' : 'rounded-md'

  if (icon) {
    return <img src={icon} alt="" style={style} className={`${radius} shrink-0 object-cover ${className}`} />
  }
  return (
    <span
      style={{ ...style, backgroundColor: look.color(target), fontSize: Math.round(size * 0.42) }}
      className={`${radius} shrink-0 inline-flex items-center justify-center text-white font-bold ${className}`}
    >
      {look.name(target).slice(0, 1)}
    </span>
  )
}
