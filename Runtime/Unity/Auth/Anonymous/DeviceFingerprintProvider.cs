using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace TrueBase.Unity.Auth.Anonymous
{
    /// <summary>
    /// 기기 고유값을 직접 전송하지 않기 위해 SHA-256 해시 지문을 생성합니다.
    /// </summary>
    internal static class DeviceFingerprintProvider
    {
        /// <summary>
        /// <c>프로젝트URL|앱번들ID|deviceUniqueIdentifier</c>를 SHA-256 해시한 소문자 hex 문자열을 반환합니다.
        /// 프로젝트·앱 단위로 지문이 달라져 기기 ID가 서비스 간에 연결되지 않습니다.
        /// </summary>
        /// <param name="projectUrl">Supabase 프로젝트 URL. null이면 빈 문자열로 처리.</param>
        /// <returns>64자 hex 지문. 기기 ID를 얻을 수 없는 플랫폼에서는 null.</returns>
        public static string TryCreateHashedFingerprint(string projectUrl)
        {
            var deviceId = SystemInfo.deviceUniqueIdentifier;
            if (string.IsNullOrWhiteSpace(deviceId))
                return null;

            var appId = Application.identifier ?? string.Empty;
            var url = projectUrl ?? string.Empty;
            var source = $"{url.Trim()}|{appId.Trim()}|{deviceId.Trim()}";

            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(source));
            return ToLowerHex(bytes);
        }

        private static string ToLowerHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;

            var sb = new StringBuilder(bytes.Length * 2);
            for (var i = 0; i < bytes.Length; i++)
                sb.Append(bytes[i].ToString("x2"));

            return sb.ToString();
        }
    }
}
