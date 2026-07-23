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
        Task<SupabaseResult> SaveNowAsync(int timeoutMs);
        SupabaseResult RequestSave();
        Task<SupabaseResult> SaveIfChangedAsync();
        Task<SupabaseResult> DeleteAsync();
        Task<SupabaseResult> EnsureRowAsync();
    }
}
