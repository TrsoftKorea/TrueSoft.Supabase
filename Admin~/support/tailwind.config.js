import base from '../tailwind.config.js'

// 콘솔과 같은 색 체계(밝은/어두운 화면 변수)를 쓰고, 훑는 파일만 고객센터 페이지로 바꾼다.
export default { ...base, content: ['./support/index.html', './support/src/**/*.{ts,tsx}'] }
