package com.truesoft.googleloginplugin;

import android.app.Activity;
import android.os.CancellationSignal;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;

import androidx.credentials.ClearCredentialStateRequest;
import androidx.credentials.Credential;
import androidx.credentials.CredentialManager;
import androidx.credentials.CredentialManagerCallback;
import androidx.credentials.CredentialOption;
import androidx.credentials.CustomCredential;
import androidx.credentials.GetCredentialRequest;
import androidx.credentials.GetCredentialResponse;
import androidx.credentials.exceptions.ClearCredentialException;
import androidx.credentials.exceptions.GetCredentialException;

import com.google.android.libraries.identity.googleid.GetGoogleIdOption;
import com.google.android.libraries.identity.googleid.GetSignInWithGoogleOption;
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential;
import com.google.android.libraries.identity.googleid.GoogleIdTokenParsingException;
import com.unity3d.player.UnityPlayer;

import java.util.concurrent.Executor;

/**
 * 안드로이드 구글 로그인(Credential Manager). C# GoogleLoginBridge 가 이 클래스의 정적 함수를 이름으로 부르고,
 * 결과는 UnitySendMessage 로 그 게임 오브젝트에 돌려준다.
 *
 * 소스로 두는 이유: 게임을 빌드할 때 그 게임에 실제로 들어간 androidx.credentials 판으로 함께 컴파일된다.
 * 미리 빌드한 aar 는 빌드한 판에만 있는 함수를 불러 게임에서 NoSuchMethodError 가 났다(로그아웃).
 *
 * 콜백은 메인 스레드에서 돌고, 거기서 새는 예외는 C# 쪽 try/catch 로 못 잡아 앱 전체가 죽는다.
 * 그래서 진입점과 콜백 모두 Throwable 로 잡는다 — 판이 어긋나 나는 NoSuchMethodError 같은 Error 도 포함.
 */
public final class GoogleLoginPlugin {
    private static final String TAG = "GoogleLoginPlugin";
    private static final Executor MAIN = new Handler(Looper.getMainLooper())::post;

    private GoogleLoginPlugin() {}

    public static void signIn(final String unityObjectName, final String webClientId, final boolean useButtonFlow) {
        final Activity activity = UnityPlayer.currentActivity;
        if (activity == null) {
            sendError(unityObjectName, "activity_null");
            return;
        }
        try {
            CredentialOption option = useButtonFlow
                    ? new GetSignInWithGoogleOption.Builder(webClientId).build()
                    : new GetGoogleIdOption.Builder()
                            .setFilterByAuthorizedAccounts(false)
                            .setServerClientId(webClientId)
                            .build();
            GetCredentialRequest request = new GetCredentialRequest.Builder()
                    .addCredentialOption(option)
                    .build();

            CredentialManager.create(activity).getCredentialAsync(activity, request, new CancellationSignal(), MAIN,
                    new CredentialManagerCallback<GetCredentialResponse, GetCredentialException>() {
                        @Override
                        public void onResult(GetCredentialResponse result) {
                            try {
                                handleCredential(unityObjectName, result.getCredential());
                            } catch (Throwable e) {
                                Log.e(TAG, "Unknown error", e);
                                sendError(unityObjectName, "unknown_error:" + e.getMessage());
                            }
                        }

                        @Override
                        public void onError(GetCredentialException e) {
                            Log.e(TAG, "GetCredentialException", e);
                            sendError(unityObjectName, "credential_error:" + e.getType() + ":" + e.getMessage());
                        }
                    });
        } catch (Throwable e) {
            Log.e(TAG, "Unknown error", e);
            sendError(unityObjectName, "unknown_error:" + e.getMessage());
        }
    }

    private static void handleCredential(String unityObjectName, Credential credential) {
        if (!(credential instanceof CustomCredential)) {
            sendError(unityObjectName, "unexpected_credential");
            return;
        }
        if (!GoogleIdTokenCredential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL.equals(credential.getType())) {
            sendError(unityObjectName, "unexpected_credential_type");
            return;
        }

        GoogleIdTokenCredential google;
        try {
            google = GoogleIdTokenCredential.createFrom(credential.getData());
        } catch (Exception e) {
            // 코틀린 함수라 자바에는 던진다고 선언돼 있지 않다 — Exception 으로 받아 종류를 가른다.
            if (!(e instanceof GoogleIdTokenParsingException)) throw e;
            Log.e(TAG, "Token parse error", e);
            sendError(unityObjectName, "token_parse_error:" + e.getMessage());
            return;
        }

        String payload = escape(google.getIdToken())
                + "|||" + escape(google.getId())
                + "|||" + escape(orEmpty(google.getDisplayName()))
                + "|||" + escape(orEmpty(google.getGivenName()))
                + "|||" + escape(orEmpty(google.getFamilyName()))
                + "|||" + escape(google.getProfilePictureUri() != null ? google.getProfilePictureUri().toString() : "");
        UnityPlayer.UnitySendMessage(unityObjectName, "OnGoogleLoginSuccess", payload);
    }

    public static void signOut(String unityObjectName) {
        clearState(unityObjectName, "OnGoogleLogout", "logout_error:");
    }

    public static void revokeAccess(String unityObjectName) {
        clearState(unityObjectName, "OnGoogleRevokeAccess", "revoke_error:");
    }

    private static void clearState(final String unityObjectName, final String doneMethod, final String errorPrefix) {
        Activity activity = UnityPlayer.currentActivity;
        if (activity == null) {
            sendError(unityObjectName, "activity_null");
            return;
        }
        try {
            CredentialManager.create(activity).clearCredentialStateAsync(new ClearCredentialStateRequest(), new CancellationSignal(), MAIN,
                    new CredentialManagerCallback<Void, ClearCredentialException>() {
                        @Override
                        public void onResult(Void result) {
                            UnityPlayer.UnitySendMessage(unityObjectName, doneMethod, "");
                        }

                        @Override
                        public void onError(ClearCredentialException e) {
                            Log.e(TAG, doneMethod + " failed", e);
                            sendError(unityObjectName, errorPrefix + e.getMessage());
                        }
                    });
        } catch (Throwable e) {
            Log.e(TAG, doneMethod + " failed", e);
            sendError(unityObjectName, errorPrefix + e.getMessage());
        }
    }

    private static void sendError(String unityObjectName, String message) {
        UnityPlayer.UnitySendMessage(unityObjectName, "OnGoogleLoginError", message);
    }

    private static String orEmpty(String value) {
        return value != null ? value : "";
    }

    private static String escape(String value) {
        return value.replace("|||", "%7C%7C%7C");
    }
}
