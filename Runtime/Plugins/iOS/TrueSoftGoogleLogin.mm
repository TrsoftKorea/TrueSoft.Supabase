// TrueSoft Supabase SDK — Google 로그인 네이티브 브릿지 (iOS)
//
// C# 측(GoogleLoginBridge)이 만든 구글 인증 URL을 iOS 기본 로그인 창(ASWebAuthenticationSession)으로 열고,
// 구글이 돌려보낸 콜백 URL을 UnitySendMessage로 그대로 돌려줍니다.
// PKCE·state 검증·토큰 교환은 C#이 하므로 이 파일은 창을 여닫는 일만 합니다.

#import <AuthenticationServices/AuthenticationServices.h>
#import <UIKit/UIKit.h>
#import <Foundation/Foundation.h>

extern "C" {
    void UnitySendMessage(const char* obj, const char* method, const char* msg);
}

API_AVAILABLE(ios(13.0))
@interface TrueSoftGoogleLoginAnchor : NSObject <ASWebAuthenticationPresentationContextProviding>
@end

@implementation TrueSoftGoogleLoginAnchor

- (ASPresentationAnchor)presentationAnchorForWebAuthenticationSession:(ASWebAuthenticationSession *)session API_AVAILABLE(ios(13.0)) {
    for (UIScene* scene in UIApplication.sharedApplication.connectedScenes) {
        if ([scene isKindOfClass:[UIWindowScene class]]) {
            UIWindowScene* windowScene = (UIWindowScene*)scene;
            for (UIWindow* window in windowScene.windows) {
                if (window.isKeyWindow) return window;
            }
            if (windowScene.windows.count > 0) return windowScene.windows.firstObject;
        }
    }
    return UIApplication.sharedApplication.windows.firstObject;
}

@end

// 창이 떠 있는 동안 세션·앵커가 해제되지 않도록 보관합니다.
static ASWebAuthenticationSession* _ts_googleSession = nil;
static TrueSoftGoogleLoginAnchor* _ts_googleAnchor = nil;
// 새 로그인이 이전 창을 취소하면 이전 창의 "취소됨" 완료가 새 로그인의 결과로 잘못 전달된다. 세대 번호로 걸러낸다.
// (C# 쪽도 요청 번호로 한 번 더 거른다 — 이미 대기열에 들어간 메시지는 여기서 못 막는다.)
static NSInteger _ts_googleGeneration = 0;

// 메시지는 "요청번호|||값" 꼴. C# 이 요청 번호로 지난 요청의 늦은 결과를 버린다.
static void TS_GoogleSend(NSString* target, const char* method, NSString* requestId, NSString* value) {
    NSString* msg = [NSString stringWithFormat:@"%@|||%@", requestId ?: @"", value ?: @""];
    dispatch_async(dispatch_get_main_queue(), ^{
        UnitySendMessage([target UTF8String], method, [msg UTF8String]);
    });
}

extern "C" void TrueSoftGoogleLogin_Start(const char* gameObjectName, const char* authUrl,
                                          const char* callbackScheme, const char* requestIdCStr) {
    NSString* target = (gameObjectName != NULL) ? [NSString stringWithUTF8String:gameObjectName] : @"";
    NSString* requestId = (requestIdCStr != NULL) ? [NSString stringWithUTF8String:requestIdCStr] : @"";

    if (@available(iOS 13.0, *)) {
        NSURL* url = (authUrl != NULL) ? [NSURL URLWithString:[NSString stringWithUTF8String:authUrl]] : nil;
        NSString* scheme = (callbackScheme != NULL) ? [NSString stringWithUTF8String:callbackScheme] : @"";
        if (url == nil || scheme.length == 0) {
            TS_GoogleSend(target, "OnGoogleIosError", requestId, @"google_auth_request_invalid");
            return;
        }

        // 세대를 먼저 올려야 cancel 이 완료 핸들러를 그 자리에서 부르더라도 옛 창의 결과가 걸러진다.
        NSInteger generation = ++_ts_googleGeneration;
        ASWebAuthenticationSession* previous = _ts_googleSession;
        _ts_googleSession = nil;
        _ts_googleAnchor = nil;
        [previous cancel];

        _ts_googleAnchor = [[TrueSoftGoogleLoginAnchor alloc] init];
        _ts_googleSession = [[ASWebAuthenticationSession alloc]
            initWithURL:url
            callbackURLScheme:scheme
            completionHandler:^(NSURL* callbackURL, NSError* error) {
                // 전역 상태는 메인 스레드에서만 만진다(Start 도 메인에서 불린다).
                dispatch_async(dispatch_get_main_queue(), ^{
                    if (generation != _ts_googleGeneration) return;
                    _ts_googleSession = nil;
                    _ts_googleAnchor = nil;

                    if (error != nil) {
                        if ([error.domain isEqualToString:ASWebAuthenticationSessionErrorDomain] &&
                            error.code == ASWebAuthenticationSessionErrorCodeCanceledLogin) {
                            TS_GoogleSend(target, "OnGoogleIosError", requestId, @"google_signin_cancelled");
                        } else {
                            TS_GoogleSend(target, "OnGoogleIosError", requestId,
                                          [NSString stringWithFormat:@"google_auth_session_error:%@:%ld",
                                           error.domain, (long)error.code]);
                        }
                        return;
                    }

                    if (callbackURL == nil) {
                        TS_GoogleSend(target, "OnGoogleIosError", requestId, @"google_auth_callback_empty");
                        return;
                    }

                    TS_GoogleSend(target, "OnGoogleIosRedirect", requestId, callbackURL.absoluteString);
                });
            }];

        _ts_googleSession.presentationContextProvider = _ts_googleAnchor;
        // 사파리와 로그인 상태를 공유해, 이미 구글에 로그인한 기기에서는 계정만 고르면 끝나게 한다.
        _ts_googleSession.prefersEphemeralWebBrowserSession = NO;

        if (![_ts_googleSession start]) {
            _ts_googleSession = nil;
            _ts_googleAnchor = nil;
            TS_GoogleSend(target, "OnGoogleIosError", requestId, @"google_auth_session_start_failed");
        }
    } else {
        TS_GoogleSend(target, "OnGoogleIosError", requestId, @"google_signin_requires_ios_13");
    }
}
