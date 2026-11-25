using System;

namespace TrueBase.Core.Data
{
    /// <summary>지금 시행 중인 이용약관·개인정보처리방침 판. <c>Supabase.GetLegalVersionsAsync</c> 가 돌려준다.</summary>
    public sealed class SupabaseLegalVersions
    {
        /// <summary>이용약관 판 번호. 게시한 판이 없으면 0.</summary>
        public int TermsVersion { get; internal set; }

        /// <summary>이용약관 시행일. 게시한 판이 없으면 null.</summary>
        public DateTime? TermsEffectiveDate { get; internal set; }

        /// <summary>개인정보처리방침 판 번호. 게시한 판이 없으면 0.</summary>
        public int PrivacyVersion { get; internal set; }

        /// <summary>개인정보처리방침 시행일. 게시한 판이 없으면 null.</summary>
        public DateTime? PrivacyEffectiveDate { get; internal set; }
    }

    /// <summary>내 마지막 약관 동의와 다시 물어야 하는지. <c>Supabase.GetLegalConsentAsync</c> 가 돌려준다.</summary>
    public sealed class SupabaseLegalConsent
    {
        /// <summary>동의 화면을 다시 띄워야 하면 true — 동의한 적이 없거나, 동의한 판이 지금 판과 다르거나, 만 14세 이상 확인이 없을 때.</summary>
        public bool NeedsConsent { get; internal set; }

        /// <summary>동의한 적이 있으면 true.</summary>
        public bool HasConsent { get; internal set; }

        /// <summary>동의한 이용약관 판. 동의한 적이 없으면 0.</summary>
        public int TermsVersion { get; internal set; }

        /// <summary>동의한 개인정보처리방침 판. 동의한 적이 없으면 0.</summary>
        public int PrivacyVersion { get; internal set; }

        /// <summary>만 14세 이상이라고 확인했으면 true.</summary>
        public bool AgeConfirmed { get; internal set; }

        /// <summary>동의한 시각. 동의한 적이 없으면 null.</summary>
        public DateTimeOffset? AgreedAt { get; internal set; }

        /// <summary>지금 시행 중인 판.</summary>
        public SupabaseLegalVersions Current { get; internal set; }
    }
}
