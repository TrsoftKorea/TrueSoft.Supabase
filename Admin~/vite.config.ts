import { defineConfig, type Plugin } from 'vite'
import react from '@vitejs/plugin-react'

// 배포 판에 어느 커밋으로 만든 건지 남긴다(version.json). 미리보기의 게시 띠가 실제 콘솔이 최신인지 이걸로 본다.
// 커밋 값은 배포 작업(admin-deploy.yml)이 VITE_BUILD_SHA 로 넘긴다 — 없으면(로컬 빌드) 만들지 않는다.
function buildVersion(): Plugin {
  return {
    name: 'build-version',
    generateBundle() {
      const sha = process.env.VITE_BUILD_SHA
      if (!sha) return
      this.emitFile({ type: 'asset', fileName: 'version.json', source: JSON.stringify({ sha, builtAt: new Date().toISOString() }) })
    },
  }
}

export default defineConfig(({ mode }) => ({
  plugins: [react(), buildVersion()],
  server: { port: 5273 },
  // 가짜 데이터 모드(npm run preview:mock)에서만 true. 상수로 박혀 일반 빌드에서는 가짜 데이터 코드가 통째로 빠진다.
  define: { __MOCK__: JSON.stringify(mode === 'mock') },
}))
