using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TrueBase.Unity
{
    /// <summary>
    /// 플레이나누 동기화를 커스터마이징하는 편의 클래스. <see cref="StaticUserSave{TRow}.UseNanooConverters"/>로 등록합니다.
    /// <para><c>map.Field(r =&gt; r.field, toString, fromString)</c> — 특정 필드만 다른 형태(보통 문자열)로 저장/복원.
    /// 등록하지 않은 필드는 기본 Newtonsoft.Json 변환을 사용합니다. 플레이나누 직렬화·역직렬화 경로 전용 —
    /// REST/DB 저장·로드에는 영향이 없습니다.</para>
    /// <para><c>map.CompareBy(r =&gt; r.field)</c> — 플레이나누·SDK 중 최신 데이터를 가릴 비교 필드 지정. 기본값 <c>updated_at</c>.</para>
    /// </summary>
    /// <typeparam name="TRow">세이브 Row 타입.</typeparam>
    public sealed class NanooFieldMap<TRow> where TRow : class, new()
    {
        private readonly List<(string key, Func<JToken, JToken> toNanoo, Func<JToken, JToken> fromNanoo)> _fields
            = new List<(string, Func<JToken, JToken>, Func<JToken, JToken>)>();

        // GetNanooMap()이 "등록된 게 하나도 없으면 map 자체를 버리고 null"로 최적화하므로,
        // CompareBy만 부르고 Field는 안 부른 경우도 "등록됨"으로 잡아야 한다 — 안 그러면 CompareBy가 조용히 무시된다.
        internal bool IsEmpty => _fields.Count == 0 && !_hasCompareBy;
        private bool _hasCompareBy;

        // 비교 필드(CompareBy) — SDK 쪽은 멤버명으로 리플렉션, 나누 쪽은 JSON 키로 조회하므로 둘 다 보관합니다.
        internal string CompareMemberName { get; private set; } = "updated_at";
        internal string CompareJsonKey { get; private set; } = "updated_at";
        internal double CompareFallbackUtcOffsetHours { get; private set; }

        /// <summary>
        /// 플레이나누와 SDK 중 어느 쪽 데이터가 최신인지 비교할 필드를 지정합니다. 지정하지 않으면 <c>updated_at</c>을 씁니다.
        /// </summary>
        /// <typeparam name="T">필드 타입(자동 추론). 값을 문자열로 변환해 <see cref="DateTimeOffset"/>으로 파싱합니다.</typeparam>
        /// <param name="selector">필드 선택식. 예: <c>r =&gt; r.lastSyncedAt</c></param>
        /// <param name="fallbackUtcOffsetHours">필드 값에 시간대 정보(<c>Z</c>·오프셋)가 없을 때 적용할 UTC 오프셋(시간 단위). 기본값 0(UTC).</param>
        public NanooFieldMap<TRow> CompareBy<T>(Expression<Func<TRow, T>> selector, double fallbackUtcOffsetHours = 0)
        {
            var member = ResolveMember(selector);
            var jp = member.GetCustomAttribute<JsonPropertyAttribute>();
            CompareMemberName = member.Name;
            CompareJsonKey = string.IsNullOrEmpty(jp?.PropertyName) ? member.Name : jp.PropertyName;
            CompareFallbackUtcOffsetHours = fallbackUtcOffsetHours;
            _hasCompareBy = true;
            return this;
        }

        /// <summary>
        /// 필드 선택식으로 변환을 등록합니다. 키는 선택한 멤버에서 자동 결정됩니다(하드코딩 문자열 불필요).
        /// </summary>
        /// <typeparam name="T">필드 타입(자동 추론).</typeparam>
        /// <param name="selector">필드 선택식. 예: <c>r =&gt; r.itemIds</c></param>
        /// <param name="toNanoo">필드값(T) → 플레이나누 저장 문자열. 예: <c>v =&gt; string.Join("_", v)</c></param>
        /// <param name="fromNanoo">플레이나누 문자열 → 필드값(T). 예: <c>s =&gt; s.Split('_')...</c></param>
        public NanooFieldMap<TRow> Field<T>(
            Expression<Func<TRow, T>> selector,
            Func<T, string> toNanoo,
            Func<string, T> fromNanoo)
            => Field(ResolveJsonKey(selector), toNanoo, fromNanoo);

        /// <summary>키(플레이나누 JSON 키)를 직접 지정해 변환을 등록합니다.</summary>
        public NanooFieldMap<TRow> Field<T>(
            string nanooKey,
            Func<T, string> toNanoo,
            Func<string, T> fromNanoo)
        {
            if (string.IsNullOrEmpty(nanooKey)) throw new ArgumentException("nanooKey is required", nameof(nanooKey));
            if (toNanoo == null) throw new ArgumentNullException(nameof(toNanoo));
            if (fromNanoo == null) throw new ArgumentNullException(nameof(fromNanoo));

            _fields.Add((
                nanooKey,
                // 직렬화: 기본 직렬화된 토큰 → 타입값 T → 저장 문자열
                defaultToken =>
                {
                    var s = toNanoo(defaultToken.ToObject<T>());
                    return s == null ? JValue.CreateNull() : new JValue(s);
                },
                // 역직렬화: 저장 문자열 → 타입값 T → 기본 토큰
                nanooToken =>
                {
                    var v = fromNanoo((string)nanooToken);
                    return v == null ? JValue.CreateNull() : JToken.FromObject(v);
                }));
            return this;
        }

        /// <summary>등록된 필드만 가공해 Row를 플레이나누 JSON으로 직렬화합니다.</summary>
        public string Serialize(TRow row)
        {
            var jo = JObject.FromObject(row);
            foreach (var f in _fields)
                if (jo.TryGetValue(f.key, out var tok) && tok.Type != JTokenType.Null)
                    jo[f.key] = f.toNanoo(tok);
            return jo.ToString(Formatting.None);
        }

        /// <summary>등록된 필드만 복원해 플레이나누 JSON을 Row로 역직렬화합니다.</summary>
        public TRow Deserialize(string json)
        {
            var jo = JObject.Parse(json);
            foreach (var f in _fields)
                if (jo.TryGetValue(f.key, out var tok) && tok.Type != JTokenType.Null)
                    jo[f.key] = f.fromNanoo(tok);
            return jo.ToObject<TRow>();
        }

        // 선택식의 멤버명(또는 [JsonProperty] 이름)을 JSON 키로 사용.
        private static string ResolveJsonKey<T>(Expression<Func<TRow, T>> selector)
        {
            var member = ResolveMember(selector);
            var jp = member.GetCustomAttribute<JsonPropertyAttribute>();
            return string.IsNullOrEmpty(jp?.PropertyName) ? member.Name : jp.PropertyName;
        }

        private static MemberInfo ResolveMember<T>(Expression<Func<TRow, T>> selector)
        {
            var body = selector.Body;
            // r => (object)r.field 같은 박싱 변환을 벗겨낸다. Unity의 Mono 컴파일러는 값 타입 필드
            // 선택식에서 Convert 를 중첩으로 두 겹 이상 붙이는 경우가 있어(.NET 컴파일러는 한 겹만
            // 붙임 — 2026-08-25 재현), 하나만 벗기면 부족하다. 몇 겹이든 다 벗겨낸다.
            while (body is UnaryExpression u && u.NodeType == ExpressionType.Convert)
                body = u.Operand;
            if (body is not MemberExpression me)
                throw new ArgumentException("필드/프로퍼티 선택식이어야 합니다. 예: r => r.itemIds", nameof(selector));
            return me.Member;
        }
    }
}
