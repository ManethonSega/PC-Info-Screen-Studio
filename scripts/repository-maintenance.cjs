const fs = require('node:fs');
const { execFileSync } = require('node:child_process');

// This manifest is intentionally fixed: future housekeeping needs a separate review.
module.exports = async ({ github, context, core, manifest, isAncestor, mergeParent }) => {
  if (context.eventName !== 'push' || context.ref !== 'refs/heads/main') {
    throw new Error('Repository maintenance is restricted to a main-branch push.');
  }
  manifest ??= JSON.parse(fs.readFileSync('.github/maintenance/alpha13-cleanup.json', 'utf8'));
  isAncestor ??= (sha, main) => {
    try {
      execFileSync('git', ['merge-base', '--is-ancestor', sha, main], { stdio: 'pipe' });
      return true;
    } catch (error) {
      if (error.status === 1) return false;
      throw error;
    }
  };
  mergeParent ??= () => execFileSync('git', ['rev-parse', `${context.sha}^2`], { encoding: 'utf8' }).trim();
  const { owner, repo } = context.repo;
  const params = { owner, repo };
  const repository = (await github.rest.repos.get(params)).data;
  const existingLabels = await github.paginate(github.rest.issues.listLabelsForRepo, { ...params, per_page: 100 });
  const names = new Set(existingLabels.map(label => label.name));
  for (const label of manifest.labels) {
    if (!names.has(label.name)) {
      await github.rest.issues.createLabel({ ...params, ...label });
      names.add(label.name);
      core.info(`Created label: ${label.name}`);
    }
  }
  const existingIssues = await github.paginate(github.rest.issues.listForRepo, { ...params, state: 'all', per_page: 100 });
  for (const desired of manifest.issues) {
    const marker = `<!-- pcinfo-roadmap:${desired.key} -->`;
    let issue = existingIssues.find(item => !item.pull_request &&
      (item.body?.includes(marker) || item.title === desired.title));
    if (!issue) {
      issue = (await github.rest.issues.create({ ...params,
        title: desired.title, body: desired.body, labels: desired.labels })).data;
      existingIssues.push(issue);
      core.info(`Created issue #${issue.number}: ${issue.title}`);
    } else {
      const currentLabels = new Set(issue.labels.map(label => typeof label === 'string' ? label : label.name));
      const missing = desired.labels.filter(label => !currentLabels.has(label));
      if (missing.length) await github.rest.issues.addLabels({ ...params, issue_number: issue.number, labels: missing });
      core.info(`Kept existing issue #${issue.number}: ${issue.title}`);
    }
    core.summary.addRaw(`- [#${issue.number}](${issue.html_url}) ${issue.title}\n`);
  }
  // All labels and issues must be in place before deleting any branch.
  for (const candidate of manifest.branches) {
    if (candidate.branch === repository.default_branch || candidate.branch === 'main') {
      throw new Error('The default branch must never be a cleanup candidate.');
    }
    let branch;
    try {
      branch = (await github.rest.repos.getBranch({ ...params, branch: candidate.branch })).data;
    } catch (error) {
      if (error.status === 404) { core.info(`Already absent: ${candidate.branch}`); continue; }
      throw error;
    }
    const expectedSha = candidate.expectedHeadFromMergeParent ? mergeParent() : candidate.expectedSha;
    const sha = branch.commit.sha;
    if (!expectedSha || branch.protected || sha !== expectedSha || !isAncestor(sha, context.sha)) {
      core.warning(`Kept protected, changed or unmerged branch: ${candidate.branch}`);
      continue;
    }
    const pulls = await github.paginate(github.rest.pulls.list, { ...params, state: 'open', per_page: 100 });
    if (pulls.some(pr => pr.head.repo?.full_name === repository.full_name && pr.head.ref === candidate.branch)) {
      core.warning(`Kept branch with an open PR: ${candidate.branch}`);
      continue;
    }
    const ref = `heads/${candidate.branch}`;
    const current = (await github.rest.git.getRef({ ...params, ref })).data;
    if (current.object.sha !== sha) {
      core.warning(`Kept branch that moved during cleanup: ${candidate.branch}`);
      continue;
    }
    await github.rest.git.deleteRef({ ...params, ref });
    core.info(`Deleted ${candidate.branch}; preserved ${sha} in ${context.sha}`);
    core.summary.addRaw(`\nDeleted \`${candidate.branch}\`; history preserved at \`${sha}\`.\n`);
  }
  await core.summary.write();
};
