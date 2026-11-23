#!/usr/bin/env node
// 실제 서버 대조의 판정이 살아 있는지 — 꼭 걸려야 할 어긋남과 넘겨야 할 차이를 하나씩 넣어 본다. 서버는 부르지 않는다.
//   node "Tools~/LiveCheck/selftest.mjs"
import { readFileSync } from 'node:fs'
import { INSTALL_SQL } from '../lib/supabase-cli.mjs'
import {
  bodyHash, fileFunctions, fileCronNames, fileTableColumns,
  compareProjects, compareFunctionsToFile, compareCron, compareEdgeLists, firstDiffLine,
} from './compare.mjs'

let failed = 0
function expect(label, ok) { if (!ok) failed++; console.log(`${ok ? '✔' : '✘'} ${label}`) }
const has = (problems, text) => problems.some((p) => p.includes(text))

const fn = (name, body) => ({ axis: 'function', name: `${name}()`, owner_name: name, body: bodyHash(body), meta: 'm' })
const row = (axis, name, body = 'b') => ({ axis, name, owner_name: name, body, meta: '' })

// DB 함수 ↔ 파일
const sql = `create or replace function public.a() returns int language sql as $$ select 1 -- 주석
$$;
create function b() returns int language plpgsql as $fn$ begin return 2; end $fn$;`
const defs = fileFunctions(sql)
expect('파일에서 함수 둘을 뽑음', defs.length === 2)
expect('주석·공백만 다른 본문은 같음', compareFunctionsToFile([fn('a', 'select   1'), fn('b', 'begin return 2; end')], defs).problems.length === 0)
expect('본문이 다르면 걸림', has(compareFunctionsToFile([fn('a', 'select 2'), fn('b', 'begin return 2; end')], defs).problems, '본문이'))
expect('서버에만 있는 함수가 걸림', has(compareFunctionsToFile([fn('a', 'select 1'), fn('b', 'begin return 2; end'), fn('c', 'x')], defs).problems, 'install.sql 에 없음'))
expect('파일에만 있는 함수가 걸림', has(compareFunctionsToFile([fn('a', 'select 1')], defs).problems, '서버에 없음'))

// 게임끼리
const base = { user_data: ['id', 'account_id'], leaderboard_scores: ['id'] }
const A = [row('table', 't'), row('column', 'user_data.id'), row('column', 'user_data.account_id'), row('column', 'user_data.gold'), row('vault_name', 'ts_cfg:X'), row('column', 'leaderboard_scores.id')]
const B = [row('table', 't'), row('column', 'user_data.id'), row('column', 'user_data.account_id'), row('column', 'user_data.gems'), row('column', 'leaderboard_scores.id')]
const same = compareProjects([{ name: 'A', rows: A }, { name: 'B', rows: B }], base)
expect('게임별 칸·콘솔 설정값은 넘김', same.problems.length === 0 && same.skipped === 2)
const noBase = compareProjects([{ name: 'A', rows: A }, { name: 'B', rows: B.filter((r) => r.name !== 'user_data.account_id') }], base)
expect('SDK 칸이 한 게임에 없으면 걸림', has(noBase.problems, 'user_data.account_id'))
const diff = compareProjects([{ name: 'A', rows: A }, { name: 'B', rows: B.map((r) => (r.name === 't' ? { ...r, body: 'x' } : r)) }], base)
expect('같은 대상의 내용이 다르면 걸림', has(diff.problems, 'table t'))
const oneSide = compareProjects([{ name: 'A', rows: [...A, row('policy', 'p')] }, { name: 'B', rows: B }], base)
expect('한 게임에만 있는 대상이 걸림', has(oneSide.problems, 'policy p'))

// 크론·서버 함수
expect('크론 빠짐·남음이 걸림', compareCron([row('cron', 'x'), row('cron', 'extra')], ['x', 'y']).length === 2)
const edge = compareEdgeLists([
  { name: 'A', functions: [{ slug: 'f', ezbr_sha256: '1', verify_jwt: true }, { slug: 'g', ezbr_sha256: '1', verify_jwt: true }] },
  { name: 'B', functions: [{ slug: 'f', ezbr_sha256: '2', verify_jwt: false }] },
], ['f', 'g'])
expect('서버 함수 코드·로그인 검사·빠진 배포가 걸림', has(edge, '코드가') && has(edge, '로그인 검사') && has(edge, 'B 에 배포 안 됨'))
const picked = compareEdgeLists([
  { name: 'A', functions: [{ slug: 'f', ezbr_sha256: '1', verify_jwt: true }, { slug: 'g', ezbr_sha256: '1', verify_jwt: true }] },
  { name: 'B', functions: [{ slug: 'f', ezbr_sha256: '2', verify_jwt: false }] },
], ['f', 'g'], new Set(['g']))
expect('고른 서버 함수만 봄(다른 함수의 차이는 안 걸림)', picked.length === 1 && has(picked, 'g — B 에 배포 안 됨'))
expect('줄 끝만 다른 코드는 같음', firstDiffLine('a\r\nb  \n', 'a\nb\n\n') === null)
expect('다른 줄 번호를 찾음', firstDiffLine('a\nb\nc', 'a\nx\nc') === 2)

// 실제 install.sql 을 읽는 부분
const real = readFileSync(INSTALL_SQL, 'utf8')
expect(`실제 install.sql 에서 함수를 뽑음(${fileFunctions(real).length}개)`, fileFunctions(real).length >= 200)
expect(`실제 install.sql 에서 크론을 뽑음(${fileCronNames(real).length}개)`, fileCronNames(real).length >= 10)
const ud = fileTableColumns(real, 'user_data')
expect(`user_data SDK 칸(${ud?.join(',')})`, ud?.includes('account_id') && !ud.includes('coins'))
expect('leaderboard_scores SDK 칸', fileTableColumns(real, 'leaderboard_scores')?.includes('score'))

console.log(failed ? `\n${failed}개 실패` : '\n모두 통과')
if (failed) process.exitCode = 1
