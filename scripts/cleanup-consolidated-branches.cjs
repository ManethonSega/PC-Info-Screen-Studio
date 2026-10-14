const fs = require('node:fs');
const { execFileSync } = require('node:child_process');

module.exports = async ({ github, context, core }) => {
  const { owner, repo } = context.repo;
  const audited = JSON.parse(fs.readFileSync('docs/branch-consolidation-2026-10-14.json', 'utf8'));
  const candidates = [...audited, { branch: 'consolidate/alpha11-and-branch-cleanup' }];
  const pulls = await github.paginate(github.rest.pulls.list, { owner, repo, state: 'open', per_page: 100 });
  for (const candidate of candidates) {
    if (candidate.branch === 'main') throw new Error('The default branch must never be a cleanup candidate.');
    let branch;
    try {
      branch = (await github.rest.repos.getBranch({ owner, repo, branch: candidate.branch })).data;
    } catch (error) {
      if (error.status === 404) { core.info(`Already absent: ${candidate.branch}`); continue; }
      throw error;
    }
    const sha = branch.commit.sha;
    if (branch.protected || (candidate.sha && sha !== candidate.sha) ||
        pulls.some(pr => pr.head.repo?.full_name === context.payload.repository.full_name && pr.head.ref === candidate.branch)) {
      core.warning(`Kept protected, changed or active branch: ${candidate.branch}`);
      continue;
    }
    try {
      execFileSync('git', ['merge-base', '--is-ancestor', sha, context.sha]);
    } catch {
      core.warning(`Kept branch whose history is not contained in this tested commit: ${candidate.branch}`);
      continue;
    }
    const ref = `heads/${candidate.branch}`;
    const current = (await github.rest.git.getRef({ owner, repo, ref })).data;
    if (current.object.sha !== sha) {
      core.warning(`Kept branch that moved during cleanup: ${candidate.branch}`);
      continue;
    }
    await github.rest.git.deleteRef({ owner, repo, ref });
    core.info(`Deleted ${candidate.branch}; preserved ${sha} in ${context.sha}`);
    await core.summary.addRaw(`Deleted \`${candidate.branch}\`; history preserved at \`${sha}\`.\n\n`).write();
  }
};
