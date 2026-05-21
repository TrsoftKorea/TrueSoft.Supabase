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
        text: '기능 가이드',
        items: [
          { text: '인증', link: '/guide/auth' },
          { text: '유저 데이터', link: '/guide/user-data' },
          { text: 'Remote Config', link: '/guide/remote-config' },
          { text: '인앱 결제 (IAP)', link: '/guide/iap' },
          { text: '계정', link: '/guide/account' },
          { text: '데이터 스키마', link: '/guide/data-schema' }
        ]
      }
    ],

    search: { provider: 'local' },

    socialLinks: [
      { icon: 'github', link: 'https://github.com/trsoftkorea/TrueSoft.Supabase' }
    ]
  }
})
