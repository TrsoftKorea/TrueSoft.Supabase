import { useCallback, useEffect, useRef, useState } from 'react'
import { callAdmin, NotAuthenticatedError } from '../lib/api'
import { getProject, PROJECTS, type ProjectTarget } from '../lib/projectTarget'
import { setProjectLook, toProjectLook, useProjectLook } from '../lib/projectLook'
import { ProjectIcon } from '../components/ui/ProjectIcon'
import { PageHeader } from '../components/ui/PageHeader'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { CardHeader, GroupBox, WhiteCard } from '../components/ui/Card'
import { Segmented } from '../components/ui/Segmented'
import { Tabs } from '../components/ui/Tabs'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { EditDialog } from '../components/ui/EditDialog'
import { TargetGameStrip } from '../components/ui/TargetGameStrip'
import { Button } from '../components/ui/Button'
import { Switch } from '../components/ui/Switch'
import { formatDateTime } from '../components/ui/format'
import { colorDistance, contrastWithWhite } from '../lib/color'
import { forgetGameTimeZone, TIME_ZONES, timeZoneName, type GameSettings } from '../lib/gameTime'
import { ChevronDown, ChevronUp, CircleHelp, ExternalLink, Loader2, Pencil, Plus, RefreshCw, Trash2, Unlock } from 'lucide-react'
import { LimitsForm, type LimitField, type LimitToggle } from './social/LimitsForm'
import PushSettings from './PushSettings'
import { TableStatusRow } from '../components/ui/TableStatusRow'
import { areaCls, inputCls } from '../components/ui/Input'

type StoreKeyStatus = {
  key: string
  source: 'console' | 'supabase' | null
  status: 'ok' | 'missing' | 'invalid'
  updatedAt: string | null
  updatedBy: string | null
}

// 화면에는 이 값이 없으면 무엇이 안 되는지만 적는다. 키 목록은 admin-api SERVER_CONFIG_VALID ·
// install.sql ts_server_config_keys() 와 같아야 한다(SdkAudit R16 이 대조).
/**
 * 값을 가져오는 방법. 메뉴·버튼·항목 이름은 운영자가 보는 한국어 화면의 표기를 따옴표로 적는다.
 * App Store Connect·Google Cloud 콘솔·Play Console 은 한국어 도움말(developer.apple.com/kr/help,
 * docs.cloud.google.com ?hl=ko, Play Console 고객센터 hl=ko)의 표기로 2026-10-28 확인했다.
 * Apple Developer 의 키 화면은 한국어 도움말도 버튼을 영어로 적어("Keys(키)", "Continue(계속)") 영어 그대로 둔다.
 * 앱 내 구입 키 화면의 "Issuer ID"(영어 그대로)·"키 ID" 와, Apple Developer 화면이 영어라는 것은 운영자가 화면으로 확인했다.
 * 줄 이름(name)은 운영자 요청으로 키 묶음 안에서 영어로 통일했다(Issuer ID · Key ID · Private key) — 단계 안의
 * 따옴표 글자는 그 화면에 보이는 그대로 둔다.
 * 확인 못 한 것 — 화면이 다르면 여기를 고친다: 애플 로그인 키의 "Configure → Primary App ID" 단계,
 * Team ID 가 보이는 "Membership details" 위치.
 */
type KeyGuide = { site: { name: string; url: string }; steps: string[]; notes?: string[] }

type ServiceTab = 'store' | 'login' | 'push'
type StoreGroup = 'appstore' | 'googleplay' | 'signin' | 'google' | 'fcm' | 'apns'
type KeySet = 'iap' | 'apns'

const SERVICE_TABS: readonly (readonly [ServiceTab, string])[] = [
  ['store', '스토어'],
  ['login', '로그인'],
  ['push', '푸시 알림'],
]

// 화면에 묶어 보이는 순서와 제목. 같은 묶음의 키는 STORE_KEYS 순서대로 나온다.
// 묶음마다 제목 띠가 있는 상자 하나로 보인다(안쪽 탭에 묶음이 하나뿐이어도 같은 모양).
const STORE_GROUPS: { id: StoreGroup; tab: ServiceTab; title: string }[] = [
  { id: 'appstore', tab: 'store', title: 'App Store' },
  { id: 'googleplay', tab: 'store', title: 'Google Play' },
  { id: 'signin', tab: 'login', title: 'Sign in with Apple' },
  { id: 'google', tab: 'login', title: 'Google 로그인' },
  { id: 'fcm', tab: 'push', title: 'Firebase Cloud Messaging' },
  { id: 'apns', tab: 'push', title: 'Apple Push Notifications' },
]

// 한 화면에서 함께 가져오는 값들(같은 키의 ID·파일 등). 묶음 제목 아래에 그 키가 무엇인지 한 번 적는다.
// 가져오는 곳이 서로 다르면 묶지 않는다 — Sign in with Apple 은 Team ID 가 다른 화면이라 묶지 않았다.
// 제목·이름은 발급처 화면에 적힌 그대로 쓴다 — 운영자가 그 화면에서 같은 글자를 찾는다.
const KEY_SETS: Record<KeySet, { title: string; note: string }> = {
  iap: {
    title: '앱 내 구입 키',
    note: 'App Store Connect에서 만드는 결제 조회용 API 키',
  },
  apns: {
    title: 'APNs 키',
    note: 'Apple Developer에서 만드는 푸시 알림 전송용 키',
  },
}

// name 은 발급처 화면의 이름. 홀로 보일 때(입력 창 제목)는 묶음 제목을 앞에 붙인다(keyTitle).
// 설명은 두 갈래로 나눈다 — about·KEY_SETS.note(표): 그 키가 무엇인지만, 짧은 명사형. 어디에 쓰이는지는 적지 않는다
// (2026-10-28 사용자 결정 — 쓰이는 곳은 화면 카드 설명 한 줄로 충분하다).
// hint(입력 창): 무엇을 어떤 형식으로 넣는지만, "~넣습니다"로 끝맺는다. 형식은 admin-api SERVER_CONFIG_VALID 와 맞춘다.
// required: 그 서비스를 켰으면 없을 때 기능이 멈추는 값. 앱 공유 암호·앱 내 구입 키는 옛 결제 방식(SK1)에서만 필요해
// 콘솔이 게임마다 가를 수 없으므로 빼 둔다.
// alsoIn: 다른 서비스도 그 값이 있어야 돌 때 그 묶음에도 같은 줄을 보인다 — 번들 ID 는 애플 로그인(apple-token)도
// 쓰므로, iOS 결제를 안 써서 App Store 를 꺼 둔 게임에서도 빠진 것이 보여야 한다.
type StoreKeyDef = {
  key: string; group: StoreGroup; alsoIn?: StoreGroup[]; set?: KeySet; name: string; about: string
  required?: boolean; multiline?: boolean; hint: string; guide: KeyGuide
}

