function legacyCopy(text: string): boolean {
  const el = document.createElement('input')
  el.value = text
  el.style.cssText = 'position:fixed;top:0;left:0;width:1px;height:1px;opacity:0;'
  document.body.appendChild(el)
  el.focus()
  el.select()
  el.setSelectionRange(0, text.length)
  const ok = document.execCommand('copy')
  document.body.removeChild(el)
  return ok
}

export async function copyToClipboard(text: string): Promise<boolean> {
  if (navigator.clipboard?.writeText) {
    try {
      await navigator.clipboard.writeText(text)
      return true
    } catch {
      // fall through to legacy
    }
  }
  return legacyCopy(text)
}
