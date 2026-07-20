namespace TrueBase.Core.Data
{
    /// <summary>현재 로그인 계정이 속한 게임 서버 식별 정보. RPC <c>ts_my_server_id</c> 응답입니다.</summary>
    public readonly struct ServerInfo
    {
        public ServerInfo(string serverId, string serverCode)
        {
            ServerId = serverId ?? string.Empty;
            ServerCode = serverCode ?? string.Empty;
        }

        /// <summary>서버 UUID (<c>game_servers.id</c>).</summary>
        public string ServerId { get; }

        /// <summary>서버 코드 (예: <c>GLOBAL</c>, <c>KR1</c>).</summary>
        public string ServerCode { get; }
    }
}
