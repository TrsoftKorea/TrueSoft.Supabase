// 색은 src/theme.css 의 변수에서 만든다 — 밝은/어두운 화면이 같은 클래스 이름을 쓰고 변수만 바뀐다.
const STEPS = ['50', '100', '200', '300', '400', '500', '600', '700', '800', '900', '950']
const themed = (family) =>
  Object.fromEntries(STEPS.map((s) => [s, `rgb(var(--${family}-${s}) / <alpha-value>)`]))

/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  darkMode: 'class',
  theme: {
    extend: {
      colors: {
        neutral: themed('neutral'),
        red: themed('red'),
        amber: themed('amber'),
        emerald: themed('emerald'),
        green: themed('green'),
        blue: themed('blue'),
        // 선택된 행·칩의 옅은 파란 바탕
        'brand-soft': 'rgb(var(--brand-soft) / <alpha-value>)',
        // 흰 카드 바탕. text-white(색 바탕 위 흰 글자)와 갈라 두어야 어두운 화면에서 글자가 같이 어두워지지 않는다.
        surface: 'rgb(var(--surface) / <alpha-value>)',
      },
    },
  },
  plugins: [],
}
