/*
 * Hugoer article surface.
 * Quill (https://quilljs.com/, v2) edits the document. Snapshots are normalized
 * back to the HTML dialect MarkdownWysiwygConverter already understands.
 */
(function (root) {
  'use strict';

  var RAW_SELECTOR = [
    'table',
    'audio',
    'video',
    '.hugoer-pdf-embed',
    'div.markdown-alert',
    'div.math-block',
    'div.math-display',
    'div.footnotes',
    'dl',
    'details'
  ].join(',');

  function isSafeUrl(value, kind) {
    var url = String(value || '').trim();
    if (!url || /[\u0000-\u001f]/.test(url)) return false;
    if (/^(?:javascript|vbscript|file):/i.test(url)) return false;
    if (/^data:/i.test(url)) return kind === 'image' && /^data:image\//i.test(url);
    return true;
  }

  function languageFromValue(value) {
    if (typeof value !== 'string') return '';
    if (!value || value === 'true') return '';
    return /^[A-Za-z0-9_+-]+$/.test(value) ? value : '';
  }

  function applyAttribute(delta, format, value) {
    var Delta = root.Quill.import('delta');
    return delta.reduce(function (next, op) {
      if (!op.insert) return next;
      if (op.attributes && op.attributes[format]) return next.push(op);
      var attributes = Object.assign({}, op.attributes || {});
      attributes[format] = value;
      return next.insert(op.insert, attributes);
    }, new Delta());
  }

  function installFormats(Quill) {
    var Delta = Quill.import('delta');
    var Link = Quill.import('formats/link');
    var BaseImage = Quill.import('formats/image');
    var CodeBlock = Quill.import('formats/code-block');
    var BlockEmbed = Quill.import('blots/block/embed');

    Link.sanitize = function (url) {
      return isSafeUrl(url, 'link') ? String(url).trim() : Link.SANITIZED_URL;
    };

    var imageAttributes = ['alt', 'height', 'width', 'data-hugoer-align', 'data-hugoer-src'];

    class HugoerImage extends BaseImage {
      static create(value) {
        var source = typeof value === 'string' ? value : (value && value.src) || '';
        return super.create(source);
      }

      static sanitize(url) {
        return isSafeUrl(url, 'image') ? String(url).trim() : '//:0';
      }

      static formats(domNode) {
        var formats = {};
        imageAttributes.forEach(function (name) {
          if (domNode.hasAttribute(name)) formats[name] = domNode.getAttribute(name);
        });
        return formats;
      }

      format(name, value) {
        if (imageAttributes.indexOf(name) === -1) {
          super.format(name, value);
          return;
        }
        if (value) this.domNode.setAttribute(name, value);
        else this.domNode.removeAttribute(name);
        if (name === 'width') applyImageWidth(this.domNode, value);
        if (name === 'data-hugoer-align') applyImageAlign(this.domNode, value || null);
      }
    }
    HugoerImage.blotName = 'image';
    HugoerImage.tagName = 'IMG';
    Quill.register(HugoerImage, true);

    var baseCodeCreate = CodeBlock.create;
    var baseCodeFormat = CodeBlock.prototype.format;
    CodeBlock.create = function (value) {
      var node = baseCodeCreate.call(this, value);
      var language = languageFromValue(value);
      if (language) node.setAttribute('data-language', language);
      return node;
    };
    CodeBlock.formats = function (domNode) {
      if (!domNode || domNode.tagName === 'PRE') return true;
      return domNode.getAttribute('data-language') || true;
    };
    CodeBlock.prototype.format = function (name, value) {
      if (typeof baseCodeFormat === 'function') baseCodeFormat.call(this, name, value);
      if (name !== 'code-block') return;
      var language = languageFromValue(value);
      if (language) this.domNode.setAttribute('data-language', language);
      else this.domNode.removeAttribute('data-language');
      var container = this.domNode.parentElement;
      if (container && container.classList.contains('ql-code-block-container')) {
        if (language) container.setAttribute('data-language', language);
        else container.removeAttribute('data-language');
      }
    };

    class DividerBlot extends BlockEmbed {
      static create() {
        return super.create();
      }

      static value() {
        return true;
      }
    }
    DividerBlot.blotName = 'divider';
    DividerBlot.tagName = 'HR';
    Quill.register(DividerBlot);

    class HugoerRawBlot extends BlockEmbed {
      static create(value) {
        var node = super.create();
        var payload = value && typeof value === 'object' ? value : { html: String(value || ''), kind: '' };
        node.setAttribute('contenteditable', 'false');
        node.setAttribute('data-hugoer-kind', payload.kind || '');
        var inner = document.createElement('div');
        inner.className = 'hugoer-raw-body';
        inner.innerHTML = payload.html || '';
        if (inner.querySelector('table, td, th')) inner.setAttribute('contenteditable', 'true');
        node.appendChild(inner);
        return node;
      }

      static value(domNode) {
        var inner = domNode.querySelector(':scope > .hugoer-raw-body');
        return {
          html: inner ? inner.innerHTML : '',
          kind: domNode.getAttribute('data-hugoer-kind') || ''
        };
      }

      constructor(scroll, node) {
        super(scroll, node);
        var inner = this.domNode.querySelector(':scope > .hugoer-raw-body');
        if (!inner) return;
        var stop = function (event) { event.stopPropagation(); };
        inner.addEventListener('keydown', stop);
        inner.addEventListener('keyup', stop);
        inner.addEventListener('paste', stop);
        inner.addEventListener('beforeinput', stop);
        inner.addEventListener('drop', stop);
      }

      updateContent() {}
    }
    HugoerRawBlot.blotName = 'hugoerRaw';
    HugoerRawBlot.tagName = 'DIV';
    HugoerRawBlot.className = 'hugoer-raw';
    Quill.register(HugoerRawBlot);

    return Delta;
  }

  function prepareIncomingHtml(document, html) {
    var parser = new document.defaultView.DOMParser();
    var parsed = parser.parseFromString(String(html || ''), 'text/html');
    var body = parsed.body;

    body.querySelectorAll('li').forEach(function (item) {
      var box = item.querySelector(':scope > input[type="checkbox"], :scope > p > input[type="checkbox"]');
      if (!box) return;
      var checked = box.hasAttribute('checked') || box.getAttribute('aria-checked') === 'true';
      box.remove();
      item.setAttribute('data-list', checked ? 'checked' : 'unchecked');
    });

    body.querySelectorAll('sup.footnote-ref, a.footnote-ref').forEach(function (node) {
      var label = String(node.textContent || '').replace(/[^\dA-Za-z_-]/g, '');
      if (!label) return;
      node.replaceWith(parsed.createTextNode('[^' + label + ']'));
    });

    body.querySelectorAll('span.math-inline, span.math').forEach(function (node) {
      var math = String(node.textContent || '').trim().replace(/^\\\(/, '').replace(/\\\)$/, '').trim();
      node.replaceWith(parsed.createTextNode('$' + math + '$'));
    });

    var rawNodes = Array.from(body.querySelectorAll(RAW_SELECTOR)).sort(function (a, b) {
      return depth(b) - depth(a);
    });
    rawNodes.forEach(function (node) {
      if (node.closest('.hugoer-raw')) return;
      var wrap = parsed.createElement('div');
      wrap.className = 'hugoer-raw';
      wrap.setAttribute('data-hugoer-kind', (node.getAttribute('class') || node.tagName).toLowerCase());
      var inner = parsed.createElement('div');
      inner.className = 'hugoer-raw-body';
      node.replaceWith(wrap);
      inner.appendChild(node);
      wrap.appendChild(inner);
    });

    return body.innerHTML;
  }

  function depth(node) {
    var count = 0;
    while (node) {
      count += 1;
      node = node.parentElement;
    }
    return count;
  }

  function normalizeEditorHtml(document, root) {
    var clone = root.cloneNode(true);
    clone.querySelectorAll('.ql-cursor, .ql-tooltip, .ql-clipboard').forEach(function (node) {
      node.remove();
    });
    restoreAuthoredSrc(clone);
    convertCodeBlocks(document, clone);
    convertLists(document, clone);
    unwrapRaw(document, clone);
    clone.querySelectorAll('img').forEach(function (img) {
      img.classList.remove('hugoer-img-selected', 'hugoer-img-moving');
      if (!img.className) img.removeAttribute('class');
    });
    var html = clone.innerHTML.trim();
    return isVisuallyEmpty(document, html) ? '' : html;
  }

  function restoreAuthoredSrc(scope) {
    scope.querySelectorAll('[data-hugoer-src]').forEach(function (node) {
      var authored = node.getAttribute('data-hugoer-src');
      if (authored) node.setAttribute('src', authored);
    });
  }

  function convertCodeBlocks(document, scope) {
    Array.from(scope.querySelectorAll('.ql-code-block-container')).forEach(function (container) {
      var lines = Array.from(container.querySelectorAll('.ql-code-block')).map(function (line) {
        var text = typeof line.innerText === 'string' ? line.innerText : (line.textContent || '');
        return text.replace(/\u200B|\uFEFF/g, '').replace(/\n$/, '');
      });
      var language = container.getAttribute('data-language')
        || (container.querySelector('[data-language]') || {}).getAttribute && container.querySelector('[data-language]').getAttribute('data-language')
        || '';
      var pre = document.createElement('pre');
      var code = document.createElement('code');
      if (language && language !== 'true') code.className = 'language-' + language;
      code.textContent = lines.join('\n');
      pre.appendChild(code);
      container.replaceWith(pre);
    });
  }

  function convertLists(document, scope) {
    var lists = Array.from(scope.querySelectorAll('ol, ul')).filter(function (list) {
      return list.querySelector(':scope > li[data-list]');
    });
    lists.forEach(function (list) {
      var items = Array.from(list.children).filter(function (node) {
        return node.tagName === 'LI';
      }).map(readListItem);
      var fragment = document.createDocumentFragment();
      var index = 0;
      while (index < items.length) {
        var built = buildList(document, items, index, items[index].indent);
        fragment.appendChild(built.list);
        index = built.next;
      }
      list.replaceWith(fragment);
    });
  }

  function readListItem(item) {
    var type = item.getAttribute('data-list') || 'ordered';
    var indentMatch = /(?:^|\s)ql-indent-(\d+)/.exec(item.className || '');
    var clone = item.cloneNode(true);
    clone.querySelectorAll('.ql-ui').forEach(function (node) { node.remove(); });
    var html = clone.innerHTML.replace(/^(?:\s|&nbsp;|\u00a0|\uFEFF|\u200B)+/, '');
    return {
      type: type,
      indent: indentMatch ? parseInt(indentMatch[1], 10) : 0,
      html: html
    };
  }

  function listTag(type) {
    return type === 'ordered' ? 'ol' : 'ul';
  }

  function buildList(document, items, start, indent) {
    var list = document.createElement(listTag(items[start].type));
    var index = start;
    while (index < items.length) {
      var item = items[index];
      if (item.indent < indent) break;
      if (item.indent > indent) break;
      if (list.childElementCount && listTag(item.type) !== list.tagName.toLowerCase()) break;
      var li = document.createElement('li');
      var prefix = '';
      if (item.type === 'checked') prefix = '<input type="checkbox" checked="checked"> ';
      else if (item.type === 'unchecked') prefix = '<input type="checkbox"> ';
      li.innerHTML = prefix + item.html;
      index += 1;
      if (index < items.length && items[index].indent > indent) {
        var nested = buildList(document, items, index, indent + 1);
        li.appendChild(nested.list);
        index = nested.next;
      }
      list.appendChild(li);
    }
    return { list: list, next: index };
  }

  function unwrapRaw(document, scope) {
    Array.from(scope.querySelectorAll('.hugoer-raw')).forEach(function (node) {
      var inner = node.querySelector(':scope > .hugoer-raw-body');
      var template = document.createElement('template');
      template.innerHTML = inner ? inner.innerHTML : '';
      node.replaceWith(template.content);
    });
  }

  function isVisuallyEmpty(document, html) {
    var probe = document.createElement('div');
    probe.innerHTML = html;
    if (probe.querySelector('img, hr, table, audio, video, iframe, pre, ul, ol, blockquote')) return false;
    var text = String(probe.textContent || '').replace(/[\s\u00a0\u200B\uFEFF]/g, '');
    return text.length === 0;
  }

  function applyImageAlign(img, align) {
    if (!img) return;
    if (!align) {
      img.removeAttribute('data-hugoer-align');
      img.style.display = '';
      img.style.cssFloat = '';
      img.style.margin = '';
      return;
    }
    img.setAttribute('data-hugoer-align', align);
    img.style.maxWidth = '100%';
    img.style.height = 'auto';
    img.style.display = 'block';
    if (align === 'wrap-left') {
      img.style.cssFloat = 'left';
      img.style.margin = '0.25em 1em 0.8em 0';
    } else if (align === 'wrap-right') {
      img.style.cssFloat = 'right';
      img.style.margin = '0.25em 0 0.8em 1em';
    } else {
      img.style.cssFloat = 'none';
      if (align === 'center') img.style.margin = '0.5em auto';
      else if (align === 'right') img.style.margin = '0.5em 0 0.5em auto';
      else img.style.margin = '0.5em auto 0.5em 0';
    }
  }

  function applyImageWidth(img, width) {
    var parsed = parseInt(width, 10);
    if (!parsed) {
      img.style.width = '';
      img.removeAttribute('width');
      return 0;
    }
    img.style.maxWidth = '100%';
    img.style.height = 'auto';
    img.style.width = parsed + 'px';
    img.setAttribute('width', String(parsed));
    return parsed;
  }

  function mount(document) {
    var Quill = root.Quill;
    if (!Quill) throw new Error('Quill failed to load');
    installFormats(Quill);
    var Delta = Quill.import('delta');
    var editorHost = document.getElementById('editor');
    var mask = document.getElementById('mask');
    var quill = new Quill(editorHost, {
      theme: null,
      placeholder: '開始撰寫文章…',
      modules: {
        toolbar: false,
        history: { delay: 400, maxStack: 200, userOnly: true },
        uploader: { handler: function () {} }
      }
    });

    var suppress = false;
    var changeTimer = 0;
    var dialogKind = 'link';
    var savedRange = null;
    var mediaMap = Object.create(null);
    var selectedImg = null;
    var resizeState = null;
    var moveState = null;
    var imgChrome = document.getElementById('imgChrome');
    var imgSizeLabel = document.getElementById('imgSize');
    var imgDrop = document.getElementById('imgDrop');

    quill.clipboard.addMatcher('DEL', function (node, delta) {
      return applyAttribute(delta, 'strike', true);
    });
    quill.clipboard.addMatcher('S', function (node, delta) {
      return applyAttribute(delta, 'strike', true);
    });
    quill.clipboard.addMatcher('STRIKE', function (node, delta) {
      return applyAttribute(delta, 'strike', true);
    });
    quill.clipboard.addMatcher('PRE', function (node, delta) {
      var code = node.querySelector && node.querySelector('code');
      var className = ((code && code.className) || '') + ' ' + (node.className || '');
      var match = /(?:language-|lang-)([A-Za-z0-9_+-]+)/.exec(className);
      var language = match ? match[1] : true;
      return delta.reduce(function (next, op) {
        if (!op.insert) return next;
        if (!op.attributes || !op.attributes['code-block']) return next.push(op);
        var attributes = Object.assign({}, op.attributes, { 'code-block': language });
        return next.insert(op.insert, attributes);
      }, new Delta());
    });

    function post(payload) {
      if (typeof root.invokeCSharpAction === 'function')
        root.invokeCSharpAction(JSON.stringify(payload));
    }

    function snapshotHtml() {
      return normalizeEditorHtml(document, quill.root);
    }

    function notifyChange() {
      if (suppress) return;
      root.clearTimeout(changeTimer);
      changeTimer = root.setTimeout(function () {
        post({ type: 'change', html: snapshotHtml() });
      }, 140);
    }

    function lookupMedia(src) {
      if (!src) return null;
      if (mediaMap[src]) return mediaMap[src];
      try {
        var decoded = decodeURI(src);
        if (decoded !== src && mediaMap[decoded]) return mediaMap[decoded];
      } catch (error) { /* ignore malformed escape */ }
      return null;
    }

    function applyMedia(scope) {
      (scope || quill.root).querySelectorAll('img[src], audio[src], video[src], source[src]').forEach(function (node) {
        var src = node.getAttribute('src');
        if (!src || /^(data:|blob:|https?:|file:)/i.test(src)) return;
        var mapped = lookupMedia(src);
        if (!mapped) return;
        if (!node.getAttribute('data-hugoer-src')) node.setAttribute('data-hugoer-src', src);
        node.setAttribute('src', mapped);
      });
      (scope || quill.root).querySelectorAll('img[data-hugoer-align]').forEach(function (img) {
        applyImageAlign(img, img.getAttribute('data-hugoer-align'));
      });
      if (selectedImg) layoutImageChrome();
    }

    function mergeMedia(media) {
      if (!media) return;
      Object.assign(mediaMap, media);
    }

    function focusEditor() {
      try {
        quill.focus({ preventScroll: true });
      } catch (error) {
        try { quill.root.focus({ preventScroll: true }); } catch (ignored) { /* keep the command usable */ }
      }
    }

    function currentRange() {
      focusEditor();
      return quill.getSelection() || { index: Math.max(0, quill.getLength() - 1), length: 0 };
    }

    function toggleFormat(name, value) {
      var range = currentRange();
      var format = quill.getFormat(range);
      if (value === undefined) quill.format(name, !format[name], 'user');
      else quill.format(name, String(format[name]) === String(value) ? false : value, 'user');
    }

    function insertPreparedHtml(html) {
      var range = currentRange();
      var prepared = prepareIncomingHtml(document, html);
      var pasted = quill.clipboard.convert({ html: prepared });
      quill.updateContents(new Delta().retain(range.index).delete(range.length).concat(pasted), 'user');
      var end = range.index + pasted.length();
      quill.setSelection(end, 0, 'silent');
      applyMedia(quill.root);
      var images = quill.root.querySelectorAll('img');
      if (/<img\b/i.test(html || '') && images.length) selectImage(images[images.length - 1]);
      notifyChange();
    }

    function insertBlockText(text, format, formatValue) {
      var range = currentRange();
      var index = range.index;
      if (range.length) quill.deleteText(range.index, range.length, 'user');
      var line = quill.getLine(index);
      var lineStart = index - line[1];
      var existing = quill.getText(lineStart, Math.max(0, line[0].length() - 1)).trim();
      if (existing) {
        var lineEnd = lineStart + line[0].length();
        quill.insertText(lineEnd, '\n', 'user');
        index = lineEnd;
      } else {
        index = lineStart;
      }
      quill.insertText(index, text, 'user');
      quill.formatLine(index, text.length, format, formatValue, 'user');
      quill.setSelection(index + text.length, 0, 'silent');
    }

    function openDialog(kind) {
      dialogKind = kind;
      savedRange = quill.getSelection(true);
      var selected = savedRange && savedRange.length
        ? quill.getText(savedRange.index, savedRange.length)
        : '';
      document.getElementById('dlgTitle').textContent = kind === 'image' ? '插入圖片' : '插入連結';
      document.getElementById('dlgTextLabel').textContent = kind === 'image' ? '替代文字' : '顯示文字';
      document.getElementById('dlgHrefLabel').textContent = kind === 'image' ? '圖片路徑或網址' : '網址';
      document.getElementById('dlgText').value = selected || (kind === 'image' ? '圖片說明' : '連結文字');
      document.getElementById('dlgHref').value = kind === 'image' ? 'image.jpg' : 'https://';
      mask.classList.add('open');
      root.setTimeout(function () { document.getElementById('dlgHref').focus(); }, 20);
    }

    function closeDialog() {
      mask.classList.remove('open');
      focusEditor();
    }

    function linePrefix(index) {
      var line = quill.getLine(index);
      var start = index - line[1];
      return { start: start, text: quill.getText(start, line[1]) };
    }

    function tryMarkdownShortcut(range) {
      if (!range || range.length) return false;
      var prefix = linePrefix(range.index);
      var text = prefix.text;
      var heading = /^(#{1,6})$/.exec(text);
      if (heading) {
        quill.deleteText(prefix.start, text.length, 'user');
        quill.formatLine(prefix.start, 1, 'header', heading[1].length, 'user');
        return true;
      }
      if (text === '>') {
        quill.deleteText(prefix.start, 1, 'user');
        quill.formatLine(prefix.start, 1, 'blockquote', true, 'user');
        return true;
      }
      if (text === '-' || text === '*') {
        quill.deleteText(prefix.start, 1, 'user');
        quill.formatLine(prefix.start, 1, 'list', 'bullet', 'user');
        return true;
      }
      if (/^\d+\.$/.test(text)) {
        quill.deleteText(prefix.start, text.length, 'user');
        quill.formatLine(prefix.start, 1, 'list', 'ordered', 'user');
        return true;
      }
      if (text === '---' || text === '***') {
        quill.deleteText(prefix.start, text.length, 'user');
        quill.insertEmbed(prefix.start, 'divider', true, 'user');
        return true;
      }
      if (text === '```' || text === '``') {
        quill.deleteText(prefix.start, text.length, 'user');
        quill.insertText(prefix.start, '程式碼', 'user');
        quill.formatLine(prefix.start, 1, 'code-block', true, 'user');
        return true;
      }
      return false;
    }

    function editorMaxImageWidth() {
      var style = root.getComputedStyle(quill.root);
      var pad = (parseFloat(style.paddingLeft) || 0) + (parseFloat(style.paddingRight) || 0);
      return Math.max(120, Math.floor(quill.root.clientWidth - pad));
    }

    function clampImageWidth(img, width) {
      var max = editorMaxImageWidth();
      return applyImageWidth(img, Math.max(64, Math.min(max, Math.round(width))));
    }

    function layoutImageChrome() {
      if (!selectedImg || !selectedImg.isConnected) {
        clearImageSelection();
        return;
      }
      var rect = selectedImg.getBoundingClientRect();
      if (rect.width < 2 || rect.height < 2) {
        imgChrome.classList.remove('open');
        return;
      }
      imgChrome.style.left = (rect.left - 2) + 'px';
      imgChrome.style.top = (rect.top - 2) + 'px';
      imgChrome.style.width = rect.width + 'px';
      imgChrome.style.height = rect.height + 'px';
      imgChrome.classList.toggle('toolbar-below', rect.top < 52);
      imgChrome.classList.toggle('toolbar-end', rect.left + 280 > root.innerWidth);
      imgChrome.classList.add('open');
      imgSizeLabel.textContent = Math.round(rect.width) + ' px';
      var align = selectedImg.getAttribute('data-hugoer-align') || '';
      imgChrome.querySelectorAll('#imgToolbar [data-align]').forEach(function (button) {
        button.classList.toggle('on', button.getAttribute('data-align') === align);
      });
    }

    function selectImage(img) {
      if (selectedImg && selectedImg !== img) selectedImg.classList.remove('hugoer-img-selected');
      selectedImg = img;
      img.classList.add('hugoer-img-selected');
      if (!img.complete) img.addEventListener('load', layoutImageChrome, { once: true });
      layoutImageChrome();
    }

    function clearImageSelection() {
      if (selectedImg) {
        selectedImg.classList.remove('hugoer-img-selected', 'hugoer-img-moving');
      }
      selectedImg = null;
      resizeState = null;
      moveState = null;
      imgChrome.classList.remove('open', 'moving');
      imgDrop.classList.remove('open');
    }

    function imageIndex(img) {
      var blot = Quill.find(img);
      if (!blot) return -1;
      return quill.getIndex(blot);
    }

    function isolateImage(img) {
      var index = imageIndex(img);
      if (index < 0) return -1;
      var line = quill.getLine(index);
      var lineStart = index - line[1];
      var before = quill.getText(lineStart, index - lineStart).replace(/\n/g, '').trim();
      if (before) {
        quill.insertText(index, '\n', 'user');
        index += 1;
      }
      var afterLine = quill.getLine(index);
      var afterStart = index - afterLine[1];
      var afterText = quill.getText(index + 1, Math.max(0, afterLine[0].length() - (index - afterStart) - 2));
      if (afterText.replace(/\n/g, '').trim()) quill.insertText(index + 1, '\n', 'user');
      return index;
    }

    function moveImageBy(direction) {
      if (!selectedImg) return;
      var index = isolateImage(selectedImg);
      if (index < 0) return;
      var line = quill.getLine(index);
      var from = index - line[1];
      var length = line[0].length();
      var dest = direction < 0 ? Math.max(0, from - 1) : from + length + 1;
      if (direction < 0) {
        var previous = quill.getLine(Math.max(0, from - 1));
        dest = from - previous[0].length();
        if (dest < 0) dest = 0;
        if (dest === from) return;
      } else {
        var nextIndex = from + length;
        if (nextIndex >= quill.getLength() - 1) return;
        dest = nextIndex;
      }
      moveRange(from, length, dest);
      root.setTimeout(function () {
        var images = quill.root.querySelectorAll('img');
        var blotIndex = direction < 0 ? dest : dest;
        var next = images[Math.min(images.length - 1, 0)];
        images.forEach(function (img) {
          var current = imageIndex(img);
          if (current >= 0 && Math.abs(current - blotIndex) < 3) next = img;
        });
        if (next) selectImage(next);
      }, 0);
      notifyChange();
    }

    function moveRange(from, length, dest) {
      var piece = quill.getContents(from, length);
      var delta = new Delta();
      if (dest < from) delta.retain(dest).concat(piece).retain(from - dest).delete(length);
      else delta.retain(from).delete(length).retain(dest - from).concat(piece);
      quill.updateContents(delta, 'user');
    }

    function commitImageFormat(img) {
      var blot = Quill.find(img);
      if (!blot) return;
      var index = quill.getIndex(blot);
      var width = img.getAttribute('width') || '';
      var align = img.getAttribute('data-hugoer-align') || '';
      quill.formatText(index, 1, 'width', width || false, 'user');
      quill.formatText(index, 1, 'data-hugoer-align', align || false, 'user');
    }

    document.getElementById('imgToolbar').addEventListener('mousedown', function (event) {
      event.preventDefault();
    });
    document.getElementById('imgToolbar').addEventListener('click', function (event) {
      var button = event.target.closest('button');
      if (!button || !selectedImg) return;
      if (button.id === 'imgReset') {
        selectedImg.removeAttribute('width');
        selectedImg.style.width = '';
        applyImageAlign(selectedImg, null);
      } else if (button.hasAttribute('data-move')) {
        moveImageBy(parseInt(button.getAttribute('data-move'), 10) || 0);
        return;
      } else if (button.hasAttribute('data-align')) {
        var align = button.getAttribute('data-align');
        var current = selectedImg.getAttribute('data-hugoer-align');
        applyImageAlign(selectedImg, current === align ? null : align);
      }
      commitImageFormat(selectedImg);
      layoutImageChrome();
      notifyChange();
    });

    imgChrome.querySelectorAll('.img-handle').forEach(function (handle) {
      handle.addEventListener('pointerdown', function (event) {
        if (!selectedImg) return;
        event.preventDefault();
        event.stopPropagation();
        resizeState = {
          handle: handle.getAttribute('data-handle'),
          startX: event.clientX,
          startWidth: selectedImg.getBoundingClientRect().width || parseInt(selectedImg.getAttribute('width') || '0', 10)
        };
      });
    });

    function onPointerMove(event) {
      if (resizeState && selectedImg) {
        event.preventDefault();
        var dx = event.clientX - resizeState.startX;
        var sign = (resizeState.handle === 'nw' || resizeState.handle === 'sw') ? -1 : 1;
        clampImageWidth(selectedImg, resizeState.startWidth + dx * sign);
        layoutImageChrome();
        return;
      }
      if (!moveState || !selectedImg || !moveState.active) return;
      var target = lineAtPoint(event.clientX, event.clientY);
      moveState.target = target;
      showDropMarker(target);
    }

    function onPointerUp() {
      if (resizeState && selectedImg) {
        resizeState = null;
        commitImageFormat(selectedImg);
        notifyChange();
        return;
      }
      if (!moveState) return;
      var active = moveState.active;
      var target = moveState.target;
      moveState = null;
      imgDrop.classList.remove('open');
      imgChrome.classList.remove('moving');
      if (selectedImg) selectedImg.classList.remove('hugoer-img-moving');
      if (!active || !target || !selectedImg) {
        layoutImageChrome();
        return;
      }
      var from = isolateImage(selectedImg);
      var line = quill.getLine(from);
      var start = from - line[1];
      var length = line[0].length();
      if (target.index === start) return;
      moveRange(start, length, target.before ? target.index : target.index + target.length);
      notifyChange();
    }

    function lineAtPoint(x, y) {
      var stack = document.elementsFromPoint ? document.elementsFromPoint(x, y) : [document.elementFromPoint(x, y)];
      var node = null;
      stack.forEach(function (candidate) {
        if (node || !candidate || !quill.root.contains(candidate)) return;
        if (candidate.closest && candidate.closest('#imgChrome')) return;
        node = candidate;
      });
      if (!node) return null;
      var blot = Quill.find(node, true);
      if (!blot) return null;
      var index = quill.getIndex(blot);
      var line = quill.getLine(index);
      if (!line[0]) return null;
      var lineIndex = index - line[1];
      var rect = line[0].domNode.getBoundingClientRect();
      return {
        index: lineIndex,
        length: line[0].length(),
        before: y < rect.top + rect.height / 2,
        rect: rect
      };
    }

    function showDropMarker(target) {
      if (!target) {
        imgDrop.classList.remove('open');
        return;
      }
      var rect = target.rect;
      imgDrop.style.top = ((target.before ? rect.top : rect.bottom) - 1) + 'px';
      imgDrop.style.left = rect.left + 'px';
      imgDrop.style.width = Math.max(24, rect.width) + 'px';
      imgDrop.classList.add('open');
    }

    document.getElementById('imgMoveHit').addEventListener('pointerdown', function (event) {
      if (!selectedImg || resizeState) return;
      moveState = { startX: event.clientX, startY: event.clientY, active: false, target: null };
      event.preventDefault();
      event.stopPropagation();
    });
    root.addEventListener('pointermove', function (event) {
      if (moveState && !moveState.active) {
        var dx = event.clientX - moveState.startX;
        var dy = event.clientY - moveState.startY;
        if (dx * dx + dy * dy >= 36) {
          moveState.active = true;
          selectedImg.classList.add('hugoer-img-moving');
          imgChrome.classList.add('moving');
        }
      }
      onPointerMove(event);
    });
    root.addEventListener('pointerup', onPointerUp);
    root.addEventListener('pointercancel', onPointerUp);
    root.addEventListener('scroll', layoutImageChrome, true);
    root.addEventListener('resize', layoutImageChrome);

    quill.root.addEventListener('pointerdown', function (event) {
      var img = event.target && event.target.closest ? event.target.closest('img') : null;
      if (!img || !quill.root.contains(img)) return;
      selectImage(img);
      event.preventDefault();
    });
    document.addEventListener('mousedown', function (event) {
      if (event.target.closest('#imgChrome') || event.target.closest('#mask')) return;
      if (event.target.closest('.ql-editor') && event.target.closest('img')) return;
      clearImageSelection();
    });
    document.addEventListener('keydown', function (event) {
      if (!selectedImg || mask.classList.contains('open')) return;
      var tag = event.target && event.target.tagName;
      if (tag === 'INPUT' || tag === 'TEXTAREA') return;
      if (event.key === 'Escape') {
        clearImageSelection();
        return;
      }
      if (event.key === 'ArrowUp') {
        event.preventDefault();
        moveImageBy(-1);
        return;
      }
      if (event.key === 'ArrowDown') {
        event.preventDefault();
        moveImageBy(1);
        return;
      }
      if (event.key === 'Delete' || event.key === 'Backspace') {
        event.preventDefault();
        var index = imageIndex(selectedImg);
        clearImageSelection();
        if (index >= 0) quill.deleteText(index, 1, 'user');
        notifyChange();
      }
    });

    document.getElementById('dlgCancel').addEventListener('click', closeDialog);
    document.getElementById('dlgOk').addEventListener('click', function () {
      var text = document.getElementById('dlgText').value || '';
      var href = document.getElementById('dlgHref').value || '';
      var range = savedRange || currentRange();
      closeDialog();
      if (!isSafeUrl(href, dialogKind)) return;
      quill.setSelection(range.index, range.length, 'silent');
      if (dialogKind === 'image') {
        quill.insertEmbed(range.index, 'image', href, 'user');
        quill.formatText(range.index, 1, 'alt', text, 'user');
        var images = quill.root.querySelectorAll('img');
        if (images.length) selectImage(images[images.length - 1]);
      } else if (range.length) {
        quill.formatText(range.index, range.length, 'link', href, 'user');
      } else {
        quill.insertText(range.index, text || href, { link: href }, 'user');
      }
      notifyChange();
    });
    mask.addEventListener('click', function (event) {
      if (event.target === mask) closeDialog();
    });

    quill.keyboard.addBinding({ key: ' ', collapsed: true }, function (range) {
      return !tryMarkdownShortcut(range);
    });
    quill.keyboard.addBinding({ key: 'k', shortKey: true }, function () {
      openDialog('link');
      return false;
    });
    quill.keyboard.addBinding({ key: 's', shortKey: true }, function () {
      post({ type: 'save', html: snapshotHtml() });
      return false;
    });
    quill.keyboard.addBinding({ key: 'm', shortKey: true, shiftKey: true }, function () {
      post({ type: 'toggleMode', html: snapshotHtml() });
      return false;
    });

    quill.on('text-change', function (_delta, _old, source) {
      if (suppress || source === 'silent') return;
      notifyChange();
    });
    quill.root.addEventListener('input', function () {
      if (!suppress) notifyChange();
    });
    quill.root.addEventListener('blur', function () {
      root.clearTimeout(changeTimer);
      if (!suppress) post({ type: 'change', html: snapshotHtml() });
    });

    root.hugoerSetHtml = function (html, media) {
      mergeMedia(media);
      suppress = true;
      clearImageSelection();
      var prepared = prepareIncomingHtml(document, html || '');
      var delta = prepared.trim()
        ? quill.clipboard.convert({ html: prepared })
        : new Delta();
      quill.setContents(delta, 'silent');
      applyMedia(quill.root);
      suppress = false;
    };
    root.hugoerAddMedia = function (media) {
      mergeMedia(media);
      applyMedia(quill.root);
    };
    root.hugoerFocus = function () {
      focusEditor();
    };
    root.hugoerFlush = function () {
      post({ type: 'flush', html: snapshotHtml() });
    };
    root.hugoerCommand = function (name, argument) {
      focusEditor();
      if (name === 'undo') { quill.history.undo(); notifyChange(); return; }
      if (name === 'redo') { quill.history.redo(); notifyChange(); return; }
      if (name === 'bold') { toggleFormat('bold'); return; }
      if (name === 'italic') { toggleFormat('italic'); return; }
      if (name === 'strike') { toggleFormat('strike'); return; }
      if (name === 'inlineCode') { toggleFormat('code'); return; }
      if (name === 'heading') { toggleFormat('header', parseInt(argument || '2', 10)); return; }
      if (name === 'quote') { toggleFormat('blockquote', true); return; }
      if (name === 'bulletList') { toggleFormat('list', 'bullet'); return; }
      if (name === 'orderedList') { toggleFormat('list', 'ordered'); return; }
      if (name === 'taskList') { insertBlockText('待辦項目', 'list', 'unchecked'); notifyChange(); return; }
      if (name === 'codeBlock') { insertBlockText('程式碼', 'code-block', true); notifyChange(); return; }
      if (name === 'link') { openDialog('link'); return; }
      if (name === 'image') { openDialog('image'); return; }
      if (name === 'hr') {
        var range = currentRange();
        quill.insertEmbed(range.index, 'divider', true, 'user');
        quill.setSelection(range.index + 1, 0, 'silent');
        notifyChange();
        return;
      }
      if (name === 'table') {
        insertPreparedHtml('<table><thead><tr><th>欄位一</th><th>欄位二</th></tr></thead><tbody><tr><td>內容</td><td>內容</td></tr></tbody></table>');
        return;
      }
      if (name === 'addMedia') {
        if (argument) {
          try { mergeMedia(JSON.parse(argument)); } catch (error) { /* ignore malformed media */ }
          applyMedia(quill.root);
        }
        return;
      }
      if (name === 'insertHtml') {
        if (argument) insertPreparedHtml(argument);
      }
    };
    root.hugoerSnapshot = snapshotHtml;

    post({ type: 'ready' });
    return quill;
  }

  root.HugoerQuill = {
    installFormats: installFormats,
    prepareIncomingHtml: prepareIncomingHtml,
    normalizeEditorHtml: normalizeEditorHtml,
    mount: mount
  };
})(typeof globalThis !== 'undefined' ? globalThis : window);
