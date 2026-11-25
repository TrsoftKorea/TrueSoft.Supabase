# 고객센터 API

| 메서드 | 설명 |
|--------|------|
| [`OpenCustomerCenterAsync`](/guide/customer-center/open) | 고객센터 창 열기 |
| [`GetCustomerCenterUnreadCountAsync`](/guide/customer-center/unread) | 새 답변이 있는 문의 수 |
| [`OpenPrivacyPolicyAsync`](/guide/customer-center/privacy) | 개인정보처리방침 창 열기 |
| [`OpenTermsAsync`](/guide/customer-center/terms) | 이용약관 창 열기 |
| [`GetLegalVersionsAsync`](/guide/customer-center/legal-versions) | 지금 시행 중인 약관 버전 |
| [`GetLegalConsentAsync`](/guide/customer-center/legal-consent) | 내 약관 동의와 다시 물어야 하는지 |
| [`SaveLegalConsentAsync`](/guide/customer-center/legal-consent-save) | 약관 동의 남기기 |

문의 읽기·쓰기는 SDK가 띄우는 창이 직접 하므로 게임이 부를 메서드가 따로 없습니다. 답변·분류 관리는 운영 콘솔 **고객센터**에서 합니다.
