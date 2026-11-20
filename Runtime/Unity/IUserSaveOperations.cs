using System.Threading.Tasks;
using TrueBase.Core.Common;

namespace TrueBase.Unity
{
    /// <summary>
    /// <see cref="StaticUserSave{TRow}"/>의 조작 API를 Row 타입과 무관하게 노출하는 내부 인터페이스입니다.
    /// <para>
    /// <c>Supabase</c> 파사드는 제네릭이 아니므로 <c>StaticUserSave&lt;TRow&gt;</c>를 직접 참조할 수 없습니다.
    /// 서브클래스는 프로젝트 전체에서 하나뿐이므로, 생성자에서 자신을 <c>SupabaseSDK._userSave</c>에 등록해
    /// 파사드가 이 인터페이스로 위임합니다. 덕분에 게임은 생성된 클래스가 아니라 <c>Supabase.*</c>로 세이브를 다루고,
    /// 생성 파일은 DB 컬럼이 바뀔 때만 다시 만들면 됩니다.
    /// </para>
    /// </summary>
    internal interface IUserSaveOperations
    {
        Task<SupabaseLoadResult> LoadAsync(bool includeUpdatedAt);
        Task<SupabaseSaveResult> SaveNowAsync(int timeoutMs);
        SupabaseResult RequestSave();
        Task<SupabaseResult> DeleteAsync();

        /// <summary>서버가 세이브 칸에 직접 더하는 요청(우편 받기)을 보내기 직전에 부릅니다. <see cref="ApplyServerAdd"/>에 넘깁니다.</summary>
        ServerAddMark BeginServerAdd();

        /// <summary>
        /// 서버가 이미 더한 값을 로컬 세이브에 맞춥니다. 그 사이 저장이 나갔으면 그 저장이 서버 값을 옛 값으로 덮었을 수 있어,
        /// 로컬에만 더하고 바뀐 것으로 표시해 다음 저장이 서버를 맞추게 합니다.
        /// </summary>
        void ApplyServerAdd(string column, long amount, ServerAddMark mark);
    }

    /// <summary>서버 지급 요청을 보낼 때의 저장 상태. 응답 사이에 저장이 나갔는지 가릅니다.</summary>
    internal readonly struct ServerAddMark
    {
        public readonly int SendsStarted;
        public readonly bool SendInFlight;

        public ServerAddMark(int sendsStarted, bool sendInFlight)
        {
            SendsStarted = sendsStarted;
            SendInFlight = sendInFlight;
        }
    }
}