const STORE_KEYS: StoreKeyDef[] = [
  {
    key: 'APPLE_BUNDLE_ID', group: 'appstore', alsoIn: ['signin', 'apns'], name: '번들 ID', about: 'iOS 앱의 고유 ID', required: true,
    hint: '영문·숫자·점·하이픈으로 된 값을 넣습니다. 앱이 여럿이면 쉼표로 나눕니다.',
    guide: {
      site: { name: 'App Store Connect', url: 'https://appstoreconnect.apple.com/apps' },
      steps: [
        '"앱"에서 게임 앱을 고릅니다.',
        '사이드바의 "일반 정보" 아래 "앱 정보"를 엽니다.',
        '"번들 ID"에 보이는 값을 복사합니다. 예: com.회사이름.게임이름',
      ],
      notes: ['유니티 Player Settings의 "Bundle Identifier"와 같은 값입니다.'],
    },
  },
  {
    key: 'APPLE_SHARED_SECRET', group: 'appstore', name: '앱 공유 암호', about: '영수증 확인용 앱 전용 암호',
    hint: 'App Store Connect에서 복사한 값을 그대로 넣습니다.',
    guide: {
      site: { name: 'App Store Connect', url: 'https://appstoreconnect.apple.com/apps' },
      steps: [
        '"앱"에서 게임 앱을 고릅니다.',
        '사이드바의 "일반 정보" 아래 "앱 정보"를 엽니다.',
        '"앱 공유 암호" 섹션에서 "관리"를 누릅니다.',
        '처음이면 "생성"을 누르고, 이미 있으면 보이는 값을 그대로 복사합니다.',
        '"완료"를 눌러 닫습니다.',
      ],
      notes: [
        '"재생성"은 누르지 마세요. 누르는 순간 예전 값을 못 쓰게 됩니다.',
        'App Store Connect 계정 소유자나 관리자만 볼 수 있습니다.',
        '새로 만든 값은 적용까지 최대 24시간 걸릴 수 있습니다.',
      ],
    },
  },
  {
    key: 'GOOGLE_PACKAGE_NAME', group: 'googleplay', name: '패키지 이름', about: 'Android 앱의 고유 ID', required: true,
    hint: 'com.회사이름.게임이름처럼 점으로 나뉜 값을 넣습니다. 앱이 여럿이면 쉼표로 나눕니다.',
    guide: {
      site: { name: 'Google Play Console', url: 'https://play.google.com/console' },
      steps: [
        '"모든 앱"에서 게임 앱을 고릅니다.',
        '화면 위쪽 앱 이름 아래에 보이는 패키지 이름을 복사합니다. 예: com.회사이름.게임이름',
      ],
      notes: [
        '유니티 Player Settings의 "Package Name"과 같은 값입니다.',
        '여기 없는 앱의 결제는 기록하지 않습니다. 비워 두면 Android 결제가 전부 거절됩니다.',
      ],
    },
  },
  {
    key: 'GOOGLE_SERVICE_ACCOUNT_JSON', group: 'googleplay', name: '서비스 계정 키', about: 'Google Cloud 서비스 계정의 키', required: true,
    multiline: true, hint: '내려받은 JSON 파일의 내용 전체를 넣습니다.',
    guide: {
      site: { name: 'Google Cloud 콘솔', url: 'https://console.cloud.google.com/iam-admin/serviceaccounts' },
      steps: [
        'Google Cloud 콘솔에서 프로젝트를 고르고, "API 라이브러리"에서 "Google Play Android Developer API"를 찾아 "사용 설정"을 누릅니다.',
        '"서비스 계정" 페이지에서 "서비스 계정 만들기"로 계정을 만들고 "완료"를 누릅니다.',
        '만든 계정의 이메일을 누르고 "키" 탭 → "키 추가" → "새 키 만들기" → 키 유형 "JSON" → "만들기". JSON 파일이 내려받아집니다.',
        'Google Play Console의 "사용자 및 권한"에서 "사용자 초대"를 누르고 그 서비스 계정 이메일을 넣습니다.',
        '권한은 "재무 데이터, 주문, 취소 설문조사 응답 보기"와 "주문 및 정기 결제 관리"를 켜고 초대를 보냅니다.',
        '내려받은 JSON 파일을 메모장으로 열어 내용 전체를 여기 붙여 넣습니다.',
      ],
      notes: [
        'JSON 파일은 한 번만 내려받을 수 있습니다. 잃어버리면 새 키를 만듭니다.',
        '"새 키 만들기"가 막혀 있으면 회사 Google Cloud 조직 정책이 키 만들기를 막은 것입니다. 조직 관리자에게 이 프로젝트만 풀어 달라고 요청합니다.',
      ],
    },
  },
  {
    key: 'APPLE_TEAM_ID', group: 'signin', alsoIn: ['apns'], name: 'Team ID', about: 'Apple 개발자 계정의 팀 ID', required: true,
    hint: '영문 대문자·숫자 10자를 넣습니다.',
    guide: {
      site: { name: 'Apple Developer', url: 'https://developer.apple.com/account' },
      steps: [
        'Apple Developer 계정에 로그인합니다.',
        '"Membership details"에 보이는 "Team ID"를 복사합니다.',
      ],
      notes: ['유료 개발자 프로그램에 가입한 계정에서만 보입니다.'],
    },
  },
  {
    key: 'APPLE_KEY_ID', group: 'signin', name: 'Key ID', about: 'Sign in with Apple 키의 ID', required: true,
    hint: '영문 대문자·숫자 10자를 넣습니다. Private key와 같은 키의 값이어야 합니다.',
    guide: {
      site: { name: 'Apple Developer › Keys', url: 'https://developer.apple.com/account/resources/authkeys/list' },
      steps: [
        '"인증서, 식별자 및 프로파일"에서 사이드바의 "Keys"를 누르고, 왼쪽 위 추가 버튼(+)을 누릅니다.',
        '"Key Name"에 알아볼 이름을 넣고 "Sign in with Apple"에 체크한 뒤, 옆 "Configure"에서 게임 앱("Primary App ID")을 고르고 "Save"를 누릅니다.',
        '"Continue"를 누르고, 내용을 확인한 뒤 "Confirm"을 누릅니다.',
        '만든 키를 누르면 키 이름 아래에 보이는 값이 "Key ID"입니다.',
      ],
      notes: ['이미 만든 애플 로그인 키가 있으면 "Keys" 목록에서 그 키를 눌러 확인합니다. 아래 "Private key"와 같은 키여야 합니다.'],
    },
  },
  {
    key: 'APPLE_PRIVATE_KEY', group: 'signin', name: 'Private key', about: 'Sign in with Apple 키의 .p8 파일', required: true,
    multiline: true, hint: '.p8 파일 내용 전체를 -----BEGIN PRIVATE KEY----- 줄까지 넣습니다. Key ID와 같은 키의 파일이어야 합니다.',
    guide: {
      site: { name: 'Apple Developer › Keys', url: 'https://developer.apple.com/account/resources/authkeys/list' },
      steps: [
        '위 "Key ID"의 키를 만들 때 마지막 화면에서 "Download"를 누르면 .p8 파일이 내려받아집니다.',
        '그 파일을 메모장으로 열어 "-----BEGIN PRIVATE KEY-----"부터 "-----END PRIVATE KEY-----"까지 전부 복사해 붙여 넣습니다.',
      ],
      notes: [
        '.p8 파일은 한 번만 내려받을 수 있습니다. "Download"가 회색이면 이미 받은 것입니다. 파일을 잃어버렸으면 새 키를 만들고, "Key ID"도 새 키 것으로 바꿉니다.',
      ],
    },
  },
  {
    key: 'APPLE_IAP_ISSUER_ID', group: 'appstore', set: 'iap', name: 'Issuer ID', about: '키를 발급한 계정의 ID',
    hint: '하이픈이 들어간 36자를 넣습니다.',
    guide: {
      site: { name: 'App Store Connect', url: 'https://appstoreconnect.apple.com' },
      steps: [
        'App Store Connect 위쪽에서 "사용자 및 액세스"를 엽니다.',
        '"통합" 탭을 누르고, 사이드바의 "키" 아래 "앱 내 구입"을 누릅니다.',
        '화면 위쪽에 보이는 "Issuer ID"를 복사합니다.',
      ],
      notes: ['App Store Connect 계정 소유자나 관리자만 볼 수 있습니다.'],
    },
  },
  {
    key: 'APPLE_IAP_KEY_ID', group: 'appstore', set: 'iap', name: 'Key ID', about: '앱 내 구입 키의 ID',
    hint: '영문 대문자·숫자 10자를 넣습니다. Private key와 같은 키의 값이어야 합니다.',
    guide: {
      site: { name: 'App Store Connect', url: 'https://appstoreconnect.apple.com' },
      steps: [
        'App Store Connect에서 "사용자 및 액세스" → "통합" 탭 → 사이드바의 "키" 아래 "앱 내 구입"을 엽니다.',
        '"앱 내 구입 키 생성"을 누릅니다. 이미 "활성화됨"에 키가 있으면 추가 버튼(+)을 누릅니다.',
        '알아볼 이름을 넣고 "생성"을 누릅니다.',
        '목록에 생긴 키의 "키 ID"를 복사합니다.',
      ],
      notes: ['로그인 탭의 Sign in with Apple 키와는 다른 키입니다. 이 화면에서 만든 키의 "키 ID"를 넣습니다.'],
    },
  },
  {
    key: 'APPLE_IAP_PRIVATE_KEY', group: 'appstore', set: 'iap', name: 'Private key', about: '앱 내 구입 키의 .p8 파일',
    multiline: true, hint: '.p8 파일 내용 전체를 -----BEGIN PRIVATE KEY----- 줄까지 넣습니다. Key ID와 같은 키의 파일이어야 합니다.',
    guide: {
      site: { name: 'App Store Connect', url: 'https://appstoreconnect.apple.com' },
      steps: [
        '위 "키 ID"의 키가 있는 화면의 "활성화됨" 아래에서 그 키의 "키 다운로드"를 누르고, 대화창에서 "다운로드"를 누릅니다.',
        '내려받은 .p8 파일을 메모장으로 열어 "-----BEGIN PRIVATE KEY-----"부터 "-----END PRIVATE KEY-----"까지 전부 복사해 붙여 넣습니다.',
      ],
      notes: [
        '.p8 파일은 한 번만 내려받을 수 있습니다. "키 다운로드"가 안 보이면 이미 받은 것입니다. 파일을 잃어버렸으면 새 키를 만들고, "키 ID"도 새 키 것으로 바꿉니다.',
      ],
    },
  },
  // 푸시 알림 — 화면 표기는 Firebase·Apple 문서의 영어 표기로 적었다. 한국어 화면의 글자는 확인 못 함(2026-10-29) —
  // 운영자가 다르다고 하면 그 화면 글자로 고친다.
  {
    key: 'FCM_SERVICE_ACCOUNT_JSON', group: 'fcm', name: '서비스 계정 키', about: 'Firebase 프로젝트 서비스 계정의 키', required: true,
    multiline: true, hint: '내려받은 JSON 파일의 내용 전체를 넣습니다.',
    guide: {
      site: { name: 'Firebase 콘솔', url: 'https://console.firebase.google.com' },
      steps: [
        'Firebase 콘솔에서 게임의 Firebase 프로젝트를 엽니다.',
        '왼쪽 위 톱니바퀴 → "Project settings" → "Service accounts" 탭을 엽니다.',
        '"Generate new private key"를 누르고 확인하면 JSON 파일이 내려받아집니다.',
        '내려받은 JSON 파일을 메모장으로 열어 내용 전체를 여기 붙여 넣습니다.',
      ],
      notes: [
        '게임 빌드의 Firebase 설정(SupabaseSettings 의 푸시 칸)과 같은 프로젝트여야 합니다.',
        'JSON 파일은 다시 내려받을 수 없습니다. 잃어버리면 새 키를 만듭니다.',
      ],
    },
  },
  {
    key: 'APNS_KEY_ID', group: 'apns', set: 'apns', name: 'Key ID', about: 'APNs 키의 ID', required: true,
    hint: '영문 대문자·숫자 10자를 넣습니다. Private key와 같은 키의 값이어야 합니다.',
    guide: {
      site: { name: 'Apple Developer › Keys', url: 'https://developer.apple.com/account/resources/authkeys/list' },
      steps: [
        '"인증서, 식별자 및 프로파일"에서 사이드바의 "Keys"를 누르고, 왼쪽 위 추가 버튼(+)을 누릅니다.',
        '"Key Name"에 알아볼 이름을 넣고 "Apple Push Notifications service (APNs)"에 체크합니다.',
        '환경을 고르는 칸이 있으면 "Production"을 고릅니다. 개발용 빌드로 시험할 때도 보내려면 "Sandbox & Production"을 고릅니다.',
        '"Continue" → "Register"를 누르고, 만든 키를 누르면 키 이름 아래에 보이는 값이 "Key ID"입니다.',
      ],
      notes: ['Sign in with Apple 키와 다른 키입니다. 앱의 "Push Notifications" 기능도 켜 두어야 합니다(Identifiers에서 앱을 눌러 체크).'],
    },
  },
  {
    key: 'APNS_PRIVATE_KEY', group: 'apns', set: 'apns', name: 'Private key', about: 'APNs 키의 .p8 파일', required: true,
    multiline: true, hint: '.p8 파일 내용 전체를 -----BEGIN PRIVATE KEY----- 줄까지 넣습니다. Key ID와 같은 키의 파일이어야 합니다.',
    guide: {
      site: { name: 'Apple Developer › Keys', url: 'https://developer.apple.com/account/resources/authkeys/list' },
      steps: [
        '위 "Key ID"의 키를 만들 때 마지막 화면에서 "Download"를 누르면 .p8 파일이 내려받아집니다.',
        '그 파일을 메모장으로 열어 "-----BEGIN PRIVATE KEY-----"부터 "-----END PRIVATE KEY-----"까지 전부 복사해 붙여 넣습니다.',
      ],
      notes: [
        '.p8 파일은 한 번만 내려받을 수 있습니다. 파일을 잃어버렸으면 새 키를 만들고, "Key ID"도 새 키 것으로 바꿉니다.',
      ],
    },
  },
  // 웹 로그인 값(지금은 게임 밖 고객센터가 쓴다) — 표기는 한국어 도움말(Google: developers.google.com/identity/gsi/web ?hl=ko,
  // Apple: developer.apple.com/kr/help "웹용 Sign in with Apple 구성")로 2026-10-30 확인했다.
  {
    key: 'GOOGLE_WEB_CLIENT_ID', group: 'google', name: '클라이언트 ID', about: '웹 애플리케이션 유형 OAuth 클라이언트의 ID',
    hint: '.apps.googleusercontent.com 으로 끝나는 값을 넣습니다.',
    guide: {
      site: { name: 'Google Cloud 콘솔 › 클라이언트', url: 'https://console.cloud.google.com/auth/clients' },
      steps: [
        'Google Cloud 콘솔에서 게임의 프로젝트를 고르고 "Google 인증 플랫폼"의 "클라이언트" 페이지를 엽니다.',
        '애플리케이션 유형이 "웹 애플리케이션"인 클라이언트를 엽니다. 게임의 구글 로그인에 쓰는 것과 같은 클라이언트입니다.',
        '"승인된 JavaScript 원본"에 https://truebase-support.pages.dev 를 추가하고 저장합니다.',
        '같은 화면의 "클라이언트 ID"를 복사합니다.',
      ],
      notes: [
        '저장한 원본이 걸리기까지 몇 분 걸릴 수 있습니다.',
        '이 클라이언트 ID가 Supabase 대시보드 구글 로그인 설정의 "Client IDs"에 들어 있어야 로그인이 됩니다.',
      ],
    },
  },
  {
    key: 'APPLE_SERVICES_ID', group: 'signin', name: 'Services ID', about: '웹 로그인용 Services ID의 식별자',
    hint: '영문·숫자·점·하이픈으로 된 식별자를 넣습니다.',
    guide: {
      site: { name: 'Apple Developer › Identifiers', url: 'https://developer.apple.com/account/resources/identifiers/list/serviceId' },
      steps: [
        '"인증서, 식별자 및 프로파일"에서 사이드바의 "Identifiers"를 누르고, 게임의 애플 로그인에 쓰는 Services ID를 엽니다.',
        '"Sign in with Apple"의 "Configure"를 누릅니다.',
        '"Website URLs"의 도메인에 truebase-support.pages.dev 를, 반환 URL에 https://truebase-support.pages.dev/ 를 더합니다.',
        '"Done" → "Continue" → "Save"를 누릅니다.',
        'Services ID 목록의 "Identifier" 값을 복사합니다. 예: com.회사이름.게임이름.Services',
      ],
      notes: ['이 Services ID가 Supabase 대시보드 애플 로그인 설정의 "Client IDs"에 들어 있어야 로그인이 됩니다. 새로 만들었으면 맨 앞에 넣습니다.'],
    },
  },
]

