using Hugoer.Services;

const string generatedConfig = """
theme = "Stack"
baseURL = "https://example.org/"
locale = "zh-tw"
title = "My New Hugo Project"
[menu]
[[menu.main]]
identifier = "home"
name = "Home"
url = "/"
weight = 1
[menu.main.params]
icon = "home"
[[menu.main]]
identifier = "archives"
name = "Archives"
url = "/archives/"
weight = 2
[menu.main.params]
icon = "archives"
[params]
description = "A personal blog powered by Hugo and Stack"
mainSections = ["post"]
[params.colorScheme]
toggle = true
default = "auto"
[markup]
[markup.goldmark]
[markup.goldmark.renderer]
unsafe = true
""";

var service = new HugoConfigService();
var fields = service.LoadForm(generatedConfig);
if (fields.Count == 0)
    throw new InvalidOperationException("Expected generated Hugo config to produce fields.");

var rewritten = service.ApplyToToml(generatedConfig, fields);
if (!rewritten.Contains("menu", StringComparison.Ordinal) ||
    !rewritten.Contains("[[menu.main]]", StringComparison.Ordinal))
    throw new InvalidOperationException("Rewritten Hugo config lost the menu table.");

var roundTrip = service.LoadForm(rewritten);
if (roundTrip.Count < fields.Count)
    throw new InvalidOperationException("Rewritten Hugo config could not be parsed back into the catalog.");

Console.WriteLine("HUGO_CONFIG_HARNESS_OK");
