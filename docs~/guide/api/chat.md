# 채팅 API

| 메서드 | 설명 |
|--------|------|
| [`GetChatChannelsAsync`](/guide/chat/channels) | 사용 가능한 채널 목록 |
| [`SubscribeChat`](/guide/chat/subscribe) | 채널 구독 · 새 메시지 수신 |
| [`SendChatAsync`](/guide/chat/send) | 메시지 발송 |

채널 생성·설정, 대화 삭제, 채팅 차단은 어드민(Retool) 전용이라 SDK에 없습니다.

::: tip
채널을 여러 개 구독해도 서버 요청은 한 번으로 묶입니다. 조회 간격은 대화량에 따라 SDK가 조절합니다.
:::
