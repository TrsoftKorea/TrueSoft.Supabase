import base from '../tailwind.config.js'

// 콘솔과 같은 색 체계(밝은/어두운 화면 변수)를 쓰고, 훑는 파일만 고객센터 페이지로 바꾼다.
// 약관 본문 부품(Markdown)은 콘솔 미리보기와 같은 파일을 쓰므로 함께 훑는다.
export default { ...base, content: ['./support/index.html', './support/src/**/*.{ts,tsx}', './src/components/ui/Markdown.tsx'] }
