import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig(({ mode }) => ({
  plugins: [react()],
  server: { port: 5273 },
  // 가짜 데이터 모드(npm run preview:mock)에서만 true. 상수로 박혀 일반 빌드에서는 가짜 데이터 코드가 통째로 빠진다.
  define: { __MOCK__: JSON.stringify(mode === 'mock') },
}))
