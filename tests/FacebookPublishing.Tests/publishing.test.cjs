const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { JSDOM } = require('jsdom');
const script = readFileSync('../../src/GroupPilot.Core/FacebookPublishing.js', 'utf8');
const groupUrl = 'https://www.facebook.com/groups/test-group/';
const postUrl = 'https://www.facebook.com/source/posts/123/';
const permalink = groupUrl + 'posts/456/';
const composer = `<div role="dialog"><h2>Create post</h2><div role="textbox" contenteditable="true"></div><button>Post</button></div>`;

function fixture(html = composer, url = groupUrl) {
  const dom = new JSDOM(html, { url, runScripts: 'outside-only' });
  const { window } = dom;
  // jsdom has no layout engine; retain visibility and hidden-element behavior in this fixture.
  window.Element.prototype.getClientRects = function () { return this.closest('[hidden]') ? [] : [{}]; };
  const document = window.document;
  document.execCommand = (_, __, value) => {
    document.querySelector('[contenteditable=true]').textContent = value;
    return true;
  };
  let clicks = 0;
  document.querySelector('button')?.addEventListener('click', () => clicks++);
  const invoke = (operation, extra = {}) => JSON.parse(JSON.stringify(window.eval(script)({
    operation, token: 'attempt-1', groupUrl, postUrl, ...extra
  })));
  function ready() { assert.equal(invoke('prepare').status, 'waiting'); assert.equal(invoke('prepare').status, 'ready'); }
  const signal = message => { const element = document.createElement('div'); element.role = 'status'; element.textContent = message; document.body.append(element); };
  return { dom, window, document, invoke, ready, signal, clicks: () => clicks };
}

test('prepares the selected URL and clicks Publish only once', () => {
  const f = fixture(); f.ready();
  assert.equal(f.invoke('submit').status, 'submitted');
  assert.equal(f.invoke('submit').status, 'uncertain');
  assert.equal(f.clicks(), 1);
  assert.equal(f.invoke('observe').status, 'waiting');
  f.signal('Your post has been published.');
  assert.equal(f.invoke('observe').status, 'published');
});

test('dialog disappearance alone is never publication evidence', () => {
  const f = fixture(); f.ready(); f.invoke('submit');
  f.document.querySelector('[role=dialog]').remove();
  assert.equal(f.invoke('observe').status, 'waiting');
});

test('an old success toast is not confirmation of the current post', () => {
  const f = fixture(); f.signal('Your post was published.'); f.ready(); f.invoke('submit');
  assert.equal(f.invoke('observe').status, 'waiting');
});

test('approval and explicit errors have separate outcomes', () => {
  const pending = fixture(); pending.ready(); pending.invoke('submit'); pending.signal('Your post is pending approval.');
  assert.equal(pending.invoke('observe').status, 'approval');
  const error = fixture(); error.ready(); error.invoke('submit'); error.signal("Couldn't publish your post.");
  assert.equal(error.invoke('observe').status, 'uncertain');
});

test('wrong group and changed content cannot be submitted', () => {
  const wrong = fixture(composer, 'https://www.facebook.com/groups/other/');
  assert.equal(wrong.invoke('prepare').status, 'wrong-page'); assert.equal(wrong.clicks(), 0);
  const changed = fixture(); changed.ready(); changed.document.querySelector('[role=textbox]').textContent = 'A different draft';
  assert.equal(changed.invoke('submit').status, 'occupied'); assert.equal(changed.clicks(), 0);
});

test('login and restriction dialogs stop preparation', () => {
  const login = fixture(composer + '<input type="password">'); assert.equal(login.invoke('prepare').status, 'authentication');
  const restricted = fixture(composer + '<div role="alert">You are temporarily blocked</div>');
  assert.equal(restricted.invoke('prepare').status, 'restricted'); assert.equal(restricted.clicks(), 0);
});

test('disabled or ambiguous Publish buttons are never clicked', () => {
  const disabled = fixture(); disabled.ready(); disabled.document.querySelector('button').disabled = true;
  assert.equal(disabled.invoke('submit').status, 'waiting'); assert.equal(disabled.clicks(), 0);
  const multiple = fixture(composer.replace('</button>', '</button><button>Post</button>'));
  multiple.invoke('prepare'); assert.equal(multiple.invoke('prepare').status, 'waiting'); assert.equal(multiple.clicks(), 0);
});

test('nested dialogs do not duplicate editors and a token change blocks submission', () => {
  const f = fixture(`<div role="dialog">${composer}</div>`); f.ready();
  assert.equal(f.invoke('submit', { token: 'another-attempt' }).status, 'uncertain'); assert.equal(f.clicks(), 0);
});

test('fresh correlated group article records its permalink, old article does not', () => {
  const article = `<article><a href="${postUrl}">Source</a><a aria-label="Just now" href="${permalink}">time</a></article>`;
  const old = fixture(composer + article); old.ready(); old.invoke('submit'); old.document.querySelector('[role=dialog]').remove();
  assert.equal(old.invoke('observe').status, 'waiting');
  const fresh = fixture(); fresh.ready(); fresh.invoke('submit'); fresh.document.querySelector('[role=dialog]').remove();
  fresh.document.body.insertAdjacentHTML('beforeend', article);
  const result = fresh.invoke('observe'); assert.equal(result.status, 'published'); assert.equal(result.url, permalink);
});

test('navigating away or losing the document context cannot trigger a retry', () => {
  const f = fixture(); f.ready(); f.invoke('submit');
  f.dom.reconfigure({ url: 'https://www.facebook.com/groups/other/' });
  assert.equal(f.invoke('observe').status, 'wrong-page');
  f.dom.reconfigure({ url: groupUrl }); delete f.window.__groupPilotPublication;
  assert.equal(f.invoke('observe').status, 'uncertain'); assert.equal(f.clicks(), 1);
});
