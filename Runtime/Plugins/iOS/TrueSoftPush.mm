// TrueSoft Supabase SDK — 푸시 알림(iOS). 알림 권한을 묻고 APNs 기기 토큰을 받는다.
// 토큰은 Unity 앱 델리게이트가 보내는 알림(kUnityDidRegisterForRemoteNotificationsWithDeviceToken)으로 받는다 —
// Unity 의 Mobile Notifications 패키지도 같은 알림을 듣는다(Classes/PluginBase/AppDelegateListener.h).
// 이 알림은 Preprocessor.h 의 UNITY_USES_REMOTE_NOTIFICATIONS 가 1일 때만 나간다 — PushIosPostProcessor 가 켠다.
// C#(PushNative) 이 Start 를 부른 뒤 State 를 짧게 물어본다. 확인 못 함: Windows 라 이 파일을 컴파일해 보지 못했다.
#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <UserNotifications/UserNotifications.h>
#include "PluginBase/AppDelegateListener.h"

// 0 대기 · 1 받음 · 2 거부 · 3 실패
static volatile int s_state = 0;
static NSString* s_token = nil;
static NSString* s_error = nil;
static id s_registered = nil;
static id s_failed = nil;

static char* TrueSoftPushCopy(NSString* s)
{
    if (s == nil) return NULL;
    const char* utf8 = [s UTF8String];
    char* out = (char*)malloc(strlen(utf8) + 1);
    strcpy(out, utf8);
    return out;
}

static void TrueSoftPushListen()
{
    if (s_registered != nil) return;
    NSNotificationCenter* center = [NSNotificationCenter defaultCenter];
    s_registered = [center addObserverForName:kUnityDidRegisterForRemoteNotificationsWithDeviceToken object:nil queue:nil usingBlock:^(NSNotification* note) {
        NSData* data = (NSData*)note.userInfo;
        if (![data isKindOfClass:[NSData class]]) { s_error = @"no_token"; s_state = 3; return; }
        const unsigned char* bytes = (const unsigned char*)data.bytes;
        NSMutableString* hex = [NSMutableString stringWithCapacity:data.length * 2];
        for (NSUInteger i = 0; i < data.length; i++) [hex appendFormat:@"%02x", bytes[i]];
        s_token = [hex copy];
        s_state = 1;
    }];
    s_failed = [center addObserverForName:kUnityDidFailToRegisterForRemoteNotificationsWithError object:nil queue:nil usingBlock:^(NSNotification* note) {
        NSError* error = (NSError*)note.userInfo;
        s_error = [error isKindOfClass:[NSError class]] ? error.localizedDescription : @"register_failed";
        s_state = 3;
    }];
}

extern "C" void TrueSoftPush_Start()
{
    s_state = 0;
    s_token = nil;
    s_error = nil;
    TrueSoftPushListen();
    UNUserNotificationCenter* center = [UNUserNotificationCenter currentNotificationCenter];
    [center requestAuthorizationWithOptions:(UNAuthorizationOptionAlert | UNAuthorizationOptionSound | UNAuthorizationOptionBadge)
                          completionHandler:^(BOOL granted, NSError* error) {
        if (!granted) {
            s_error = error != nil ? error.localizedDescription : nil;
            s_state = 2;
            return;
        }
        // 애플 권장: 토큰을 기억해 두지 말고 실행할 때마다 다시 받는다.
        dispatch_async(dispatch_get_main_queue(), ^{
            [[UIApplication sharedApplication] registerForRemoteNotifications];
        });
    }];
}

// ── 게임이 앞에 떠 있을 때 온 알림 ─────────────────────────────────────────────
// 앞에 떠 있을 때 온 알림은 iOS 가 띄우지 않는다(알림 센터 대리자가 없으면). Unity 앱 델리게이트가
// didReceiveRemoteNotification:fetchCompletionHandler: 에서 kUnityDidReceiveRemoteNotification 을 보내고,
// 애플 문서상 이 메서드는 앱이 앞에 있을 때도 불린다. 알림을 눌러 앱이 앞으로 올 때 다시 불릴 수 있어
// 그 순간 앱이 활성일 때만 담는다. C# 이 PollReceived 로 꺼내 Supabase.OnPushReceived 로 알린다.
static const NSUInteger kReceivedMax = 20;
static NSMutableArray<NSString*>* s_received = nil;
static id s_receivedObserver = nil;

static void TrueSoftPushListenReceived()
{
    if (s_receivedObserver != nil) return;
    s_received = [NSMutableArray array];
    s_receivedObserver = [[NSNotificationCenter defaultCenter] addObserverForName:kUnityDidReceiveRemoteNotification object:nil queue:nil usingBlock:^(NSNotification* note) {
        if ([UIApplication sharedApplication].applicationState != UIApplicationStateActive) return;
        NSDictionary* info = (NSDictionary*)note.userInfo;
        if (![info isKindOfClass:[NSDictionary class]]) return;

        NSString* title = @"";
        NSString* body = @"";
        id alert = [info[@"aps"] isKindOfClass:[NSDictionary class]] ? info[@"aps"][@"alert"] : nil;
        if ([alert isKindOfClass:[NSDictionary class]]) {
            if ([alert[@"title"] isKindOfClass:[NSString class]]) title = alert[@"title"];
            if ([alert[@"body"] isKindOfClass:[NSString class]]) body = alert[@"body"];
        } else if ([alert isKindOfClass:[NSString class]]) {
            body = alert;
        }
        NSMutableDictionary* data = [NSMutableDictionary dictionary];
        for (id key in info) {
            if (![key isKindOfClass:[NSString class]] || [key isEqualToString:@"aps"]) continue;
            id value = info[key];
            if ([value isKindOfClass:[NSString class]]) data[key] = value;
            else if ([value isKindOfClass:[NSNumber class]]) data[key] = [value stringValue];
        }
        // 받은 시각(밀리초) — 게임이 한참 뒤에 구독하면 지난 알림을 C# 이 거른다.
        NSNumber* at = @((long long)([[NSDate date] timeIntervalSince1970] * 1000.0));
        NSData* json = [NSJSONSerialization dataWithJSONObject:@{ @"title": title, @"body": body, @"data": data, @"at": at } options:0 error:nil];
        if (json == nil) return;
        NSString* line = [[NSString alloc] initWithData:json encoding:NSUTF8StringEncoding];
        @synchronized (s_received) {
            if (s_received.count >= kReceivedMax) [s_received removeObjectAtIndex:0];
            [s_received addObject:line];
        }
    }];
}

// 처음 부를 때 듣기 시작한다 — C# 은 게임이 OnPushReceived 를 구독한 뒤에야 부른다.
extern "C" char* TrueSoftPush_PollReceived()
{
    TrueSoftPushListenReceived();
    @synchronized (s_received) {
        if (s_received.count == 0) return NULL;
        NSString* line = s_received[0];
        [s_received removeObjectAtIndex:0];
        return TrueSoftPushCopy(line);
    }
}

// 로그아웃·계정 전환 때 쌓인 알림을 버린다 — 앞 계정 몫이 다음 계정에게 가지 않게.
extern "C" void TrueSoftPush_ClearReceived()
{
    if (s_received == nil) return;
    @synchronized (s_received) {
        [s_received removeAllObjects];
    }
}

extern "C" int TrueSoftPush_State() { return s_state; }
extern "C" char* TrueSoftPush_Token() { return TrueSoftPushCopy(s_token); }
extern "C" char* TrueSoftPush_Error() { return TrueSoftPushCopy(s_error); }
