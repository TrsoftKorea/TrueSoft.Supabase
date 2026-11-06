#!/usr/bin/env node
// 라이브 DB 에 SQL 을 적용하기 직전, 이미 깔린 게임이 부르는 함수·칸을 깨뜨리는지 본다(SdkAudit R23).
// 작업 끝의 검사는 적용된 뒤라 늦다 — 서버는 적용 즉시 전원에게 바뀐다.
import { spawnSync } from 'node:child_process'
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

const input = JSON.parse(readFileSync(0, 'utf8') || '{}')
const sql = input.tool_input?.query
// execute_sql 은 대부분 조회다. 구조를 바꾸는 글자가 있을 때만 본다.
if (typeof sql !== 'string' || !/\b(create|alter|drop|grant|revoke)\b/i.test(sql)) process.exit(0)

const project = process.env.CLAUDE_PROJECT_DIR || process.cwd()
const dir = mkdtempSync(join(tmpdir(), 'live-sql-'))
const file = join(dir, 'migration.sql')
try {
  writeFileSync(file, sql)
  const r = spawnSync('dotnet', ['run', '--project', join(project, 'Tools~', 'SdkAudit'), '--', '--check-migration', file], {
    cwd: project,
    encoding: 'utf8',
    timeout: 110000,
  })
  if (r.status === 2) {
    process.stderr.write('라이브 적용을 막았습니다 — 이미 깔린 게임이 실패합니다.\n' + r.stdout)
    process.exit(2)
  }
  if (r.status !== 0) {
    // 검사기가 안 돌았다고 적용을 막으면 긴급 수정까지 막힌다. 대신 안 봤다는 사실을 남긴다.
    console.log(JSON.stringify({
      hookSpecificOutput: {
        hookEventName: 'PreToolUse',
        additionalContext: `옛 빌드 호환 검사를 돌리지 못했습니다(코드 ${r.status}). 이 SQL 은 호환 확인 없이 적용됩니다 — 사용자에게 알리세요. ${(r.stdout || r.error?.message || '').slice(0, 300)}`,
      },
    }))
  }
} finally {
  rmSync(dir, { recursive: true, force: true })
}
