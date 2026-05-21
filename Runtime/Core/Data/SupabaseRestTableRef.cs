using System;

namespace TrueBase.Core.Data
{
    /// <summary>PostgREST <c>/rest/v1/{table}</c> 경로에 쓰는 테이블 식별자 검증·URL 조립.</summary>
    internal static class SupabaseRestTableRef
    {
        /// <summary>공백·슬래시 등 위험 문자를 거르고, <c>schema.table</c> 형태를 허용합니다.</summary>
        public static string Normalize(string tableRef, string paramName)
        {
            if (string.IsNullOrWhiteSpace(tableRef))
                throw new ArgumentException("Table reference is empty.", paramName);

            var t = tableRef.Trim();
            if (t.Contains("..", StringComparison.Ordinal)
                || t.Contains('/', StringComparison.Ordinal)
                || t.Contains('\\', StringComparison.Ordinal))
                throw new ArgumentException("Invalid table reference.", paramName);

            foreach (var c in t)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '.')
                    continue;
                throw new ArgumentException("Table reference may only contain letters, digits, underscore, and dot.", paramName);
            }

            return t;
        }

        public static string BuildTableUrl(string supabaseUrlBase, string tableRef)
        {
            return $"{supabaseUrlBase}/rest/v1/{tableRef}";
        }
    }
}
