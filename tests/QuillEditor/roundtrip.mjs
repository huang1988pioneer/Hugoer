import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { JSDOM, VirtualConsole } from '/tmp/quill-jsdom/node_modules/jsdom/lib/api.js';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const editorDir = path.join(root, 'Assets/editor');

function assemble() {
  let html = fs.readFileSync(path.join(editorDir, 'wysiwyg.html'), 'utf8');
  const replacements = {
    '/*__QUILL_CSS__*/': fs.readFileSync(path.join(editorDir, 'vendor/quill.core.css'), 'utf8'),
    '/*__QUILL_JS__*/': fs.readFileSync(path.join(editorDir, 'vendor/quill.js'), 'utf8'),
    '/*__HUGOER_EDITOR_JS__*/': fs.readFileSync(path.join(editorDir, 'hugoer-quill.js'), 'utf8'),
  };
  for (const [token, value] of Object.entries(replacements)) {
    if (!html.includes(token)) throw new Error(`Missing ${token}`);
    html = html.split(token).join(value);
  }
  const scripts = html.match(/<script>/g) || [];
  if (scripts.length !== 2) throw new Error(`Expected 2 script tags, found ${scripts.length}`);
  return html;
}

const virtualConsole = new VirtualConsole();
const errors = [];
virtualConsole.on('jsdomError', (error) => errors.push(String(error && error.stack || error)));
virtualConsole.on('error', (error) => errors.push(String(error)));

const dom = new JSDOM(assemble(), {
  url: 'about:blank',
  pretendToBeVisual: true,
  runScripts: 'dangerously',
  virtualConsole,
});

const { window } = dom;
const rect = () => ({
  x: 0, y: 0, top: 0, left: 0, bottom: 20, right: 80, width: 80, height: 20, toJSON() { return {}; },
});
window.Range.prototype.getBoundingClientRect = rect;
window.Range.prototype.getClientRects = () => [rect()];
window.Element.prototype.getBoundingClientRect = rect;
window.Element.prototype.getClientRects = () => [rect()];
window.scrollBy = () => {};
window.HTMLElement.prototype.scrollBy = () => {};
await new Promise((resolve) => setTimeout(resolve, 50));
if (errors.length) {
  console.error(errors.join('\n'));
  process.exit(1);
}
if (typeof window.hugoerSetHtml !== 'function') {
  console.error('Quill editor did not mount');
  console.error(window.document.body.innerHTML.slice(0, 500));
  process.exit(1);
}

const samples = JSON.parse(fs.readFileSync('/tmp/hugoer-html-samples.json', 'utf8'));
const snapshots = {};
for (const [name, html] of Object.entries(samples)) {
  window.hugoerSetHtml(html, {});
  snapshots[name] = window.hugoerSnapshot();
  console.log('----- ' + name + ' -----');
  console.log(snapshots[name]);
}

const expectations = {
  heading: ['<h1>標題</h1>', '段落文字'],
  emphasis: ['<strong>粗體</strong>', '<em>斜體</em>', '<s>刪除</s>'],
  lists: ['<ul><li>蘋果</li>', '<ol><li>第一</li>'],
  tasks: ['<input type="checkbox">', 'checked="checked"', '已完成'],
  link: ['href="https://gohugo.io"', 'src="cover.jpg"', 'alt="圖"'],
  media: ['<audio', 'src="/music/song.mp3"', '<video', 'src="/video/clip.mp4"'],
  code: ['language-csharp', 'Console.WriteLine(1);'],
  quote: ['<blockquote>引用段落</blockquote>'],
  table: ['<table>', '<th>欄位一</th>', '<td>內容</td>'],
  rule: ['<hr>', '上段', '下段'],
  shortcode: ['{{&lt; figure src="a.jpg" &gt;}}', '開頭', '結尾'],
  sized: ['src="/image/cat.png"', 'width="320"', 'data-hugoer-align="center"', 'alt="貓"'],
  nested: ['<ul><li>父<ul><li>子</li></ul></li><li>另一</li></ul>'],
};
for (const [name, needles] of Object.entries(expectations)) {
  for (const needle of needles) {
    if (!snapshots[name].includes(needle)) {
      throw new Error(`${name} snapshot missing ${needle}\n${snapshots[name]}`);
    }
  }
}

const quill = window.Quill.find(window.document.getElementById('editor'));
function commandSnapshot(html, name, argument, select) {
  window.hugoerSetHtml(html, {});
  if (select) quill.setSelection(select.index, select.length, 'silent');
  window.hugoerCommand(name, argument);
  return window.hugoerSnapshot();
}

const commandExpectations = [
  ['heading', '<p>段落</p>', 'heading', '2', null, '<h2>段落</h2>'],
  ['quote', '<p>段落</p>', 'quote', undefined, null, '<blockquote>段落</blockquote>'],
  ['bullet', '<p>段落</p>', 'bulletList', undefined, null, '<ul><li>段落</li></ul>'],
  ['ordered', '<p>段落</p>', 'orderedList', undefined, null, '<ol><li>段落</li></ol>'],
  ['bold', '<p>段落文字</p>', 'bold', undefined, { index: 0, length: 2 }, '<strong>段落</strong>'],
  ['code', '<p>段落</p>', 'codeBlock', undefined, null, '<pre><code>程式碼</code></pre>'],
  ['task', '<p>段落</p>', 'taskList', undefined, null, '<input type="checkbox">'],
  ['hr', '<p>段落</p>', 'hr', undefined, null, '<hr>'],
  ['table', '<p>段落</p>', 'table', undefined, null, '<th>欄位一</th>'],
];
for (const [name, html, command, argument, select, needle] of commandExpectations) {
  const snapshot = commandSnapshot(html, command, argument, select);
  console.log('----- command ' + name + ' -----');
  console.log(snapshot);
  if (!snapshot.includes(needle)) {
    throw new Error(`${name} command missing ${needle}\n${snapshot}`);
  }
}

window.hugoerSetHtml('<p>段落文字</p>', {});
quill.setSelection(0, 2, 'silent');
quill.blur();
window.hugoerCommand('bold');
const boldAfterBlur = window.hugoerSnapshot();
if (!boldAfterBlur.includes('<strong>段落</strong>')) {
  throw new Error('Bold did not keep the selection after blur:\n' + boldAfterBlur);
}

fs.writeFileSync('/tmp/hugoer-quill-snapshots.json', JSON.stringify(snapshots, null, 2));
console.log('QUILL_EDITOR_MOUNTED');