/** 입력 창 제목처럼 홀로 보일 때의 이름 — "Key ID" 만으로는 어느 키인지 모르니 묶음 제목을 앞에 붙인다. */
function keyTitle(k: StoreKeyDef | undefined): string {
  if (!k) return ''
  const owner = k.set ? KEY_SETS[k.set].title : STORE_GROUPS.find((g) => g.id === k.group)?.title
  return owner ? `${owner} › ${k.name}` : k.name
}

/** 입력칸 옆 "가져오는 방법" — 누르면 발급처 바로 가기·단계가 펼쳐진다. */
function KeyGuideToggle({ guide }: { guide: KeyGuide }) {
  const [open, setOpen] = useState(false)
  return (
    <div>
      <button type="button" onClick={() => setOpen((v) => !v)}
        className="inline-flex items-center gap-1 text-xs text-[#1677ff] hover:underline">
        <CircleHelp className="w-3.5 h-3.5" />
        가져오는 방법
        {open ? <ChevronUp className="w-3 h-3" /> : <ChevronDown className="w-3 h-3" />}
      </button>
      {open && (
        <div className="mt-2 rounded-md border border-neutral-200 bg-neutral-50 px-3 py-2.5 space-y-2 text-xs">
          <a href={guide.site.url} target="_blank" rel="noreferrer" className="inline-flex items-center gap-0.5 text-[#1677ff] hover:underline">
            {guide.site.name} 열기 <ExternalLink className="w-3 h-3" />
          </a>
          <ol className="list-decimal pl-4 space-y-1 leading-relaxed text-neutral-700">
            {guide.steps.map((s, i) => <li key={i}>{s}</li>)}
          </ol>
          {guide.notes && guide.notes.length > 0 && (
            <ul className="space-y-1 leading-relaxed text-neutral-500">
              {guide.notes.map((s, i) => <li key={i}>※ {s}</li>)}
            </ul>
          )}
        </div>
      )}
    </div>
  )
}

