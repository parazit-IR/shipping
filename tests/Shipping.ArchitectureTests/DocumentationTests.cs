using System.Text.Json;
using System.Text.RegularExpressions;

namespace Shipping.ArchitectureTests;

/// <summary>
/// US-EN-1 T5: the new-developer README stays true to the repository it describes. These checks are static: they
/// compare the documents with the files, CI commands, Compose configuration and module template they name. That the
/// documented environment starts and answers GET /health is tested at run time by tests/compose/compose-smoke.sh.
/// </summary>
public partial class DocumentationTests
{
    private static readonly string Root = FindRoot();
    private static readonly string[] OnboardingDocuments = ["README.md", "docs/getting-started.md", "docs/local-environment.md"];

    /// <summary>The module name the README uses in its "Create a module" example. A developer may follow it literally.</summary>
    private const string ExampleModule = "Booking";

    [Fact]
    public void Readme_has_the_onboarding_sections_with_the_quickstart_before_the_dotnet_workflow()
    {
        var headings = Headings(Read("README.md"), level: 2);
        string[] required = ["Prerequisites", "Quickstart", "Build and test", "Architecture", "Create a module", "Health endpoint", "Troubleshooting", "Documentation"];
        foreach (var heading in required)
            Assert.True(headings.Contains(heading), $"README has no '## {heading}' section; it has: {string.Join(", ", headings)}");
        Assert.True(headings.IndexOf("Quickstart") < headings.IndexOf("Build and test"), "the Docker quickstart must come before the .NET workflow");
    }

    [Fact]
    public void Readme_quickstart_runs_clone_configure_validate_start_check_and_stop_in_order()
    {
        var commands = CodeLines(Section(Read("README.md"), "Quickstart"));
        string[] steps =
        [
            "git clone https://github.com/parazit-IR/shipping.git",
            "cp .env.example .env",
            "docker compose config --quiet",
            "docker compose up --build -d --wait",
            "docker compose ps",
            "/health",
            "docker compose down"
        ];
        var position = -1;
        foreach (var step in steps)
        {
            var next = commands.FindIndex(position + 1, line => line.Contains(step, StringComparison.Ordinal));
            Assert.True(next >= 0, $"Quickstart has no command containing '{step}' after line {position} of its code:{Environment.NewLine}{string.Join(Environment.NewLine, commands)}");
            position = next;
        }
    }

    [Fact]
    public void Readme_quickstart_needs_no_dotnet_sdk()
    {
        var dotnet = CodeLines(Section(Read("README.md"), "Quickstart")).Where(line => Regex.IsMatch(line, @"(^|\s)dotnet\s")).ToArray();
        Assert.True(dotnet.Length == 0, $"the Docker-only quickstart runs dotnet: {string.Join(" | ", dotnet)}");
    }

