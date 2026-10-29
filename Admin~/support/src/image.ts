// 붙일 그림을 줄여 보낸다 — 휴대폰 스크린샷은 원본이 수 MB 라 그대로 보내면 느리고 저장소 한도(5MB)에 걸린다.
// 긴 변 1600px 의 JPEG 로 바꾼다. 서버는 PNG·JPG·WEBP 만 받는다.
const MAX_SIDE = 1600

export async function shrinkImage(file: File): Promise<{ contentType: string; base64: string; previewUrl: string }> {
  const bitmap = await createImageBitmap(file)
  const scale = Math.min(1, MAX_SIDE / Math.max(bitmap.width, bitmap.height))
  const canvas = document.createElement('canvas')
  canvas.width = Math.max(1, Math.round(bitmap.width * scale))
  canvas.height = Math.max(1, Math.round(bitmap.height * scale))
  const ctx = canvas.getContext('2d')
  if (!ctx) throw new Error('canvas')
  ctx.fillStyle = '#ffffff'
  ctx.fillRect(0, 0, canvas.width, canvas.height)
  ctx.drawImage(bitmap, 0, 0, canvas.width, canvas.height)
  bitmap.close()
  const dataUrl = canvas.toDataURL('image/jpeg', 0.85)
  return { contentType: 'image/jpeg', base64: dataUrl.slice(dataUrl.indexOf(',') + 1), previewUrl: dataUrl }
}
