const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { JSDOM } = require('jsdom');
const script = readFileSync('../../src/GroupPilot.Core/FacebookPageSnapshot.js', 'utf8');
const groupUrl = 'https://www.facebook.com/groups/test-group/';
const postUrl = 'https://www.facebook.com/source/posts/123/';
const composer = '<div role="dialog"><h2>Create post</h2><div role="textbox" contenteditable="true"></div><button>Post</button></div>';
function fixture(html = composer, url = groupUrl) {
  const dom = new JSDOM(html, { url, runScripts: 'outside-only' });
  const { window } = dom;
  window.Element.prototype.getClientRects = function () { return this.closest('[hidden]') ? [] : [{}]; };
  let clicks = 0;
  window.document.querySelectorAll('button').forEach(button => button.onclick = () => clicks++);
  const read = () => JSON.parse(JSON.stringify(window.eval(script)({ token: 'fixture', groupUrl, postUrl })));
  return { document: window.document, read, clicks: () => clicks };
}
test('labels one composer/editor/submit without filling or clicking', () => {
  const f = fixture(); const result = f.read();
  assert.equal(result.ComposerCount, 1); assert.equal(result.EditorCount, 1); assert.equal(result.SubmitCount, 1);
  assert.equal(result.EditorText, ''); assert.equal(f.clicks(), 0);
  assert.equal(f.document.querySelectorAll('[data-grouppilot-submit=fixture]').length, 1);
});
test('nested dialogs resolve to one composer', () => {
  const f = fixture(`<div role="dialog">${composer}</div>`);
  assert.equal(f.read().ComposerCount, 1); assert.equal(f.read().EditorCount, 1);
});
test('unrelated draft is exposed unchanged', () => {
  const f = fixture(); f.document.querySelector('[role=textbox]').textContent = 'Unrelated draft';
  assert.equal(f.read().EditorText, 'Unrelated draft'); assert.equal(f.clicks(), 0);
});
test('wrong group is rejected', () => {
  assert.equal(fixture(composer, 'https://www.facebook.com/groups/other/').read().Status, 'wrong-page');
});
test('authentication and restriction states are separate', () => {
  assert.equal(fixture(composer + '<input type=password>').read().Status, 'authentication');
  assert.equal(fixture(composer + '<div role=alert>You are temporarily blocked</div>').read().Status, 'restricted');
});
test('hidden fields/buttons do not add ambiguous matches', () => {
  const f = fixture(composer.replace('</div><button>', '</div><div hidden><div role=textbox contenteditable=true></div><button>Post</button></div><button>'));
  const result = f.read(); assert.equal(result.EditorCount, 1); assert.equal(result.SubmitCount, 1);
});
test('loading dialogs can be observed without treating them as a failed publication', () => {
  const result = fixture('<div role=dialog>Loading...</div>').read();
  assert.equal(result.Status, 'ok'); assert.equal(result.DialogCount, 1); assert.equal(result.ComposerCount, 0);
});
test('two actual editors remain ambiguous', () => {
  const result = fixture(composer.replace('</div><button>', '</div><div role=textbox contenteditable=true></div><button>')).read();
  assert.equal(result.EditorCount, 2);
});
test('inline composer and textarea are supported', () => {
  const result = fixture('<main><form><textarea aria-label="Post text"></textarea><button>Publică</button></form></main>').read();
  assert.equal(result.ComposerCount, 1); assert.equal(result.EditorCount, 1);
});
test('collects correlated source links, group permalink and timestamp', () => {
  const url = groupUrl + 'posts/456/';
  const f = fixture(`<article><a href="${postUrl}">Source</a><a aria-label="Just now" href="${url}">time</a></article>`);
  const article = f.read().Articles[0]; assert.equal(article.Url, url); assert.equal(article.HasSource, true); assert.ok(article.Timestamp > 0);
});
test('nested comments do not hide the enclosing post', () => {
  const url = groupUrl + 'posts/456/';
  const f = fixture(`<article><a href="${postUrl}">Source</a><a href="${url}">time</a><div role=article>Comment</div></article>`);
  assert.equal(f.read().Articles.length, 1); assert.equal(f.read().Articles[0].HasSource, true);
});
test('external or comment permalinks are not accepted as group post results', () => {
  const f = fixture(`<article><a href="${postUrl}">Source</a><a href="https://other.test/groups/test-group/posts/456/">fake</a><a href="${groupUrl}posts/456/?comment_id=1">comment</a></article>`);
  assert.equal(f.read().Articles.length, 0);
});