    [Fact]
    public void Readme_quickstart_waits_and_retries_for_a_bounded_time_against_the_port_and_host_compose_publishes()
    {
        var compose = Read("compose.yaml");
        Assert.Contains("\"127.0.0.1:${SHIPPING_HTTP_PORT:-8000}:80\"", compose, StringComparison.Ordinal);
        Assert.Contains("Host(`${SHIPPING_HOST:-localhost}`)", compose, StringComparison.Ordinal);

        var commands = CodeLines(Section(Read("README.md"), "Quickstart"));
        var up = commands.Where(line => line.StartsWith("docker compose up ", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(up);
        Assert.All(up, line => Assert.Matches(@"--wait --wait-timeout \d+", line));
        var health = commands.Where(line => line.Contains("/health", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(health);
        foreach (var line in health)
        {
            Assert.Contains("http://localhost:8000/health", line, StringComparison.Ordinal);
            // Traefik's transient 404 is retried only when curl treats it as an error (--fail) and retries all errors.
            Assert.Matches(@"--retry \d+", line);
            Assert.Contains("--fail", line, StringComparison.Ordinal);
            Assert.Contains("--retry-all-errors", line, StringComparison.Ordinal);
        }
        Assert.DoesNotContain(commands, line => Regex.IsMatch(line, @"while\s+(true|:)|until\s"));
    }

    [Fact]
    public void Readme_quickstart_stops_the_environment_without_deleting_the_database_volume()
    {
        var commands = CodeLines(Section(Read("README.md"), "Quickstart"));
        Assert.Contains("docker compose down", commands);
        Assert.DoesNotContain(commands, line => Regex.IsMatch(line, @"down\s.*(-v\b|--volumes)"));
    }

    [Fact]
    public void Readme_build_and_test_commands_are_the_ones_CI_runs()
    {
        var readme = CodeLines(Section(Read("README.md"), "Build and test"));
        var ci = Regex.Matches(Read(".github/workflows/ci.yml"), @"^\s*run:\s*(?<command>.+)$", RegexOptions.Multiline)
            .Select(match => match.Groups["command"].Value.Trim()).ToArray();
        string[] expected =
        [
            "dotnet restore Shipping.slnx",
            "dotnet build Shipping.slnx -c Release --no-restore",
            "dotnet test Shipping.slnx -c Release --no-build",
            "bash tests/compose/compose-smoke.sh"
        ];
        foreach (var command in expected)
        {
            Assert.True(readme.Contains(command), $"README 'Build and test' does not show '{command}'");
            Assert.True(ci.Any(run => run == command || run.StartsWith(command + " ", StringComparison.Ordinal)),
                $"CI does not run '{command}'; it runs: {string.Join(" | ", ci)}");
        }
    }

    [Fact]
    public void Readme_module_instructions_match_the_template_and_the_host()
    {
        var section = Section(Read("README.md"), "Create a module");
        var commands = CodeLines(section);
        var shortName = JsonDocument.Parse(Read("templates/Module/.template.config/template.json")).RootElement.GetProperty("shortName").GetString();
        Assert.True(Directory.Exists(Path.Combine(Root, "templates/Module")));
        Assert.Contains("dotnet new install ./templates/Module", commands);
        Assert.Contains($"dotnet new {shortName} --name {ExampleModule} --output src/Modules/{ExampleModule}", commands);
        Assert.Contains(commands, line => line.StartsWith("dotnet sln Shipping.slnx add ", StringComparison.Ordinal));
        Assert.Contains(commands, line => line.Contains($"reference src/Modules/{ExampleModule}/Shipping.Modules.{ExampleModule}.Api/Shipping.Modules.{ExampleModule}.Api.csproj", StringComparison.Ordinal));
        foreach (var layer in new[] { "Domain", "Application", "Infrastructure", "Api" })
            Assert.Contains($"Shipping.Modules.{ExampleModule}.{layer}", section, StringComparison.Ordinal);

        // The registration lines are the ones the host already has for Sample, renamed.
        var program = Read("src/Shipping.Platform.Api/Program.cs");
        var registration = Read("templates/Module/Shipping.Modules.Sample.Api/ModuleRegistration.cs");
        foreach (var call in new[] { "builder.Services.AddSampleModule(databaseConnection);", "app.MapSampleModule();" })
        {
            Assert.Contains(call, program, StringComparison.Ordinal);
            Assert.Contains(call.Replace("Sample", ExampleModule, StringComparison.Ordinal), section, StringComparison.Ordinal);
        }
        Assert.Contains("AddSampleModule(", registration, StringComparison.Ordinal);
        Assert.Contains("MapSampleModule(", registration, StringComparison.Ordinal);

        // The migration command names the generated context and the folder Sample's migrations live in.
        Assert.True(File.Exists(Path.Combine(Root, "templates/Module/Shipping.Modules.Sample.Infrastructure/Persistence/SampleDbContext.cs")));
        Assert.True(Directory.Exists(Path.Combine(Root, "src/Modules/Sample/Shipping.Modules.Sample.Infrastructure/Persistence/Migrations")));
        Assert.Contains(commands, line => line.StartsWith("dotnet tool restore", StringComparison.Ordinal));
        var migration = commands.FindIndex(line => line.Contains("dotnet ef migrations add", StringComparison.Ordinal)
            && line.Contains($"--context {ExampleModule}DbContext", StringComparison.Ordinal)
            && line.Contains("--output-dir Persistence/Migrations", StringComparison.Ordinal));
        Assert.True(migration >= 0, "no 'dotnet ef migrations add' for the example module's context");
        // dotnet ef fails with NETSDK1004 on projects that were never restored, so a build comes first.
        var build = commands.FindIndex(line => line.StartsWith("dotnet build ", StringComparison.Ordinal));
        Assert.True(build >= 0 && build < migration, "the module's projects must be built before 'dotnet ef migrations add'");
        var variable = commands.FindIndex(line => line.Contains("ConnectionStrings__PostgreSql", StringComparison.Ordinal));
        Assert.True(variable >= 0 && variable < migration, "the design-time factory needs ConnectionStrings__PostgreSql before 'dotnet ef'");
    }

    [Fact]
    public void Readme_windows_alternatives_use_powershell_commands_and_curl_exe()
    {
        var readme = Read("README.md");
        var powershell = FencedBlocks(readme).Where(block => block.Language == "powershell").SelectMany(block => block.Lines).ToArray();
        Assert.Contains("Copy-Item .env.example .env", powershell);
        Assert.Contains(powershell, line => line.Contains("curl.exe ", StringComparison.Ordinal));
        // In Windows PowerShell 5.1 "curl" is an alias of Invoke-WebRequest, which takes none of curl's options.
        Assert.DoesNotContain(powershell, line => Regex.IsMatch(line, @"(^|[\s;(|])curl\s"));
    }

    [Fact]
    public void Onboarding_keeps_the_database_password_out_of_the_repository()
    {
        var example = Read(".env.example");
        Assert.Matches(new Regex("^POSTGRES_PASSWORD=$", RegexOptions.Multiline), example);
        var ignored = Read(".gitignore").Split('\n').Select(line => line.Trim()).ToArray();
        Assert.Contains(".env", ignored);
        Assert.Contains("!.env.example", ignored);
        // A value is literal when it starts with a letter or digit. Empty, <placeholder>, $(generated), ${variable}
        // and the .* of a replace pattern are not.
        foreach (var document in OnboardingDocuments)
        {
            foreach (Match match in Regex.Matches(Read(document), @"(POSTGRES_PASSWORD|Password)=(?<value>[^;\s""'`]*)"))
                Assert.False(Regex.IsMatch(match.Groups["value"].Value, "^[A-Za-z0-9]"), $"{document} contains a literal password: {match.Value}");
        }
    }

    [Fact]
    public void Onboarding_documents_do_not_describe_delivered_work_as_missing()
    {
        string[] obsolete = [@"no health endpoint", @"may add[^.]*health probes", @"new-developer README:\s*SLS-15 T5"];
        foreach (var document in OnboardingDocuments)
        {
            var text = Regex.Replace(Read(document), @"\s+", " ");
            foreach (var pattern in obsolete)
                Assert.False(Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase), $"{document} still says '{Regex.Match(text, pattern, RegexOptions.IgnoreCase).Value}'");
        }
    }

    [Fact]
    public void Paths_named_in_the_onboarding_documents_exist()
    {
        var missing = new List<string>();
        foreach (var document in OnboardingDocuments)
        {
            var directory = Path.GetDirectoryName(Path.Combine(Root, document))!;
            foreach (var path in InlineCode(Read(document)).Where(LooksLikeRepositoryPath).Distinct())
            {
                // README paths are relative to the repository root; other documents name root paths too.
                if (!Exists(Path.Combine(Root, path)) && !Exists(Path.Combine(directory, path)))
                    missing.Add($"{document}: `{path}`");
            }
        }
        Assert.True(missing.Count == 0, "Paths that do not exist:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void Relative_markdown_links_resolve_to_files_and_headings()
    {
        var documents = new[] { "README.md", "src/Modules/README.md" }
            .Concat(Directory.GetFiles(Path.Combine(Root, "docs"), "*.md", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(Root, path)))
            .Select(path => path.Replace('\\', '/'));
        var broken = new List<string>();
        foreach (var document in documents)
        {
            var source = Path.Combine(Root, document);
            foreach (Match link in MarkdownLink().Matches(WithoutFencedCode(File.ReadAllText(source))))
            {
                var target = link.Groups["target"].Value;
                if (Regex.IsMatch(target, @"^[a-z][a-z0-9+.-]*:", RegexOptions.IgnoreCase)) continue; // https:, mailto:
                var hash = target.IndexOf('#');
                var file = hash < 0 ? target : target[..hash];
                var anchor = hash < 0 ? null : target[(hash + 1)..];
                var resolved = file.Length == 0 ? source : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, Uri.UnescapeDataString(file)));
                if (!Exists(resolved)) { broken.Add($"{document}: {target} (no such file)"); continue; }
                if (anchor is not null && File.Exists(resolved) && resolved.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                    && !Anchors(File.ReadAllText(resolved)).Contains(anchor))
                    broken.Add($"{document}: {target} (no such heading)");
            }
        }
        Assert.True(broken.Count == 0, "Broken links:" + Environment.NewLine + string.Join(Environment.NewLine, broken));
    }

    /// <summary>Git-ignored files a developer creates; the documents name them, the repository never has them.</summary>
    private static readonly string[] DeveloperFiles = [".env", "compose.override.yml"];

    /// <summary>
    /// A path into the repository: its first segment is a top-level directory, or it is a root file name with a
    /// repository file extension. So `text/plain` or `Shipping.Platform.Api` are not taken for paths.
    /// </summary>
    private static bool LooksLikeRepositoryPath(string code)
    {
        if (!Regex.IsMatch(code, @"^[A-Za-z0-9_.\-]+(/[A-Za-z0-9_.\-]+)*/?$") || DeveloperFiles.Contains(code)
            || code.Contains(ExampleModule, StringComparison.Ordinal))
            return false;
        var first = code.Split('/')[0];
        return code.Contains('/')
            ? Directory.Exists(Path.Combine(Root, first))
            : Regex.IsMatch(code, @"\.(md|ya?ml|json|slnx|csproj|sh|props|example)$") || code == "Dockerfile";
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static string Read(string relative) => File.ReadAllText(Path.Combine(Root, relative)).Replace("\r\n", "\n");

    /// <summary>The text under a heading of the given level, up to the next heading of that level or higher.</summary>
    private static string Section(string markdown, string heading, int level = 2)
    {
        var lines = WithoutFencedCode(markdown, keepLines: true).Split('\n');
        var marker = new string('#', level) + " " + heading;
        var start = Array.FindIndex(lines, line => line.TrimEnd() == marker);
        Assert.True(start >= 0, $"no '{marker}' heading");
        var end = Array.FindIndex(lines, start + 1, line => Regex.IsMatch(line, $"^#{{1,{level}}} "));
        return string.Join('\n', markdown.Split('\n')[start..(end < 0 ? lines.Length : end)]);
    }

    private static List<string> Headings(string markdown, int level) =>
        WithoutFencedCode(markdown).Split('\n')
            .Where(line => line.StartsWith(new string('#', level) + " ", StringComparison.Ordinal))
            .Select(line => line[(level + 1)..].Trim()).ToList();

    private sealed record FencedBlock(string Language, string[] Lines);

    private static List<FencedBlock> FencedBlocks(string markdown)
    {
        var blocks = new List<FencedBlock>();
        string? language = null;
        var lines = new List<string>();
        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var fence = Regex.Match(line, @"^\s*```(?<language>\S*)");
            if (fence.Success && language is null) { language = fence.Groups["language"].Value; lines = []; continue; }
            if (fence.Success) { blocks.Add(new(language!, lines.ToArray())); language = null; continue; }
            if (language is not null) lines.Add(line);
        }
        return blocks;
    }

    /// <summary>The commands of every fenced block: trimmed, without comments, prompts or blank lines.</summary>
    private static List<string> CodeLines(string markdown) =>
        FencedBlocks(markdown).SelectMany(block => block.Lines)
            .Select(line => Regex.Replace(line, @"\s+#\s.*$", "").Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();

    private static IEnumerable<string> InlineCode(string markdown) =>
        Regex.Matches(WithoutFencedCode(markdown), "`(?<code>[^`\n]+)`").Select(match => match.Groups["code"].Value.Trim());

    private static string WithoutFencedCode(string markdown, bool keepLines = false)
    {
        var inFence = false;
        return string.Join('\n', markdown.Split('\n').Select(line =>
        {
            if (Regex.IsMatch(line, @"^\s*```")) { inFence = !inFence; return keepLines ? "" : null; }
            return inFence ? (keepLines ? "" : null) : line;
        }).Where(line => line is not null));
    }

    /// <summary>GitHub's heading anchors: lower case, punctuation removed, spaces as hyphens, repeats numbered.</summary>
    private static HashSet<string> Anchors(string markdown)
    {
        var anchors = new HashSet<string>(StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in WithoutFencedCode(markdown.Replace("\r\n", "\n")).Split('\n'))
        {
            var heading = Regex.Match(line, @"^#{1,6}\s+(?<text>.+?)\s*#*\s*$");
            if (!heading.Success) continue;
            var text = Regex.Replace(heading.Groups["text"].Value, @"`|\*|\[([^\]]*)\]\([^)]*\)", "$1");
            var slug = Regex.Replace(text.ToLowerInvariant(), @"[^\p{L}\p{Nd}\s_-]", "").Replace(' ', '-');
            var count = counts.GetValueOrDefault(slug);
            anchors.Add(count == 0 ? slug : $"{slug}-{count}");
            counts[slug] = count + 1;
        }
        return anchors;
    }

    [GeneratedRegex(@"(?<!!)\[[^\]\n]*\]\((?<target>[^)\s]+)(\s+""[^""]*"")?\)")]
    private static partial Regex MarkdownLink();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Shipping.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root");
    }
}