const STATUS_VIEW: Record<StoreKeyStatus['status'], { text: string; cls: string }> = {
  ok: { text: '등록됨', cls: 'bg-green-50 text-green-700' },
  missing: { text: '없음', cls: 'bg-red-50 text-red-600' },
  invalid: { text: '형식 오류', cls: 'bg-amber-50 text-amber-700' },
}
// 필수가 아닌 값이 비어 있는 것은 문제가 아니다 — 빨갛게 띄우면 진짜 빠진 필수 값이 묻힌다.
const OPTIONAL_MISSING_VIEW = { text: '없음', cls: 'bg-neutral-100 text-neutral-500' }

type ServiceEnabled = Partial<Record<StoreGroup, boolean>>

function StoreStatusCard({
  target,
  report,
}: {
  target: ProjectTarget
  report: (e: unknown, fallback: string) => void
}) {
  const [rows, setRows] = useState<StoreKeyStatus[] | null>(null)
  const [services, setServices] = useState<ServiceEnabled>({})
  const [toggling, setToggling] = useState<StoreGroup | null>(null)
  const [editing, setEditing] = useState<{ key: string; clear: boolean } | null>(null)
  const [value, setValue] = useState('')
  const [saving, setSaving] = useState(false)
  const [savedKey, setSavedKey] = useState('')
  const [saveError, setSaveError] = useState('')
  const [tab, setTab] = useState<ServiceTab>('store')

  const load = useCallback(async () => {
    try {
      const res = await callAdmin<{ rows: StoreKeyStatus[]; services?: ServiceEnabled }>(target, 'settings.getStoreStatus', {})
      setRows(res.rows)
      setServices(res.services ?? {})
    } catch (e: unknown) {
      report(e, '서비스 연결 상태를 불러오지 못했습니다.')
    }
  }, [target, report])

  useEffect(() => {
    setRows(null)
    setServices({})
    setSavedKey('')
    void load()
  }, [load])

  const byKey = new Map((rows ?? []).map((r) => [r.key, r]))
  const editingDef = STORE_KEYS.find((k) => k.key === editing?.key)

  // 아직 한 번도 켜고 끈 적 없는 서비스는 값이 하나라도 있으면 쓰는 것으로 본다 — 이미 운영 중인 게임이 접혀 보이지 않게.
  const isEnabled = (g: StoreGroup) =>
    services[g] ?? STORE_KEYS.some((k) => k.group === g && (byKey.get(k.key)?.status ?? 'missing') !== 'missing')

  const toggle = async (g: StoreGroup, enabled: boolean) => {
    setToggling(g)
    try {
      const res = await callAdmin<{ services: ServiceEnabled }>(target, 'settings.setServiceEnabled', { service: g, enabled })
      setServices(res.services)
    } catch (e: unknown) {
      report(e, '사용 여부를 바꾸지 못했습니다.')
    } finally {
      setToggling(null)
    }
  }

  const open = (key: string, clear: boolean) => {
    setValue('')
    setSavedKey('')
    setSaveError('')
    setEditing({ key, clear })
  }

  const save = async () => {
    if (!editing) return
    setSaving(true)
    setSaveError('')
    try {
      await callAdmin(target, 'settings.setServerConfig', { key: editing.key, secretValue: editing.clear ? '' : value })
      setSavedKey(editing.key)
      setEditing(null)
      setValue('')
      await load()
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) report(e, '')
      else setSaveError(e instanceof Error ? e.message : '값을 저장하지 못했습니다.')
    } finally {
      setSaving(false)
    }
  }

  const groups = STORE_GROUPS.filter((g) => g.tab === tab)

  return (
    <WhiteCard className="p-5 space-y-5">
      <CardHeader
        title="서비스 연결"
        description="결제 확인·환불 기록·탈퇴 처리·푸시 알림·게임 밖 고객센터 로그인에 쓰는 스토어·애플·구글·Firebase 키를 넣습니다."
      />
      <Segmented value={tab} onChange={setTab} options={SERVICE_TABS.map(([v, label]) => ({ v, label }))} />
      {/* 가게마다 테두리 상자 하나. 상자끼리 칸 폭을 맞춰(table-fixed + colgroup) 상태·버튼이 위아래로 줄을 선다. */}
      <div className="space-y-4">
        {groups.map((g) => (
          <GroupBox
            key={g.id}
            title={g.title}
            // 상태를 못 불러온 동안은 켜짐을 알 수 없다 — 스위치를 숨기고 접지 않는다(꺼진 것처럼 보이지 않게).
            actions={rows && <>
              <span className="text-xs text-neutral-500">사용</span>
              <Switch label={`${g.title} 사용`} value={isEnabled(g.id)} disabled={toggling === g.id}
                onChange={(v) => void toggle(g.id, v)} />
            </>}
          >
            {(!rows || isEnabled(g.id)) && (
            <table className="w-full text-sm table-fixed">
              <colgroup>
                <col className="w-[34%]" />
                <col />
                <col className="w-[76px]" />
                <col className="w-[164px]" />
              </colgroup>
              <tbody>
                {STORE_KEYS.filter((k) => k.group === g.id || k.alsoIn?.includes(g.id)).flatMap((k, i, list) => {
                  const row = byKey.get(k.key)
                  const view = !row ? null : row.status === 'missing' && !k.required ? OPTIONAL_MISSING_VIEW : STATUS_VIEW[row.status]
                  // 한 키에서 나오는 값들은 묶음 제목 한 줄 아래에 들여 써서 함께 보인다.
                  const startsSet = !!k.set && list[i - 1]?.set !== k.set
                  const set = k.set ? KEY_SETS[k.set] : null
                  return [
                    ...(startsSet && set ? [
                      <tr key={`${k.set}-head`} className="border-t border-neutral-100 first:border-t-0">
                        <td colSpan={4} className="px-4 pt-2.5 pb-1">
                          <div className="text-neutral-800">{set.title}</div>
                          <div className="text-xs text-neutral-500">{set.note}</div>
                        </td>
                      </tr>,
                    ] : []),
                    <tr key={k.key} className={set ? '' : 'border-t border-neutral-100 first:border-t-0'}>
                      <td className={`py-2 pr-4 ${set ? 'pl-8' : 'pl-4'}`}>
                        <div className={set ? 'text-neutral-700' : 'text-neutral-800'}>
                          {k.name}
                          {k.required && <span className="ml-0.5 text-red-500" aria-label="필수">*</span>}
                        </div>
                        <div className="text-xs text-neutral-500">{k.about}</div>
                      </td>
                      <td className="py-2 pr-4 text-xs text-neutral-400">
                        {row?.source === 'console' && row.updatedAt && (
                          <>
                            <div className="whitespace-nowrap">수정 {formatDateTime(row.updatedAt)}</div>
                            {row.updatedBy && <div className="truncate">{row.updatedBy}</div>}
                          </>
                        )}
                        {row?.source === 'supabase' && <span>콘솔 밖에서 넣은 값 사용 중</span>}
                        {savedKey === k.key && <span className="ml-2 text-green-600 whitespace-nowrap">저장했습니다.</span>}
                      </td>
                      <td className="py-2 pr-3 text-right">
                        {view
                          ? <span className={`inline-flex rounded px-2 py-0.5 text-xs whitespace-nowrap ${view.cls}`}>{view.text}</span>
                          : <span className="text-xs text-neutral-400">-</span>}
                      </td>
                      <td className="py-2 pr-4 text-right whitespace-nowrap">
                        {/* 두 자리는 폭을 고정하고 삭제가 없는 줄도 빈자리를 남긴다 — 줄마다 버튼 위치가 흔들리지 않게. */}
                        <div className="inline-flex gap-1.5">
                          {row?.status === 'missing' ? (
                            <Button size="sm" variant="primary" className="w-[76px]" onClick={() => open(k.key, false)} disabled={!rows}>
                              <Plus className="w-3 h-3" />
                              입력
                            </Button>
                          ) : (
                            <Button size="sm" className="w-[76px]" onClick={() => open(k.key, false)} disabled={!rows}>
                              <Pencil className="w-3 h-3" />
                              바꾸기
                            </Button>
                          )}
                          {row?.source === 'console' ? (
                            <Button size="sm" variant="dangerOutline" className="w-[64px]" onClick={() => open(k.key, true)}>
                              <Trash2 className="w-3 h-3" />
                              삭제
                            </Button>
                          ) : (
                            <span className="w-[64px]" aria-hidden />
                          )}
                        </div>
                      </td>
                    </tr>,
                  ]
                })}
              </tbody>
            </table>
            )}
          </GroupBox>
        ))}
      </div>

      {/* 지우기는 되돌리기 어려운 확인이라 ConfirmDialog, 값 넣기는 편집 창. */}
      <ConfirmDialog
        open={!!editing?.clear}
        title={`${keyTitle(editingDef)} 삭제`}
        description="삭제하면 1분 안에 콘솔 밖에서 넣은 값으로 돌아갑니다. 그 값도 없으면 이 기능이 멈춥니다."
        confirmLabel="삭제"
        danger
        busy={saving}
        error={saveError}
        onConfirm={() => void save()}
        onCancel={() => setEditing(null)}
      />
      {editing && !editing.clear && editingDef && (
        <EditDialog
          title={`${keyTitle(editingDef)} ${byKey.get(editing.key)?.status === 'missing' ? '입력' : '바꾸기'}`}
          busy={saving}
          onClose={() => setEditing(null)}
          error={saveError || undefined}
          footer={<>
            <Button variant="outline" onClick={() => setEditing(null)} disabled={saving}>취소</Button>
            <Button variant="primary" onClick={() => void save()} disabled={saving || value.trim() === ''}>
              {saving && <Loader2 className="w-4 h-4 animate-spin" />}
              저장
            </Button>
          </>}
        >
          <TargetGameStrip />
          <div className="space-y-1.5">
            <KeyGuideToggle key={editingDef.key} guide={editingDef.guide} />
            {editingDef.multiline
              ? (
                <textarea
                  className={areaCls + ' w-full h-40 font-mono'}
                  value={value}
                  onChange={(e) => setValue(e.target.value)}
                  spellCheck={false}
                  autoComplete="off"
                />
              )
              : (
                <input
                  className={`${inputCls} w-full font-mono`}
                  value={value}
                  onChange={(e) => setValue(e.target.value)}
                  spellCheck={false}
                  autoComplete="off"
                />
              )}
            <p className="text-xs text-neutral-500">{editingDef.hint}</p>
          </div>
          <p className="text-sm text-neutral-600">저장하면 1분 안에 적용됩니다. 저장한 값은 다시 볼 수 없습니다.</p>
        </EditDialog>
      )}
    </WhiteCard>
  )
}

