using Hugoer.Services;

var toml = """
+++
date = '2026-08-22T22:40:12+08:00'
draft = true
title = 'Hello World'
+++

Ox Alpha
""";

var mixed = """
---
title: "20260822"
draft: false
---

+++ date = '2026-08-22T22:40:12+08:00' draft = true title = 'Hello World' +++

Ox Alpha
""";

var service = new FrontMatterService();
var tomlDocument = service.Parse(toml);
Assert(tomlDocument.Fields["title"] == "Hello World", "TOML title must be parsed.");
Assert(tomlDocument.Fields["draft"] == "true", "TOML draft must be parsed.");
Assert(!tomlDocument.Body.Contains("+++", StringComparison.Ordinal), "TOML delimiters must not leak into the body.");

var mixedDocument = service.Parse(mixed);
Assert(mixedDocument.Fields["title"] == "20260822", "Outer title must win when cleaning duplicated front matter.");
Assert(mixedDocument.Fields["date"] == "2026-08-22T22:40:12+08:00", "Inner date should be recovered.");
Assert(mixedDocument.Body.Trim() == "Ox Alpha", "Duplicated front matter must be stripped from body.");

var previewBody = MarkdownPreviewService.StripFrontMatter(mixed).Trim();
Assert(previewBody == "Ox Alpha", $"Preview body should only contain Markdown content, got: {previewBody}");

var yamlLists = """
---
title: Nested
categories:
  - alpha
  - beta
tags:
- one
- two
cover:
  image: /image/hero.jpg
  caption: Hero
description: |
  line one
  line two
---

Body
""";
var yamlDocument = service.Parse(yamlLists);
Assert(yamlDocument.Fields["title"] == "Nested", "YAML title");
Assert(yamlDocument.Fields["categories"] == "alpha, beta", "YAML block categories");
Assert(yamlDocument.Fields["tags"] == "one, two", "YAML unindented tags");
Assert(yamlDocument.Fields["image"] == "/image/hero.jpg", "Nested cover.image flattens to image");
Assert(yamlDocument.Fields["description"].Contains("line one", StringComparison.Ordinal), "YAML block scalar description");
Assert(yamlDocument.Body.Trim() == "Body", "YAML body");

Console.WriteLine("FRONT_MATTER_FORMATS_HARNESS_OK");

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
