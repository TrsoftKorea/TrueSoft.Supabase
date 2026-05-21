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