const MAIL_FIELDS: LimitField[] = [
  {
    param: 'expiredRetentionDays', field: 'expired_retention_days', min: 0, max: 30,
    label: '만료 우편 보관 일수',
    hint: '기간이 지난 우편을 "기한 만료"로 남겨 두는 일수. 지나면 우편함에서 사라집니다. 0이면 기간이 지나는 즉시 사라집니다. 0~30 사이.',
  },
]

const FRIEND_FIELDS: LimitField[] = [
  {
    param: 'maxFriends', field: 'max_friends', min: 1, max: 1000,
    label: '친구 수 상한',
    hint: '한 계정이 가질 수 있는 친구의 최대 수. 상한에 닿으면 요청도 수락도 막힙니다. 1~1000 사이.',
  },
  {
    param: 'maxPendingSent', field: 'max_pending_sent', min: 1, max: 500,
    label: '보낸 요청 대기 상한',
    hint: '아직 상대가 응답하지 않은 요청의 최대 개수. 무차별 요청을 막습니다. 1~500 사이.',
  },
  {
    param: 'requestCooldownSeconds', field: 'request_cooldown_seconds', min: 0, max: 300,
    label: '연속 요청 최소 간격',
    hint: '같은 사람이 요청을 연달아 보낼 때 기다려야 하는 시간. 초 단위로 최대 300까지, 0이면 제한 없음.',
  },
]

const LOBBY_FIELDS: LimitField[] = [
  {
    param: 'maxPendingInvitesReceived', field: 'max_pending_invites_received', min: 1, max: 200,
    label: '받는 쪽 대기 초대 상한',
    hint: '한 사람이 동시에 받아 둘 수 있는 초대의 최대 개수. 넘으면 그 사람에게는 초대가 들어가지 않습니다. 1~200 사이.',
  },
  {
    param: 'inviteCooldownSeconds', field: 'invite_cooldown_seconds', min: 0, max: 300,
    label: '연속 초대 최소 간격',
    hint: '같은 사람이 초대를 연달아 보낼 때 기다려야 하는 시간. 초 단위로 최대 300까지, 0이면 제한 없음.',
  },
  {
    param: 'lobbyExpireMinutes', field: 'lobby_expire_minutes', min: 1, max: 1440,
    label: '방이 닫히기까지의 시간',
    hint: '아무도 시작하지 않은 방이 저절로 닫히기까지의 시간. 분 단위로 최대 1440까지.',
  },
]

// 범위는 admin-api 와 install.sql request_limit_settings 의 check 와 같게 둔다.
const REQUEST_LIMIT_TOGGLE: LimitToggle = {
  param: 'enabled', field: 'enabled', label: '사용',
  hint: '끄면 지금 제한 중인 계정도 함께 풀립니다.',
}

const REQUEST_LIMIT_FIELDS: LimitField[] = [
  {
    param: 'windowSeconds', field: 'window_seconds', min: 1, max: 600,
    label: '기준 시간',
    hint: '요청 수를 세는 시간. 초 단위로 1~600 사이.',
  },
  {
    param: 'maxRequests', field: 'max_requests', min: 10, max: 10000,
    label: '최대 요청 수',
    hint: '기준 시간 안에 한 계정이 보낼 수 있는 요청 수. 넘으면 제한됩니다. 로그인 전 요청, 단순 조회, 쿠폰 말고 실패한 요청은 세지 않습니다. 10~10000 사이.',
  },
  {
    param: 'blockMinutes', field: 'block_minutes', min: 1, max: 1440,
    label: '제한 시간',
    hint: '기준을 넘은 계정의 요청을 모두 막는 시간. 분 단위로 1~1440 사이.',
  },
]

