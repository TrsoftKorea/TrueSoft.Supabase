import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'Truesoft Supabase SDK',
  description: 'Unity UPM Supabase SDK 문서',
  lang: 'ko-KR',
  base: '/TrueSoft.Supabase/',

  themeConfig: {
    nav: [
      { text: '가이드', link: '/guide/getting-started' },
      { text: 'GitHub', link: 'https://github.com/trsoftkorea/TrueSoft.Supabase' }
    ],

    sidebar: [
      {
        text: '시작하기',
        items: [
          { text: '빠른 시작', link: '/guide/getting-started' },
          { text: '샘플', link: '/guide/samples' }
        ]
      },
      {
        text: '인증',
        items: [
          { text: '로그인', link: '/guide/auth' },
          { text: '소셜 로그인', link: '/guide/social-login' }
        ]
      },
      {
        text: '계정',
        items: [
          { text: '닉네임 · 프로필', link: '/guide/display-name' },
          { text: '탈퇴 · 서버 이주', link: '/guide/withdrawal' }
        ]
      },
      {
        text: '게임 데이터',
        items: [
          { text: '유저 데이터', link: '/guide/user-data' },
          { text: 'Remote Config', link: '/guide/remote-config' }
        ]
      },
      {
        text: '서비스',
        items: [
          { text: '인앱 결제 (IAP)', link: '/guide/iap' }
        ]
      },
      {
        text: '이관',
        items: [
          { text: 'PlayNanoo 이관', link: '/guide/playnanoo-migration' }
        ]
      }
    ],

    outline: { level: [2, 3], label: '이 페이지' },

    search: { provider: 'local' },

    socialLinks: [
      { icon: 'github', link: 'https://github.com/trsoftkorea/TrueSoft.Supabase' }
    ]
  }
})
