using System.Text.RegularExpressions;

namespace Rex.Agent.Services;

/// <summary>
/// Minimal Handlebars-style template renderer.
/// Supports: {{Token}}, {{#if Flag}}...{{/if}}, {{#if !Flag}}...{{/if}}
/// </summary>
public static class TemplateEngine
{
    private static readonly Regex IfBlock =
        new(@"\{\{#if\s+(!?)(\w+)\}\}(.*?)\{\{/if\}\}", RegexOptions.Singleline | RegexOptions.Compiled);

    public static string Render(string template, Dictionary<string, string> tokens, HashSet<string>? flags = null)
    {
        flags ??= [];

        // Process {{#if Flag}} and {{#if !Flag}} blocks
        var result = IfBlock.Replace(template, m =>
        {
            var negate    = m.Groups[1].Value == "!";
            var flagName  = m.Groups[2].Value;
            var body      = m.Groups[3].Value;
            var flagSet   = flags.Contains(flagName);
            var condition = negate ? !flagSet : flagSet;
            return condition ? body : "";
        });

        // Replace {{Token}} variables
        foreach (var (key, value) in tokens)
            result = result.Replace("{{" + key + "}}", value);

        return result;
    }

    public static string ReadTemplate(string scaffoldingRoot, string templateFile)
    {
        var path = Path.Combine(scaffoldingRoot, templateFile);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Template not found: {path}");
        return File.ReadAllText(path);
    }
}
