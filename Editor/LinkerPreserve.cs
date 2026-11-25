using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEditor.UnityLinker;

namespace TrueBase.Editor
{
    /// <summary>
    /// IL2CPP 빌드가 SDK 코드를 덜어내지 않게 한다. 서버 응답을 읽는 클래스는 Newtonsoft 가 리플렉션으로만 만들어
    /// 빌드 도구 눈에는 안 쓰는 코드라, 덜어내면 기기에서만 "Unable to find a constructor" 로 읽기가 실패한다(에디터는 정상).
    /// 패키지 안의 link.xml 은 유니티가 읽지 않고 [Preserve] 는 클래스·기본 생성자만 지켜 값을 넣는 setter 가 빠질 수 있어,
    /// 빌드 때 어셈블리 전체를 지키는 link.xml 을 만들어 넘긴다.
    /// </summary>
    internal sealed class LinkerPreserve : IUnityLinkerProcessor
    {
        // 빌드에 들어가는 것만 적는다 — IAP·PlayNanoo 어셈블리는 Unity IAP 5·PlayNANOO 가 없으면 빠진다.
        static readonly string[] Assemblies = { "TrueBase.Core", "TrueBase.Unity", "TrueBase.Unity.IAP", "TrueBase.Unity.PlayNanoo" };

        public int callbackOrder => 0;

        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            var inBuild = CompilationPipeline.GetAssemblies(AssembliesType.Player).Select(a => a.name).ToHashSet();
            var xml = new StringBuilder("<linker>\n");
            foreach (var name in Assemblies.Where(inBuild.Contains))
                xml.Append("  <assembly fullname=\"").Append(name).Append("\" preserve=\"all\"/>\n");
            xml.Append("</linker>\n");

            var path = Path.GetFullPath(Path.Combine("Library", "TrueBase", "link.xml"));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, xml.ToString());
            return path;
        }
    }
}
