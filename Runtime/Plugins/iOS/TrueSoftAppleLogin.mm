// TrueSoft Supabase SDK — Sign in with Apple 네이티브 브릿지 (iOS)
//
// C# 측(AppleLoginBridge)에서 SHA256 해시된 nonce를 받아 ASAuthorizationController로
// Apple 로그인을 수행하고, 결과(identityToken 등)를 UnitySendMessage로 돌려줍니다.
// raw nonce 생성·해시는 C#에서 처리하므로 이 파일은 crypto를 다루지 않습니다.

#import <AuthenticationServices/AuthenticationServices.h>
#import <UIKit/UIKit.h>
#import <Foundation/Foundation.h>

extern "C" {
    void UnitySendMessage(const char* obj, const char* method, const char* msg);
}

// 필드 구분자(|||)가 값 안에 들어가면 깨지므로 C# 측 Unescape와 동일하게 이스케이프합니다.
static NSString* TS_Escape(NSString* s) {
    if (s == nil) return @"";
    return [s stringByReplacingOccurrencesOfString:@"|||" withString:@"%7C%7C%7C"];
}

API_AVAILABLE(ios(13.0))
@interface TrueSoftAppleLoginDelegate : NSObject <ASAuthorizationControllerDelegate, ASAuthorizationControllerPresentationContextProviding>
@property (nonatomic, copy) NSString* targetObject;
@end

// 비동기 콜백 동안 델리게이트가 해제되지 않도록 보관합니다.
static TrueSoftAppleLoginDelegate* _ts_appleDelegate = nil;

@implementation TrueSoftAppleLoginDelegate

- (ASPresentationAnchor)presentationAnchorForAuthorizationController:(ASAuthorizationController *)controller API_AVAILABLE(ios(13.0)) {
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

- (void)authorizationController:(ASAuthorizationController *)controller
   didCompleteWithAuthorization:(ASAuthorization *)authorization API_AVAILABLE(ios(13.0)) {
    NSString* target = self.targetObject;

    if (![authorization.credential isKindOfClass:[ASAuthorizationAppleIDCredential class]]) {
        UnitySendMessage([target UTF8String], "OnAppleLoginError", "apple_credential_unexpected");
        _ts_appleDelegate = nil;
        return;
    }

    ASAuthorizationAppleIDCredential* cred = (ASAuthorizationAppleIDCredential*)authorization.credential;

    NSString* idToken = @"";
    if (cred.identityToken != nil) {
        idToken = [[NSString alloc] initWithData:cred.identityToken encoding:NSUTF8StringEncoding] ?: @"";
    }

    if (idToken.length == 0) {
        UnitySendMessage([target UTF8String], "OnAppleLoginError", "apple_id_token_empty");
        _ts_appleDelegate = nil;
        return;
    }

    NSString* appleUserId = cred.user ?: @"";
    NSString* email = cred.email ?: @"";
    NSString* given = cred.fullName.givenName ?: @"";
    NSString* family = cred.fullName.familyName ?: @"";

    // payload: idToken|||appleUserId|||email|||givenName|||familyName
    NSString* payload = [NSString stringWithFormat:@"%@|||%@|||%@|||%@|||%@",
                         TS_Escape(idToken), TS_Escape(appleUserId),
                         TS_Escape(email), TS_Escape(given), TS_Escape(family)];

    UnitySendMessage([target UTF8String], "OnAppleLoginSuccess", [payload UTF8String]);
    _ts_appleDelegate = nil;
}

- (void)authorizationController:(ASAuthorizationController *)controller
           didCompleteWithError:(NSError *)error API_AVAILABLE(ios(13.0)) {
    NSString* target = self.targetObject;
    // ASAuthorizationErrorCanceled = 1001 (사용자가 직접 취소)
    NSString* code = [NSString stringWithFormat:@"%ld", (long)error.code];
    UnitySendMessage([target UTF8String], "OnAppleLoginError", [code UTF8String]);
    _ts_appleDelegate = nil;
}

@end

extern "C" void TrueSoftAppleLogin_SignIn(const char* gameObjectName, const char* hashedNonce) {
    NSString* target = (gameObjectName != NULL)
        ? [NSString stringWithUTF8String:gameObjectName] : @"";

    if (@available(iOS 13.0, *)) {
        _ts_appleDelegate = [[TrueSoftAppleLoginDelegate alloc] init];
        _ts_appleDelegate.targetObject = target;

        ASAuthorizationAppleIDProvider* provider = [[ASAuthorizationAppleIDProvider alloc] init];
        ASAuthorizationAppleIDRequest* request = [provider createRequest];
        request.requestedScopes = @[ASAuthorizationScopeFullName, ASAuthorizationScopeEmail];
        if (hashedNonce != NULL && strlen(hashedNonce) > 0) {
            request.nonce = [NSString stringWithUTF8String:hashedNonce];
        }

        ASAuthorizationController* controller =
            [[ASAuthorizationController alloc] initWithAuthorizationRequests:@[request]];
        controller.delegate = _ts_appleDelegate;
        controller.presentationContextProvider = _ts_appleDelegate;
        [controller performRequests];
    } else {
        UnitySendMessage([target UTF8String], "OnAppleLoginError", "apple_signin_requires_ios_13");
    }
}
