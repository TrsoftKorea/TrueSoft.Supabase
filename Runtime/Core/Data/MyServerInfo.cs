namespace TrueBase.Core.Data
{
    public readonly struct MyServerInfo
    {
        public MyServerInfo(string serverId, string serverCode)
        {
            ServerId = serverId ?? string.Empty;
            ServerCode = serverCode ?? string.Empty;
        }

        public string ServerId { get; }
        public string ServerCode { get; }
    }
}
