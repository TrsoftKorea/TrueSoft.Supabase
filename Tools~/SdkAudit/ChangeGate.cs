using System;
using System.Diagnostics;
using System.Linq;

namespace SdkAudit
{
    /// <summary>
    /// 검사 대상 경로가 실제로 바뀌었는지 판정한다.
    /// <para>
    /// 훅에서 매 턴 검사기를 부르면 대화만 한 턴에도 몇 초를 쓴다. 그렇다고 사람이 기억해서
    /// 부르는 방식은 잊으면 그냥 안 돈다. 바뀐 게 있을 때만 돌리면 둘 다 해결된다.
    /// </para>
    /// </summary>
    public static class ChangeGate
    {
        /// <summary>검사기가 보는 경로. 여기 밖의 변경은 결과를 바꾸지 못한다.</summary>
        private static readonly string[] Watched =
        {
            "Runtime/",
            "Samples~/",
            "docs~/",
            "Tools~/SdkAudit/",
            "CLAUDE.md",
            ".claude/",
        };

        public static bool HasRelevantChange(string root)
        {
            var status = RunGit(root, "status --porcelain");
            if (status == null)
                return true; // git 을 못 부르면 판정을 포기하고 검사한다

            return status.Split('\n').Any(IsWatchedStatusLine);
        }

        /// <summary>
        /// <c>git status --porcelain</c> 한 줄이 검사 대상 경로인지 본다.
        /// 형식은 <c>XY path</c> 또는 이름이 바뀐 경우 <c>XY old -&gt; new</c>.
        /// </summary>
        internal static bool IsWatchedStatusLine(string line)
        {
            var text = line != null && line.Length > 3 ? line.Substring(3).Trim().Trim('"') : "";
            if (text.Length == 0)
                return false;

            var arrow = text.LastIndexOf("->", StringComparison.Ordinal);
            if (arrow >= 0)
                text = text.Substring(arrow + 2).Trim().Trim('"');

            return Watched.Any(w => text.StartsWith(w, StringComparison.OrdinalIgnoreCase));
        }

        private static string RunGit(string root, string arguments)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo("git", arguments)
                {
                    WorkingDirectory = root,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });

                if (p == null)
                    return null;

                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(10_000);
                return p.ExitCode == 0 ? output.Replace("\r\n", "\n") : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
