using System;

namespace TrueBase.Core.Models
{
    [Serializable]
    internal sealed class AnalyticsEventInsert
    {
        public string account_id;
        public string user_id;
        public string session_id;
        public string event_name;
        // event_time: DB default now()
    }
}
