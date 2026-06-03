const test = require('node:test');
const assert = require('node:assert/strict');

const {
  getDownloadUrl,
  parseBuildTag,
  resolveReleaseVersionAsync,
} = require('./postinstall.js');

test('uses explicit release version when package metadata provides it', async () => {
  const releaseVersion = await resolveReleaseVersionAsync(
    '1.7.22',
    '1.7.22.291',
    async () => {
      throw new Error('fetchJson should not be called when sihReleaseVersion is present');
    });

  assert.equal(releaseVersion, '1.7.22.291');
});

test('falls back to latest matching four-part GitHub release when metadata is missing', async () => {
  const releaseVersion = await resolveReleaseVersionAsync(
    '1.7.20',
    null,
    async () => [
      { tag_name: 'v1.7.19.275' },
      { tag_name: 'v1.7.20.277' },
      { tag_name: 'v1.7.20.278' },
      { tag_name: 'v1.7.22.282' },
    ]);

  assert.equal(releaseVersion, '1.7.20.278');
});

test('keeps the npm version when no matching four-part release exists', async () => {
  const releaseVersion = await resolveReleaseVersionAsync(
    '1.7.30',
    null,
    async () => [
      { tag_name: 'v1.7.20.278' },
      { tag_name: 'v1.7.22.282' },
    ]);

  assert.equal(releaseVersion, '1.7.30');
});

test('builds download URLs from the concrete release version', () => {
  assert.equal(
    getDownloadUrl('1.7.22.291', 'sih-linux-x64.tar.gz'),
    'https://github.com/PinedaTec-EU/SphereIntegrationHub/releases/download/v1.7.22.291/sih-linux-x64.tar.gz');
});

test('parses only matching four-part build tags for the current npm version', () => {
  assert.deepEqual(parseBuildTag('v1.7.20.278', '1.7.20'), { build: 278, tagName: 'v1.7.20.278' });
  assert.equal(parseBuildTag('v1.7.22.282', '1.7.20'), null);
});
