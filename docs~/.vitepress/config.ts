import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'TrueBase',
  description: 'TrueBase — Unity UPM Supabase SDK 문서',
  lang: 'ko-KR',
  base: '/TrueSoft.Supabase/',

  themeConfig: {
    nav: [
      { text: '가이드', link: '/guide/start/install' },
      { text: 'GitHub', link: 'https://github.com/trsoftkorea/TrueSoft.Supabase' }
    ],

    sidebar: [
      {
        text: '시작하기',
        link: '/guide/start/',
        items: [
          { text: '설치', link: '/guide/start/install' },
          { text: '프로젝트 생성', link: '/guide/start/project' },
          { text: '초기 설정', link: '/guide/start/init-setup' },
          { text: '데이터베이스 설정', link: '/guide/start/database-setup' },
          { text: '다음 단계', link: '/guide/start/next' }
        ]
      },
      {
        text: '인증',
        link: '/guide/auth/',
        items: [
          {
            text: '로그인',
            collapsed: false,
            items: [
              { text: '익명 로그인', link: '/guide/auth/anonymous' },
              { text: '자동 로그인', link: '/guide/auth/auto-login' },
              { text: '로그인 후 상태', link: '/guide/auth/session-state' },
              { text: '로그아웃', link: '/guide/auth/logout' },
              { text: '익명 계정 복구', link: '/guide/auth/recovery' },
              { text: '중복 로그인 감지', link: '/guide/auth/duplicate-login' },
              { text: '차단된 계정 처리', link: '/guide/auth/ban' }
            ]
          },
          {
            text: '소셜 로그인',
            collapsed: false,
            items: [
              {
                text: 'Google',
                link: '/guide/social/google/',
                collapsed: true,
                items: [
                  { text: '대시보드 설정', link: '/guide/social/google/setup' },
                  { text: '신규 로그인 · Android', link: '/guide/social/google/signin-android' },
                  { text: '신규 로그인 · iOS', link: '/guide/social/google/signin-ios' },
                  { text: '게스트 연동 · Android', link: '/guide/social/google/link-android' },
                  { text: '게스트 연동 · iOS', link: '/guide/social/google/link-ios' },
                  { text: '추가 연동 · Android', link: '/guide/social/google/add-android' },
                  { text: '추가 연동 · iOS', link: '/guide/social/google/add-ios' },
                  { text: '연동 해제', link: '/guide/social/google/unlink' }
                ]
              },
              {
                text: 'Apple',
                link: '/guide/social/apple/',
                collapsed: true,
                items: [
                  { text: '대시보드 설정', link: '/guide/social/apple/setup' },
                  { text: '신규 로그인', link: '/guide/social/apple/signin' },
                  { text: '신규 로그인 · 커스텀', link: '/guide/social/apple/signin-token' },
                  { text: '게스트 연동', link: '/guide/social/apple/link' },
                  { text: '게스트 연동 · 커스텀', link: '/guide/social/apple/link-token' },
                  { text: '추가 연동', link: '/guide/social/apple/add' },
                  { text: '추가 연동 · 커스텀', link: '/guide/social/apple/add-token' },
                  { text: '연동 해제', link: '/guide/social/apple/unlink' }
                ]
              }
            ]
          }
        ]
      },
      {
        text: '계정',
        link: '/guide/account/',
        items: [
          {
            text: '닉네임',
            link: '/guide/display-name/nickname/',
            collapsed: true,
            items: [
              { text: '중복 확인', link: '/guide/display-name/nickname/check' },
              { text: '닉네임 설정', link: '/guide/display-name/nickname/set' },
              { text: '다른 플레이어 조회', link: '/guide/display-name/nickname/get-other' }
            ]
          },
          { text: '공개 프로필', link: '/guide/display-name/profile' },
          {
            text: '탈퇴 처리',
            link: '/guide/withdrawal/',
            collapsed: true,
            items: [
              { text: '탈퇴 신청', link: '/guide/withdrawal/submit' },
              { text: '예약 상태 확인', link: '/guide/withdrawal/status' },
              { text: '탈퇴 취소', link: '/guide/withdrawal/cancel' }
            ]
          },
          { text: '서버 정보', link: '/guide/withdrawal/server-info' }
        ]
      },
      {
        text: '게임 데이터',
        link: '/guide/game-data/',
        items: [
          {
            text: '유저 데이터',
            link: '/guide/user-data/',
            collapsed: false,
            items: [
              { text: '개요', link: '/guide/user-data/how-it-works' },
              { text: '클래스 생성', link: '/guide/user-data/class-gen' },
              { text: '로드', link: '/guide/user-data/load' },
              { text: '읽기 / 쓰기', link: '/guide/user-data/read-write' },
              { text: '즉시 저장 후 대기', link: '/guide/user-data/save-now' },
              { text: '즉시 저장 요청', link: '/guide/user-data/request-save' },
              { text: '변경분 저장', link: '/guide/user-data/save-if-changed' },
              { text: '세이브 삭제', link: '/guide/user-data/delete' }
            ]
          },
          {
            text: '데이터 타입',
            link: '/guide/data-types/',
            collapsed: true,
            items: [
              { text: '지원 타입', link: '/guide/data-types/supported' },
              { text: '자동 확장 컬렉션', link: '/guide/data-types/auto-collections' },
              { text: '커스텀 클래스 요소', link: '/guide/data-types/custom-class' }
            ]
          },
          {
            text: '원격 설정',
            link: '/guide/remote-config/',
            collapsed: true,
            items: [
              { text: '개요', link: '/guide/remote-config/how-it-works' },
              { text: '빠른 시작', link: '/guide/remote-config/quickstart' },
              { text: 'Reader', link: '/guide/remote-config/reader' },
              { text: 'Binding', link: '/guide/remote-config/binding' },
              { text: 'Listener', link: '/guide/remote-config/listener' },
              { text: '더 알아보기', link: '/guide/remote-config/advanced' }
            ]
          }
        ]
      },
      {
        text: '서비스',
        items: [
          {
            text: '인앱 결제',
            link: '/guide/iap/',
            collapsed: true,
            items: [
              { text: '사전 준비', link: '/guide/iap/prepare' },
              { text: '사용법', link: '/guide/iap/usage' },
              { text: '알아둘 점', link: '/guide/iap/notes' },
              { text: '더 알아보기', link: '/guide/iap/advanced' },
              { text: '버전별 차이', link: '/guide/iap/versions' }
            ]
          },
          {
            text: '우편함',
            link: '/guide/mailbox/',
            collapsed: true,
            items: [
              { text: '목록 조회', link: '/guide/mailbox/list' },
              { text: '다국어 메시지', link: '/guide/mailbox/localized' },
              { text: '상세 조회', link: '/guide/mailbox/detail' },
              { text: '아이템 핸들러 등록', link: '/guide/mailbox/item-handler' },
              { text: '보상 수령', link: '/guide/mailbox/claim' },
              { text: '전체 수령', link: '/guide/mailbox/claim-all' },
              { text: '삭제', link: '/guide/mailbox/delete' },
              { text: '수령한 우편 일괄 삭제', link: '/guide/mailbox/delete-claimed' },
              { text: '미수령 수', link: '/guide/mailbox/counts-unclaimed' },
              { text: '분류별 전체 현황', link: '/guide/mailbox/counts-detail' }
            ]
          },
          {
            text: '리더보드',
            link: '/guide/leaderboard/',
            collapsed: true,
            items: [
              { text: '목록 조회', link: '/guide/leaderboard/tables' },
              { text: '리더보드 조회', link: '/guide/leaderboard/table' },
              { text: '점수 기록', link: '/guide/leaderboard/submit' },
              { text: '점수 기록 · 생성 클래스', link: '/guide/leaderboard/submit-row' },
              { text: '순위 조회', link: '/guide/leaderboard/range' },
              { text: '플레이어 순위 조회', link: '/guide/leaderboard/player' },
              { text: '추가 데이터 수정', link: '/guide/leaderboard/set-player-data' },
              { text: '본인 기록 삭제', link: '/guide/leaderboard/delete-my-score' },
              { text: '리더보드 클래스와 필드', link: '/guide/leaderboard/columns' }
            ]
          },
          {
            text: '쿠폰',
            link: '/guide/coupon/',
            collapsed: true,
            items: [
              { text: '쿠폰 사용', link: '/guide/coupon/redeem' }
            ]
          },
          {
            text: '채팅',
            link: '/guide/chat/',
            collapsed: true,
            items: [
              { text: '채널 목록 조회', link: '/guide/chat/channels' },
              { text: '채널 구독', link: '/guide/chat/subscribe' },
              { text: '메시지 발송', link: '/guide/chat/send' }
            ]
          },
          { text: '서버 시간', link: '/guide/server-time' }
        ]
      },
      {
        text: '서비스 계정 JSON 발급',
        link: '/guide/google-service-account/',
        collapsed: true,
        items: [
          { text: '발급 절차', link: '/guide/google-service-account/issue' },
          { text: '콘솔 등록', link: '/guide/google-service-account/console' }
        ]
      },
      {
        text: '플레이나누 이관',
        link: '/guide/migration/',
        collapsed: true,
        items: [
          { text: '개요', link: '/guide/migration/how-it-works' },
          { text: '설치', link: '/guide/migration/install' },
          { text: '로그인', link: '/guide/migration/login' },
          { text: '탈퇴 취소', link: '/guide/migration/withdrawal' },
          { text: '인앱 결제', link: '/guide/migration/iap' },
          { text: '데이터 동기화', link: '/guide/migration/sync' },
          { text: '플레이나누 제거 후', link: '/guide/migration/after' }
        ]
      },
      {
        text: 'API',
        link: '/guide/api/',
        items: [
          { text: '인증', link: '/guide/api/auth' },
          { text: '계정', link: '/guide/api/account' },
          { text: '게임 데이터', link: '/guide/api/game-data' },
          { text: '인앱 결제', link: '/guide/api/iap' },
          { text: '우편함', link: '/guide/api/mailbox' },
          { text: '리더보드', link: '/guide/api/leaderboard' },
          { text: '쿠폰', link: '/guide/api/coupon' },
          { text: '채팅', link: '/guide/api/chat' },
          { text: '기타', link: '/guide/api/etc' },
          { text: '에러 코드', link: '/guide/api/fail-reasons' }
        ]
      },
      {
        text: '부록',
        collapsed: true,
        items: [
          { text: '예제', link: '/guide/samples/examples' },
          { text: 'Apple 설정 상세', link: '/guide/social/apple/setup-details' }
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
