package com.truesoft.supabase;

import android.app.Activity;
import android.app.Dialog;
import android.app.Fragment;
import android.content.ClipData;
import android.content.DialogInterface;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;
import android.view.KeyEvent;
import android.view.ViewGroup;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceRequest;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;

/**
 * 고객센터 창 — WebView 를 전체 화면 대화상자로 띄운다. C# 의 CustomerCenterWebView 가 열고, 닫혔는지는 isOpen 으로 본다.
 * 페이지의 닫기 단추는 truebase-support://close 로 가고, 여기서 가로채 창을 닫는다. 뒤로 가기도 창을 닫는다.
 * 그림 첨부(input type=file)는 WebView 가 스스로 못 열어 사진 고르기 화면을 대신 띄운다 — 결과를 받으려고
 * 보이지 않는 Fragment 를 잠깐 붙인다(게임 Activity 의 onActivityResult 를 건드리지 않으려고).
 */
public final class TrueSoftCustomerCenter {
    private static final String CLOSE_SCHEME = "truebase-support";
    private static final int REQUEST_FILES = 0x7353;

    private static volatile boolean open;
    private static Dialog dialog;
    private static ValueCallback<Uri[]> pendingFiles;

    private TrueSoftCustomerCenter() {}

    public static boolean isOpen() {
        return open;
    }

    public static void open(final Activity activity, final String url) {
        if (open) return;
        open = true;
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                try {
                    show(activity, url);
                } catch (Throwable t) {
                    open = false;
                    dialog = null;
                }
            }
        });
    }

    private static void show(final Activity activity, String url) {
        final WebView web = new WebView(activity);
        WebSettings settings = web.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setDomStorageEnabled(true);
        settings.setAllowFileAccess(false);

        final String pageHost = Uri.parse(url).getHost();
        web.setWebViewClient(new WebViewClient() {
            @Override
            public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
                Uri target = request.getUrl();
                if (CLOSE_SCHEME.equals(target.getScheme())) {
                    close();
                    return true;
                }
                // 고객센터 페이지 밖의 주소는 창 안에서 열지 않고 기본 브라우저로 넘긴다.
                if (pageHost != null && target.getHost() != null && !pageHost.equals(target.getHost())) {
                    try {
                        activity.startActivity(new Intent(Intent.ACTION_VIEW, target));
                    } catch (Throwable ignored) {
                        // 열 앱이 없으면 그냥 머문다.
                    }
                    return true;
                }
                return false;
            }
        });

        web.setWebChromeClient(new WebChromeClient() {
            @Override
            public boolean onShowFileChooser(WebView view, ValueCallback<Uri[]> callback, FileChooserParams params) {
                if (pendingFiles != null) pendingFiles.onReceiveValue(null);
                pendingFiles = callback;
                Intent pick = new Intent(Intent.ACTION_GET_CONTENT);
                pick.addCategory(Intent.CATEGORY_OPENABLE);
                pick.setType("image/*");
                pick.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, params.getMode() == FileChooserParams.MODE_OPEN_MULTIPLE);
                FileResult.start(activity, Intent.createChooser(pick, null));
                return true;
            }
        });

        Dialog d = new Dialog(activity, android.R.style.Theme_DeviceDefault_NoActionBar);
        d.setContentView(web, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        d.setOnKeyListener(new DialogInterface.OnKeyListener() {
            @Override
            public boolean onKey(DialogInterface di, int keyCode, KeyEvent event) {
                if (keyCode != KeyEvent.KEYCODE_BACK || event.getAction() != KeyEvent.ACTION_UP) return false;
                if (web.canGoBack()) web.goBack();
                else close();
                return true;
            }
        });
        d.setOnDismissListener(new DialogInterface.OnDismissListener() {
            @Override
            public void onDismiss(DialogInterface di) {
                if (pendingFiles != null) {
                    pendingFiles.onReceiveValue(null);
                    pendingFiles = null;
                }
                web.destroy();
                dialog = null;
                open = false;
            }
        });
        dialog = d;
        d.show();
        // 대화상자 창은 기본이 내용 크기라 화면 전체로 넓힌다.
        if (d.getWindow() != null) d.getWindow().setLayout(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT);
        web.loadUrl(url);
    }

    private static void close() {
        if (dialog != null) dialog.dismiss();
    }

    private static void deliverFiles(int resultCode, Intent data) {
        Uri[] uris = null;
        if (resultCode == Activity.RESULT_OK && data != null) {
            ClipData clip = data.getClipData();
            if (clip != null) {
                uris = new Uri[clip.getItemCount()];
                for (int i = 0; i < uris.length; i++) uris[i] = clip.getItemAt(i).getUri();
            } else if (data.getData() != null) {
                uris = new Uri[] { data.getData() };
            }
        }
        if (pendingFiles != null) {
            pendingFiles.onReceiveValue(uris);
            pendingFiles = null;
        }
    }

    /** 사진 고르기 결과만 받고 스스로 떨어지는 보이지 않는 Fragment. */
    public static final class FileResult extends Fragment {
        private Intent request;

        static void start(Activity activity, Intent request) {
            FileResult f = new FileResult();
            f.request = request;
            activity.getFragmentManager().beginTransaction().add(f, "truebase-support-files").commitAllowingStateLoss();
        }

        @Override
        public void onCreate(Bundle savedInstanceState) {
            super.onCreate(savedInstanceState);
            if (request == null) {
                // 게임이 다시 만들어지며 붙은 경우 — 기다리는 첨부가 없다.
                deliverFiles(Activity.RESULT_CANCELED, null);
                getFragmentManager().beginTransaction().remove(this).commitAllowingStateLoss();
                return;
            }
            startActivityForResult(request, REQUEST_FILES);
        }

        @Override
        public void onActivityResult(int requestCode, int resultCode, Intent data) {
            if (requestCode == REQUEST_FILES) deliverFiles(resultCode, data);
            getFragmentManager().beginTransaction().remove(this).commitAllowingStateLoss();
        }
    }
}
