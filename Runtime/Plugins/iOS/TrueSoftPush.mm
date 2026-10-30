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

extern "C" int TrueSoftPush_State() { return s_state; }
extern "C" char* TrueSoftPush_Token() { return TrueSoftPushCopy(s_token); }
extern "C" char* TrueSoftPush_Error() { return TrueSoftPushCopy(s_error); }
