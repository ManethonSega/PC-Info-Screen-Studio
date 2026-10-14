const test = require('node:test');
const assert = require('node:assert/strict');
const maintain = require('./repository-maintenance.cjs');

function fixture() {
  const state = { labels: [], issues: [], branches: new Map(), pulls: [], deleted: [], summaries: [], additions: [], created: 0 };
  const method = key => Object.assign(() => {}, { key });
  const github = { rest: {
    repos: {
      get: async () => ({ data: { default_branch: 'main', full_name: 'owner/repo' } }),
      getBranch: async ({ branch }) => {
        if (!state.branches.has(branch)) throw Object.assign(new Error('Absent'), { status: 404 });
        return { data: state.branches.get(branch) };
      }
    },
    issues: {
      listLabelsForRepo: method('labels'), listForRepo: method('issues'),
      createLabel: async label => { state.labels.push(label); },
      create: async issue => {
        state.created++;
        const data = { ...issue, number: state.created, html_url: `https://github.com/owner/repo/issues/${state.created}`, state: 'open' };
        state.issues.push(data); return { data };
      },
      addLabels: async ({ issue_number, labels }) => { state.additions.push({ issue_number, labels }); }
    },
    pulls: { list: method('pulls') },
    git: {
      getRef: async ({ ref }) => ({ data: { object: { sha: state.branches.get(ref.slice(6)).refSha ?? state.branches.get(ref.slice(6)).commit.sha } } }),
      deleteRef: async ({ ref }) => { state.deleted.push(ref); state.branches.delete(ref.slice(6)); }
    }
  }, paginate: async method => [...state[method.key]] };
  const manifest = { labels: [{ name: 'enhancement', color: 'abcdef' }], issues: [
    { key: 'one', title: 'First task', body: '<!-- pcinfo-roadmap:one -->\nCriteria', labels: ['enhancement'] }
  ], branches: [] };
  const args = { github, manifest, context: { eventName: 'push', ref: 'refs/heads/main', repo: { owner: 'owner', repo: 'repo' }, sha: 'tested-main' },
    core: { info() {}, warning() {}, summary: { addRaw: value => state.summaries.push(value), write: async () => {} } },
    isAncestor: () => true, mergeParent: () => 'cleanup-head' };
  return { state, args, manifest };
}

test('creates missing labels/issues once and keeps edited closed issues intact', async () => {
  const { state, args } = fixture();
  await maintain(args);
  state.issues[0].body = '<!-- pcinfo-roadmap:one -->\nOwner edited this';
  state.issues[0].title = 'Owner title'; state.issues[0].state = 'closed';
  state.labels[0].color = '123456';
  await maintain(args);
  assert.equal(state.created, 1); assert.equal(state.labels.length, 1);
  assert.equal(state.labels[0].color, '123456');
  assert.equal(state.issues[0].state, 'closed'); assert.equal(state.issues[0].title, 'Owner title');
  assert.match(state.issues[0].body, /Owner edited/);
});

test('existing task gains only missing labels', async () => {
  const { state, args } = fixture();
  state.issues.push({ number: 42, title: 'First task', body: 'Owner notes', labels: [{ name: 'custom' }], state: 'open' });
  await maintain(args);
  assert.equal(state.created, 0);
  assert.deepEqual(state.additions, [{ issue_number: 42, labels: ['enhancement'] }]);
  assert.equal(state.issues[0].body, 'Owner notes');
});

test('deletes only unchanged merged branches, including the audited merge parent', async () => {
  const { state, args, manifest } = fixture();
  manifest.branches = [{ branch: 'feature', expectedSha: 'feature-head' }, { branch: 'cleanup', expectedHeadFromMergeParent: true }];
  state.branches.set('feature', { commit: { sha: 'feature-head' }, protected: false });
  state.branches.set('cleanup', { commit: { sha: 'cleanup-head' }, protected: false });
  await maintain(args);
  assert.deepEqual(state.deleted, ['heads/feature', 'heads/cleanup']);
  await maintain(args); assert.equal(state.deleted.length, 2); assert.equal(state.created, 1);
});

for (const reason of ['protected', 'changed', 'unmerged', 'open PR', 'moved', 'missing audit']) {
  test(`keeps a branch that is ${reason}`, async () => {
    const { state, args, manifest } = fixture();
    manifest.branches = [{ branch: 'feature', expectedSha: reason === 'missing audit' ? undefined : 'audited' }];
    state.branches.set('feature', { commit: { sha: reason === 'changed' ? 'new-work' : 'audited' }, protected: reason === 'protected', refSha: reason === 'moved' ? 'new-work' : undefined });
    if (reason === 'unmerged') args.isAncestor = () => false;
    if (reason === 'open PR') state.pulls = [{ head: { ref: 'feature', repo: { full_name: 'owner/repo' } } }];
    await maintain(args); assert.deepEqual(state.deleted, []);
  });
}

test('rejects default-branch deletion', async () => {
  const { state, args, manifest } = fixture();
  manifest.branches = [{ branch: 'main', expectedSha: 'main-head' }];
  await assert.rejects(maintain(args), /default branch/); assert.deepEqual(state.deleted, []);
});

test('issue creation failure blocks all deletion', async () => {
  const { state, args, manifest } = fixture();
  manifest.branches = [{ branch: 'feature', expectedSha: 'audited' }];
  state.branches.set('feature', { commit: { sha: 'audited' }, protected: false });
  args.github.rest.issues.create = async () => { throw new Error('No issue permission'); };
  await assert.rejects(maintain(args), /No issue permission/); assert.deepEqual(state.deleted, []);
});

test('rejects a PR context before any writes', async () => {
  const { state, args } = fixture(); args.context.eventName = 'pull_request';
  await assert.rejects(maintain(args), /main-branch push/);
  assert.equal(state.created, 0); assert.equal(state.labels.length, 0);
});
