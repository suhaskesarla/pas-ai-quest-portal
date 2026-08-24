import type { FullConfig, FullResult, Reporter, TestCase, TestResult } from '@playwright/test/reporter'
import { execFileSync } from 'node:child_process'
import fs from 'node:fs'
import path from 'node:path'

export default class EvidenceReporter implements Reporter {
  private passed = 0
  private failed = 0
  private startedAt = new Date()

  onBegin() { this.startedAt = new Date() }

  onTestEnd(_test: TestCase, result: TestResult) {
    if (result.status === 'passed') this.passed += 1
    else if (result.status !== 'skipped') this.failed += 1
  }

  onEnd(result: FullResult) {
    const runRoot = process.env.QA_RUN_ROOT
    if (!runRoot) return
    fs.mkdirSync(runRoot, { recursive: true })
    const screenshots = walk(runRoot).filter(file => file.toLowerCase().endsWith('.png')).map(file => path.relative(runRoot, file).replaceAll('\\', '/')).sort()
    const healthPath = path.join(runRoot, 'docker-health.txt')
    const health = fs.existsSync(healthPath) ? fs.readFileSync(healthPath, 'utf8').trim() : 'not applicable'
    const sha = execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim()
    const summary = [
      `timestamp=${this.startedAt.toISOString()}`,
      `git_commit=${sha}`,
      `test_mode=${process.env.QA_TEST_MODE ?? 'unknown'}`,
      `base_url=${process.env.QA_BASE_URL ?? 'unknown'}`,
      `tests_passed=${this.passed}`,
      `tests_failed=${this.failed}`,
      `clean_database=${process.env.QA_CLEAN_DATABASE ?? 'unknown'}`,
      `fixture_data_used=${process.env.QA_FIXTURE_DATA ?? 'unknown'}`,
      `docker_health=${JSON.stringify(health)}`,
      `screenshots=${JSON.stringify(screenshots)}`,
      `final_result=${result.status}`,
      '',
    ].join('\n')
    fs.writeFileSync(path.join(runRoot, 'summary.txt'), summary)
  }
}

function walk(directory: string): string[] {
  if (!fs.existsSync(directory)) return []
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    const item = path.join(directory, entry.name)
    return entry.isDirectory() ? walk(item) : [item]
  })
}
