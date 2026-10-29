import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// 고객센터 창(게임 안 웹뷰로 띄우는 페이지). 콘솔과 다른 사이트(Cloudflare Pages truebase-support)로 올린다 —
// 같은 주소에 두면 운영자 브라우저에서 콘솔 로그인 정보와 저장 공간을 나눠 쓰게 된다.
// 가짜 데이터 모드(--mode mock)에서만 ?mock=1 로 서버 없이 띄울 수 있다. 일반 빌드에서는 그 코드가 빠진다.
export default defineConfig(({ mode }) => ({
  root: 'support',
  // 게임 목록(VITE_*_URL)은 콘솔과 같은 .env·배포 환경 변수에서 읽는다.
  envDir: '..',
  plugins: [react()],
  build: { outDir: mode === 'mock' ? '../dist-support-mock' : '../dist-support', emptyOutDir: true },
  preview: { port: 4175 },
  define: { __MOCK__: JSON.stringify(mode === 'mock') },
}))