type RequestLimitBlock = {
  key: string; account_id: string | null; ip: string | null; display_name: string | null
  blocked_at: string; blocked_until: string
}

/** 지금 요청 제한에 걸려 있는 계정. 정상 플레이어가 잘못 걸렸을 때 바로 풀 수 있게 둔다(2026-10-29 사용자 결정). */
function RequestLimitBlocksCard({ target, report }: { target: ProjectTarget; report: (e: unknown, fallback: string) => void }) {
  const [rows, setRows] = useState<RequestLimitBlock[] | null>(null)
  const [failed, setFailed] = useState(false)
  const [unblocking, setUnblocking] = useState('')

  const load = useCallback(async () => {
    setFailed(false)
    try {
      setRows(await callAdmin<RequestLimitBlock[]>(target, 'requestLimit.list'))
    } catch (e: unknown) {
      setRows([])
      setFailed(true)
      report(e, '제한 중인 계정을 불러오지 못했습니다.')
    }
  }, [target, report])

  useEffect(() => { setRows(null); void load() }, [load])

  const unblock = async (key: string) => {
    setUnblocking(key)
    try {
      await callAdmin(target, 'requestLimit.unblock', { key })
      await load()
    } catch (e: unknown) {
      report(e, '제한을 풀지 못했습니다.')
    } finally {
      setUnblocking('')
    }
  }

  return (
    <WhiteCard>
      <div className="p-5 pb-3">
        <CardHeader
          title="제한 중인 계정"
          description="지금 요청이 막혀 있는 계정입니다. 해제하면 곧바로 다시 요청할 수 있습니다."
          actions={
            <Button size="compact" onClick={() => void load()} disabled={rows === null}>
              <RefreshCw className={`w-3.5 h-3.5 ${rows === null ? 'animate-spin' : ''}`} />
              새로고침
            </Button>
          }
        />
      </div>
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="bg-neutral-50 text-neutral-500 text-xs">
            <tr>
              <th className="px-5 py-2 text-left font-medium whitespace-nowrap">대상</th>
              <th className="px-3 py-2 text-left font-medium whitespace-nowrap">제한 시작</th>
              <th className="px-3 py-2 text-left font-medium whitespace-nowrap">제한 끝</th>
              <th className="px-5 py-2" />
            </tr>
          </thead>
          <tbody>
            {rows === null || rows.length === 0 ? (
              <TableStatusRow loading={rows === null} empty={rows !== null && rows.length === 0} failed={failed} colSpan={4} emptyText="제한 중인 계정이 없습니다." />
            ) : rows.map((r) => (
              <tr key={r.key} className="border-t border-neutral-100">
                <td className="px-5 py-2 text-neutral-800">
                  {r.display_name || r.account_id || r.key}
                </td>
                <td className="px-3 py-2 text-neutral-600 whitespace-nowrap">{formatDateTime(r.blocked_at)}</td>
                <td className="px-3 py-2 text-neutral-600 whitespace-nowrap">{formatDateTime(r.blocked_until)}</td>
                <td className="px-5 py-2 text-right whitespace-nowrap">
                  <Button size="sm" onClick={() => void unblock(r.key)} disabled={unblocking !== ''}>
                    {unblocking === r.key ? <Loader2 className="w-3 h-3 animate-spin" /> : <Unlock className="w-3 h-3" />}
                    해제
                  </Button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </WhiteCard>
  )
}

// 흰 글자가 얹히는 색이라 전부 흰색과 4.5:1 이상 대비가 나는 진한 색으로 골랐다.
const CONSOLE_PALETTE: { name: string; hex: string }[] = [
  { name: '보라', hex: '#7c3aed' },
  { name: '남색', hex: '#4338ca' },
  { name: '파랑', hex: '#1d4ed8' },
  { name: '청록', hex: '#0f766e' },
  { name: '초록', hex: '#15803d' },
  { name: '주황', hex: '#c2410c' },
  { name: '빨강', hex: '#b91c1c' },
  { name: '분홍', hex: '#be185d' },
  { name: '갈색', hex: '#92400e' },
  { name: '회색', hex: '#475569' },
]

const HEX_RE = /^#[0-9a-f]{6}$/

type ConsoleSettings = {
  color: string | null
  display_name: string | null
  icon: string | null
  updated_at: string | null
  updated_by: string | null
}

const ICON_PX = 128
const ICON_MAX_CHARS = 120_000 // install.sql ts_admin_console_settings.icon 검사와 같은 값

/** 올린 그림을 가운데 기준 정사각형으로 잘라 128px 로 줄인다. 투명 배경을 살리려 WebP, 안 되는 브라우저는 PNG. */
function shrinkIcon(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const url = URL.createObjectURL(file)
    const img = new Image()
    img.onload = () => {
      URL.revokeObjectURL(url)
      const side = Math.min(img.naturalWidth, img.naturalHeight)
      const canvas = document.createElement('canvas')
      canvas.width = ICON_PX
      canvas.height = ICON_PX
      const ctx = canvas.getContext('2d')
      if (!ctx) { reject(new Error('그림을 처리하지 못했습니다.')); return }
      ctx.imageSmoothingQuality = 'high'
      ctx.drawImage(img, (img.naturalWidth - side) / 2, (img.naturalHeight - side) / 2, side, side, 0, 0, ICON_PX, ICON_PX)
      let out = canvas.toDataURL('image/webp', 0.9)
      if (!out.startsWith('data:image/webp')) out = canvas.toDataURL('image/png')
      resolve(out)
    }
    img.onerror = () => { URL.revokeObjectURL(url); reject(new Error('그림 파일을 열지 못했습니다. PNG·JPG·WebP 파일을 골라 주세요.')) }
    img.src = url
  })
}

/** 콘솔에 보이는 게임 이름과 아이콘. */
function ConsoleIdentityCard({
  target,
  current,
  onSaved,
  report,
}: {
  target: ProjectTarget
  current: ConsoleSettings | null
  onSaved: (s: ConsoleSettings) => void
  report: (e: unknown, fallback: string) => void
}) {
  const [name, setName] = useState('')
  const [icon, setIcon] = useState<string | null>(null)
  const [iconError, setIconError] = useState('')
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const filled = useRef(false)

  // 서버 값은 처음 받을 때만 채운다(게임을 바꾸면 current 가 null 로 돌아갔다가 다시 온다).
  // current 가 바뀔 때마다 채우면 색 카드에서 저장할 때 여기서 고치던 이름·아이콘이 서버 값으로 돌아간다.
  useEffect(() => {
    if (!current) { filled.current = false; setSaved(false); return }
    if (filled.current) return
    filled.current = true
    setName(current.display_name ?? '')
    setIcon(current.icon)
    setIconError('')
  }, [current])

  const defaultName = getProject(target).label
  const trimmed = name.trim()
  const tooLong = trimmed.length > 40
  const changed = !!current && (trimmed !== (current.display_name ?? '') || icon !== current.icon)

  const pick = async (file: File | undefined) => {
    setIconError('')
    setSaved(false)
    if (!file) return
    try {
      const data = await shrinkIcon(file)
      if (data.length > ICON_MAX_CHARS) { setIconError('그림이 너무 복잡해 줄여도 큽니다. 다른 그림을 골라 주세요.'); return }
      setIcon(data)
    } catch (e: unknown) {
      setIconError(e instanceof Error ? e.message : '그림을 처리하지 못했습니다.')
    }
  }

  const save = async () => {
    setSaving(true)
    setSaved(false)
    try {
      const s = await callAdmin<ConsoleSettings>(target, 'settings.setConsoleIdentity', { displayName: trimmed, icon: icon ?? '' })
      onSaved(s)
      setSaved(true)
    } catch (e: unknown) {
      report(e, '이름·아이콘을 저장하지 못했습니다.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <WhiteCard className="p-5 space-y-5 max-w-2xl">
      <CardHeader
        title="프로젝트 이름 · 아이콘"
        description="프로젝트 목록·메뉴·상단 띠·확인 창에 보일 이 프로젝트의 이름과 아이콘을 정합니다."
      />

      <div className="space-y-1">
        <label htmlFor="console-name" className="block text-sm font-medium text-neutral-800">프로젝트 이름</label>
        <input
          id="console-name"
          className={`${inputCls} w-72 ${tooLong ? 'border-red-400 focus:border-red-400 focus:ring-red-200' : ''}`}
          value={name}
          disabled={!current}
          onChange={(e) => { setName(e.target.value); setSaved(false) }}
          autoComplete="off"
        />
        <p className={`text-xs ${tooLong ? 'text-red-600' : 'text-neutral-500'}`}>
          {tooLong ? '40자 이내로 적어 주세요.' : `비워 두면 "${defaultName}"으로 보입니다.`}
        </p>
      </div>

      <div className="space-y-2">
        <span className="block text-sm font-medium text-neutral-800">아이콘</span>
        <div className="flex flex-wrap items-center gap-3">
          {icon
            ? <img src={icon} alt="" className="w-14 h-14 rounded-xl object-cover border border-neutral-200" />
            : <ProjectIcon target={target} size={56} />}
          <label className={`inline-flex items-center h-9 px-3 rounded-md border border-neutral-300 text-sm text-neutral-700 hover:bg-neutral-50 whitespace-nowrap ${current ? 'cursor-pointer' : 'opacity-40 pointer-events-none'}`}>
            그림 고르기
            <input
              type="file"
              accept="image/png,image/jpeg,image/webp"
              className="sr-only"
              disabled={!current}
              onChange={(e) => { void pick(e.target.files?.[0]); e.target.value = '' }}
            />
          </label>
          {icon && (
            <Button onClick={() => { setIcon(null); setSaved(false) }} disabled={saving}>
              그림 빼기
            </Button>
          )}
        </div>
        <p className={`text-xs ${iconError ? 'text-red-600' : 'text-neutral-500'}`}>
          {iconError || '정사각형 그림이 좋습니다. 가운데를 기준으로 잘라 작게 줄여 저장합니다. 그림이 없으면 프로젝트 색 바탕에 이름 첫 글자가 보입니다.'}
        </p>
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <Button variant="primary" onClick={() => void save()} disabled={saving || !changed || tooLong}>
          저장
        </Button>
        {saved && <span className="text-sm text-green-600 whitespace-nowrap">저장했습니다.</span>}
      </div>
    </WhiteCard>
  )
}

function ConsoleCards({ target, report }: { target: ProjectTarget; report: (e: unknown, fallback: string) => void }) {
  const [current, setCurrent] = useState<ConsoleSettings | null>(null)

  useEffect(() => {
    setCurrent(null)
    callAdmin<ConsoleSettings>(target, 'settings.getConsole')
      .then(setCurrent)
      .catch((e: unknown) => report(e, '프로젝트 정보를 불러오지 못했습니다.'))
  }, [target, report])

  const onSaved = (s: ConsoleSettings) => {
    setCurrent(s)
    setProjectLook(target, toProjectLook(s))
  }

  return (
    <div className="space-y-4">
      <ConsoleIdentityCard target={target} current={current} onSaved={onSaved} report={report} />
      <ConsoleColorCard target={target} current={current} onSaved={onSaved} report={report} />
    </div>
  )
}

function ConsoleColorCard({
  target,
  current,
  onSaved,
  report,
}: {
  target: ProjectTarget
  current: ConsoleSettings | null
  onSaved: (s: ConsoleSettings) => void
  report: (e: unknown, fallback: string) => void
}) {
  const look = useProjectLook()
  // 다른 게임과 비슷한지는 고른 원래 색끼리 잰다(화면에 보이는 어두운 화면용 색이 아니라).
  const colorOf = look.baseColor
  const [value, setValue] = useState('')
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const filled = useRef(false)

  // 이름·아이콘 카드와 같은 이유로 처음 받을 때만 채우고, 저장 표시도 그때만 끈다.
  useEffect(() => {
    if (!current) { filled.current = false; setSaved(false); return }
    if (filled.current) return
    filled.current = true
    setValue(current.color ?? getProject(target).color)
  }, [target, current])

  const hex = value.trim().toLowerCase()
  const valid = HEX_RE.test(hex)
  const readable = valid && contrastWithWhite(hex) >= 4.5
  const similar = valid
    ? PROJECTS.filter((p) => p.key !== target && colorDistance(colorOf(p.key), hex) < 90)
    : []
  const changed = !!current && valid && hex !== (current.color ?? getProject(target).color)

  const save = async (color: string | null) => {
    setSaving(true)
    setSaved(false)
    try {
      const s = await callAdmin<ConsoleSettings>(target, 'settings.setConsoleColor', { color: color ?? '' })
      onSaved(s)
      setValue(s.color ?? getProject(target).color)
      setSaved(true)
    } catch (e: unknown) {
      report(e, '색을 저장하지 못했습니다.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <WhiteCard className="p-5 space-y-5 max-w-2xl">
      <CardHeader
        title="프로젝트 색"
        description="메뉴·상단 띠·확인 창에 쓸 이 프로젝트의 색을 정합니다."
      />

      <div className="flex flex-wrap gap-2.5" role="group" aria-label="색 고르기">
        {CONSOLE_PALETTE.map((c) => {
          const on = c.hex === hex
          return (
            <button
              key={c.hex}
              type="button"
              aria-label={c.name}
              aria-pressed={on}
              disabled={!current}
              onClick={() => { setValue(c.hex); setSaved(false) }}
              className="w-11 h-11 rounded-xl border-[3px] border-white disabled:opacity-40"
              // 테두리 색은 neutral 변수로 — 고정 색이면 어두운 화면에서 선택 표시가 안 보인다.
              style={{
                backgroundColor: c.hex,
                boxShadow: on ? '0 0 0 2px rgb(var(--neutral-900))' : '0 0 0 1px rgb(var(--neutral-300))',
              }}
            />
          )
        })}
      </div>

      <div className="space-y-1">
        <label htmlFor="console-color" className="block text-sm font-medium text-neutral-800">직접 입력</label>
        <input
          id="console-color"
          className={`${inputCls} w-40 font-mono ${valid && readable ? '' : 'border-red-400 focus:border-red-400 focus:ring-red-200'}`}
          value={value}
          disabled={!current}
          onChange={(e) => { setValue(e.target.value); setSaved(false) }}
          spellCheck={false}
          autoComplete="off"
        />
        <p className={`text-xs ${valid && readable ? 'text-neutral-500' : 'text-red-600'}`}>
          {!valid
            ? '#과 16진수 6자리로 적어 주세요.'
            : !readable
              ? '너무 밝아 흰 글자가 잘 안 보입니다. 더 진한 색을 골라 주세요.'
              : '#과 16진수 6자리.'}
        </p>
      </div>

      {similar.length > 0 && (
        <div className="rounded-md bg-amber-50 px-3 py-2 text-sm text-amber-800">
          {similar.map((p) => look.name(p.key)).join(', ')}와(과) 색이 비슷합니다. 엉뚱한 프로젝트에서 작업하지 않도록 확실히 다른 색을 고르세요.
        </div>
      )}

      <div className="rounded-lg overflow-hidden border border-neutral-200">
        <div className="px-4 py-3 text-white font-bold" style={{ backgroundColor: valid ? look.display(hex) : '#9ca3af' }}>
          {look.name(target)}
        </div>
        <div className="px-4 py-2 text-xs text-neutral-500">미리보기</div>
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <Button variant="primary" onClick={() => void save(hex)} disabled={saving || !changed || !readable}>
          저장
        </Button>
        {current?.color && (
          <Button onClick={() => void save(null)} disabled={saving}>
            기본색으로
          </Button>
        )}
        {saved && <span className="text-sm text-green-600 whitespace-nowrap">저장했습니다.</span>}
        {current?.updated_at && current.color && (
          <span className="text-xs text-neutral-400">
            마지막 수정 {formatDateTime(current.updated_at)}{current.updated_by ? ` · ${current.updated_by}` : ''}
          </span>
        )}
      </div>
    </WhiteCard>
  )
}

function zonedText(iso: string | null, tz: string): string {
  if (!iso) return '-'
  try {
    return new Intl.DateTimeFormat('ko-KR', {
      timeZone: tz, year: 'numeric', month: 'long', day: 'numeric', weekday: 'short', hour: '2-digit', minute: '2-digit', hour12: false,
    }).format(new Date(iso))
  } catch {
    return formatDateTime(iso)
  }
}

function TimeZoneCard({ target, report }: { target: ProjectTarget; report: (e: unknown, fallback: string) => void }) {
  const [current, setCurrent] = useState<GameSettings | null>(null)
  const [tz, setTz] = useState('')
  const [asking, setAsking] = useState(false)
  const [busy, setBusy] = useState(false)
  const [saveError, setSaveError] = useState('')

  useEffect(() => {
    void (async () => {
      try {
        const s = await callAdmin<GameSettings>(target, 'game.settingsGet')
        setCurrent(s)
        setTz(s.time_zone)
      } catch (e: unknown) {
        report(e, '시간대 설정을 불러오지 못했습니다.')
      }
    })()
  }, [target, report])

  const save = async () => {
    setBusy(true); setSaveError('')
    try {
      const s = await callAdmin<GameSettings>(target, 'game.settingsUpdate', { timeZone: tz })
      forgetGameTimeZone(target)
      setCurrent(s); setTz(s.time_zone); setAsking(false)
    } catch (e: unknown) {
      if (e instanceof NotAuthenticatedError) { report(e, ''); return }
      setSaveError(e instanceof Error ? e.message : '저장하지 못했습니다.')
    } finally {
      setBusy(false)
    }
  }

  const options = current && !TIME_ZONES.some(([v]) => v === current.time_zone)
    ? [[current.time_zone, current.time_zone] as const, ...TIME_ZONES]
    : TIME_ZONES
  const label = (v: string) => options.find(([k]) => k === v)?.[1] ?? v

  return (
    <WhiteCard className="p-5 space-y-5 max-w-2xl">
      <CardHeader
        title="프로젝트 시간대"
        description="날짜를 나누는 기준 시간대를 정합니다."
      />
      <div className="space-y-1.5">
        <label className="block text-sm font-medium text-neutral-700">시간대</label>
        <select value={tz} onChange={(e) => setTz(e.target.value)} className={inputCls + ' max-w-sm'} disabled={!current}>
          {options.map(([v, l]) => (<option key={v} value={v}>{l}</option>))}
        </select>
        {current && (
          <p className="text-xs text-neutral-500">
            지금 7일 유예로 탈퇴하면 {timeZoneName(current.time_zone)} <span className="font-medium text-neutral-700">{zonedText(current.example_deadline, current.time_zone)}</span>에 삭제됩니다.
          </p>
        )}
        <p className="text-xs text-neutral-400">
          바꾸면 통계의 하루·대시보드의 오늘·날짜 검색·탈퇴 자정·새 우편 예약·새 리더보드 회차가 새 시간대로 셉니다. 지난 기록과 이미 만든 예약은 그대로입니다.
        </p>
      </div>
      <div className="flex items-center gap-3">
        <Button variant="primary" onClick={() => { setSaveError(''); setAsking(true) }} disabled={!current || tz === current.time_zone}>
          저장
        </Button>
        {current?.updated_at && (
          <span className="text-xs text-neutral-400">
            마지막 수정 {formatDateTime(current.updated_at)}{current.updated_by ? ` · ${current.updated_by}` : ''}
          </span>
        )}
      </div>
      <ConfirmDialog
        open={asking}
        title="프로젝트 시간대를 바꿀까요?"
        confirmLabel="바꾸기"
        busy={busy}
        error={saveError || undefined}
        onConfirm={() => { void save() }}
        onCancel={() => setAsking(false)}
        description="통계의 하루가 바뀌는 시각이 달라져, 바꾼 날의 숫자는 두 기준이 섞입니다. 지난 기록은 다시 세지 않습니다."
      >
        <div className="rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-sm">
          {current ? label(current.time_zone) : ''} → <span className="font-medium">{label(tz)}</span>
        </div>
      </ConfirmDialog>
    </WhiteCard>
  )
}

type Tab = 'store' | 'time' | 'mail' | 'friend' | 'lobby' | 'push' | 'limit' | 'console'

const TABS: readonly (readonly [Tab, string])[] = [
  ['store', '서비스 연결'],
  ['time', '시간대'],
  ['mail', '우편'],
  ['friend', '친구'],
  ['lobby', '매치 로비'],
  ['push', '푸시 알림'],
  ['limit', '요청 제한'],
  ['console', '프로젝트 정보'],
]

export default function Settings({
  target,
  onUnauthenticated,
}: {
  target: ProjectTarget
  onUnauthenticated: () => void
}) {
  const [error, setError] = useState('')
  const [tab, setTab] = useState<Tab>('store')
  // 요청 제한 설정을 저장하면 제한 목록을 다시 읽는다(끄면 목록이 빈다).
  const [limitReload, setLimitReload] = useState(0)

  const report = useCallback((e: unknown, fallback: string) => {
    if (e instanceof NotAuthenticatedError) { onUnauthenticated(); return }
    setError(e instanceof Error ? e.message : fallback)
  }, [onUnauthenticated])

  return (
    <div className="space-y-4">
      <PageHeader title="설정" description="프로젝트 운영에 필요한 설정을 정합니다." />
      <ErrorBanner message={error} onDismiss={() => setError('')} />

      <Tabs value={tab} onChange={setTab} tabs={TABS} />

      {tab === 'store' && <StoreStatusCard target={target} report={report} />}
      {tab === 'console' && <ConsoleCards target={target} report={report} />}
      {tab === 'time' && <TimeZoneCard target={target} report={report} />}
      {tab === 'mail' && (
        <LimitsForm
          target={target}
          report={report}
          title="우편"
          description="플레이어 우편함에서 기간이 지난 우편을 얼마 동안 보여 줄지 정합니다."
          getAction="mails.settingsGet"
          updateAction="mails.settingsUpdate"
          fields={MAIL_FIELDS}
          confirmTitle="우편 설정을 바꿀까요?"
        />
      )}
      {tab === 'friend' && (
        <LimitsForm
          target={target}
          report={report}
          title="친구"
          description="한 사람이 가질 수 있는 친구 수와 친구 요청을 보내는 빈도를 정합니다."
          getAction="friends.settingsGet"
          updateAction="friends.settingsUpdate"
          fields={FRIEND_FIELDS}
          confirmTitle="친구 설정을 바꿀까요?"
        />
      )}
      {tab === 'lobby' && (
        <LimitsForm
          target={target}
          report={report}
          title="매치 로비"
          description="함께 할 사람을 초대하는 빈도와, 시작하지 않은 방이 닫히는 시간을 정합니다."
          getAction="matchLobby.settingsGet"
          updateAction="matchLobby.settingsUpdate"
          fields={LOBBY_FIELDS}
          confirmTitle="매치 로비 설정을 바꿀까요?"
        />
      )}
      {tab === 'push' && <PushSettings target={target} report={report} onUnauthenticated={onUnauthenticated} />}
      {tab === 'limit' && (
        <div className="space-y-4">
          <LimitsForm
            target={target}
            report={report}
            title="요청 제한"
            description="짧은 시간에 요청을 지나치게 많이 보내는 계정을 잠시 막습니다."
            getAction="requestLimit.settingsGet"
            updateAction="requestLimit.settingsUpdate"
            fields={REQUEST_LIMIT_FIELDS}
            toggle={REQUEST_LIMIT_TOGGLE}
            confirmTitle="요청 제한 설정을 바꿀까요?"
            onSaved={() => setLimitReload((n) => n + 1)}
          />
          <RequestLimitBlocksCard key={limitReload} target={target} report={report} />
        </div>
      )}
    </div>
  )
}
