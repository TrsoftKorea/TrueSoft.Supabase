// 고객센터 창 — WKWebView 를 시트로 띄운다. C# 의 CustomerCenterWebView 가 열고, 닫혔는지는 IsOpen 으로 본다.
// 닫는 길은 셋이다: 페이지의 닫기 단추(truebase-support://close 를 여기서 가로챈다), 아래로 쓸어내리기(시트),
// 페이지가 안 떠도 쓸어내리면 닫힌다 — 페이지 단추 하나에만 기대면 페이지를 못 불러왔을 때 빠져나올 길이 없다.
// 고객센터 페이지 밖의 주소는 창 안에서 열지 않고 기본 브라우저로 넘긴다 — 창 안에서 다른 곳으로 가면 페이지의 닫기 단추가 사라진다.
// WebKit 링크·카메라 사용 문구는 Editor/CustomerCenterBuildPostProcessor 가 넣는다.
#import <UIKit/UIKit.h>
#import <WebKit/WebKit.h>

extern UIViewController* UnityGetGLViewController();

// 창이 실제로 떠 있는 동안만 YES. 열어 달라고 한 순간이 아니라 화면에 뜬 뒤에 켠다 — 다른 화면이 떠 있어
// 띄우기가 조용히 실패하면 켜진 채 남아 C# 이 영영 기다리기 때문이다.
static BOOL gTrueSoftCustomerCenterOpen = NO;

@interface TrueSoftCustomerCenterController : UIViewController <WKNavigationDelegate, WKUIDelegate, UIAdaptivePresentationControllerDelegate>
@property (nonatomic, copy) NSString* url;
@property (nonatomic, copy) NSString* host;
@property (nonatomic, strong) WKWebView* web;
@end

@implementation TrueSoftCustomerCenterController

- (void)viewDidLoad
{
    [super viewDidLoad];
    if (@available(iOS 13.0, *)) self.view.backgroundColor = UIColor.systemBackgroundColor;
    else self.view.backgroundColor = UIColor.whiteColor;

    WKWebViewConfiguration* config = [[WKWebViewConfiguration alloc] init];
    self.web = [[WKWebView alloc] initWithFrame:CGRectZero configuration:config];
    self.web.navigationDelegate = self;
    self.web.UIDelegate = self;
    self.web.translatesAutoresizingMaskIntoConstraints = NO;
    [self.view addSubview:self.web];

    UILayoutGuide* safe = self.view.safeAreaLayoutGuide;
    [NSLayoutConstraint activateConstraints:@[
        [self.web.topAnchor constraintEqualToAnchor:safe.topAnchor],
        [self.web.bottomAnchor constraintEqualToAnchor:safe.bottomAnchor],
        [self.web.leadingAnchor constraintEqualToAnchor:self.view.leadingAnchor],
        [self.web.trailingAnchor constraintEqualToAnchor:self.view.trailingAnchor],
    ]];

    NSURL* target = [NSURL URLWithString:self.url];
    self.host = target.host;
    if (target) [self.web loadRequest:[NSURLRequest requestWithURL:target]];
}

- (void)closeWindow
{
    [self dismissViewControllerAnimated:YES completion:^{ gTrueSoftCustomerCenterOpen = NO; }];
}

// 쓸어내려 닫았을 때.
- (void)presentationControllerDidDismiss:(UIPresentationController*)presentationController
{
    gTrueSoftCustomerCenterOpen = NO;
}

- (void)viewDidDisappear:(BOOL)animated
{
    [super viewDidDisappear:animated];
    if (self.isBeingDismissed) gTrueSoftCustomerCenterOpen = NO;
}

- (void)webView:(WKWebView*)webView decidePolicyForNavigationAction:(WKNavigationAction*)action
    decisionHandler:(void (^)(WKNavigationActionPolicy))decisionHandler
{
    NSURL* url = action.request.URL;
    if ([url.scheme isEqualToString:@"truebase-support"])
    {
        decisionHandler(WKNavigationActionPolicyCancel);
        [self closeWindow];
        return;
    }
    BOOL inPage = url.host == nil || self.host == nil || [url.host isEqualToString:self.host]
        || [url.scheme isEqualToString:@"about"] || [url.scheme isEqualToString:@"data"] || [url.scheme isEqualToString:@"blob"];
    if (!inPage && action.targetFrame.isMainFrame)
    {
        decisionHandler(WKNavigationActionPolicyCancel);
        [[UIApplication sharedApplication] openURL:url options:@{} completionHandler:nil];
        return;
    }
    decisionHandler(WKNavigationActionPolicyAllow);
}

// 새 창으로 여는 링크는 창 안에서 열지 않고 위 규칙대로 기본 브라우저로 넘긴다.
- (WKWebView*)webView:(WKWebView*)webView createWebViewWithConfiguration:(WKWebViewConfiguration*)configuration
    forNavigationAction:(WKNavigationAction*)action windowFeatures:(WKWindowFeatures*)windowFeatures
{
    if (action.request.URL) [[UIApplication sharedApplication] openURL:action.request.URL options:@{} completionHandler:nil];
    return nil;
}

@end

extern "C"
{
    void TrueSoftCustomerCenter_Open(const char* url)
    {
        if (url == NULL || gTrueSoftCustomerCenterOpen) return;
        NSString* target = [NSString stringWithUTF8String:url];
        dispatch_async(dispatch_get_main_queue(), ^{
            UIViewController* root = UnityGetGLViewController();
            while (root.presentedViewController) root = root.presentedViewController;
            if ([root isKindOfClass:[TrueSoftCustomerCenterController class]] || root.isBeingPresented || root.isBeingDismissed) return;

            TrueSoftCustomerCenterController* controller = [[TrueSoftCustomerCenterController alloc] init];
            controller.url = target;
            controller.modalPresentationStyle = UIModalPresentationPageSheet;
            controller.presentationController.delegate = controller;
            [root presentViewController:controller animated:YES completion:^{ gTrueSoftCustomerCenterOpen = YES; }];
        });
    }

    bool TrueSoftCustomerCenter_IsOpen()
    {
        return gTrueSoftCustomerCenterOpen;
    }
}
