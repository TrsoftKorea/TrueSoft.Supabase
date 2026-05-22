using System;

namespace TrueBase.Core.Models
{
    [Serializable]
    internal sealed class AnalyticsSessionInsert
    {
        public string session_id;
        public string account_id;
        public string user_id;
        public string platform;
        public string app_version;
    }

    [Serializable]
    internal sealed class AnalyticsSessionPatch
    {
        public string last_active_at;
    }

    [Serializable]
    internal sealed class AnalyticsSessionClosePatch
    {
        public string ended_at;
        public bool   is_closed;
    }
}
