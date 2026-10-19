using System.Text.Json;
using System.Text.RegularExpressions;

namespace Shipping.ArchitectureTests;

/// <summary>
/// US-EN-1 T6: the team walkthrough documents stay runnable and honest. Like the T5 checks these are static: they
/// compare the documents with the repository, check that every demonstration says what to expect and how to recover,
/// that no demonstration deletes data it does not own, and that no human attendance, readiness or acceptance is
/// recorded in advance. That the demonstrations run is shown by the rehearsal in docs/testing/US-EN-1-T6-red-green.md;
/// that the team understood them is shown only by the session itself.
/// </summary>
public partial class DocumentationTests
{
    private const string Walkthrough = "docs/onboarding/team-walkthrough.md";
    private const string Runbook = "docs/onboarding/demo-runbook.md";
    private const string Checklist = "docs/onboarding/team-readiness-checklist.md";
    private const string WalkthroughEvidence = "docs/testing/US-EN-1-T6-red-green.md";
    private static readonly string[] WalkthroughDocuments = [Walkthrough, Runbook, Checklist];

    /// <summary>The module the runbook generates, in a disposable clone, during the live demonstration.</summary>
    private const string DemoModule = "Walkthrough";

    /// <summary>The Compose project that demonstration may start in its clone, apart from the developer's "shipping".</summary>
    private const string DemoProject = "shipping-walkthrough";

    /// <summary>The Jira keys this repository already names. Any other key in the T6 documents would be invented.</summary>
    private static readonly string[] KnownKeys = ["SLS-15", "SLS-124", "SLS-125", "SLS-126", "SLOS-127"];

    [Fact]
    public void Walkthrough_guide_has_the_parts_a_facilitator_needs()
    {
        var headings = Headings(Read(Walkthrough), level: 2);
        string[] topics = ["objective", "prerequisite", "facilitator", "agenda", "question", "success", "handoff"];
        foreach (var topic in topics)
            Assert.True(headings.Any(heading => heading.Contains(topic, StringComparison.OrdinalIgnoreCase)),
                $"{Walkthrough} has no '## ' heading about '{topic}'; it has: {string.Join(" | ", headings)}");
    }

    [Fact]
    public void Walkthrough_agenda_is_contiguous_and_takes_60_to_90_minutes()
    {
        var agenda = Sections(Read(Walkthrough)).Single(section => section.Heading.Contains("agenda", StringComparison.OrdinalIgnoreCase));
        var slots = Tables(agenda.Text).SelectMany(table => table)
            .Select(row => Regex.Match(row[0], @"^(?<from>\d+)\s*[–-]\s*(?<to>\d+)\s*min"))
            .Where(match => match.Success)
            .Select(match => (From: int.Parse(match.Groups["from"].Value), To: int.Parse(match.Groups["to"].Value)))
            .ToList();
        Assert.True(slots.Count >= 5, $"the agenda has {slots.Count} timed rows ('0–10 min', ...)");
        Assert.Equal(0, slots[0].From);
        for (var i = 1; i < slots.Count; i++)
            Assert.True(slots[i].From == slots[i - 1].To, $"agenda gap or overlap between {slots[i - 1]} and {slots[i]}");
        Assert.All(slots, slot => Assert.True(slot.To > slot.From, $"empty agenda slot {slot}"));
        Assert.InRange(slots[^1].To, 60, 90);
    }

    [Fact]
    public void Walkthrough_guide_links_the_runbook_the_checklist_the_evidence_and_the_canonical_guides()
    {
        var source = Path.Combine(Root, Walkthrough);
        var targets = MarkdownLink().Matches(WithoutFencedCode(Read(Walkthrough)))
            .Select(link => link.Groups["target"].Value.Split('#')[0])
            .Where(target => target.Length > 0 && !target.Contains(':'))
            .Select(target => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, Uri.UnescapeDataString(target))))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] required =
        [
            Runbook, Checklist, WalkthroughEvidence, "README.md", "docs/architecture/ADR-001-four-project-modules.md",
            "docs/architecture/ADR-002-module-persistence.md", "docs/local-environment.md", "docs/persistence.md",
            "docs/development-workflow.md", "src/Modules/README.md"
        ];
        foreach (var document in required)
            Assert.True(targets.Contains(Path.GetFullPath(Path.Combine(Root, document))), $"{Walkthrough} does not link to {document}");
    }

    [Fact]
    public void Every_demonstration_in_the_guide_says_what_to_expect_why_it_matters_and_how_to_recover()
    {
        var sections = Sections(Read(Walkthrough));
        for (var demo = 1; demo <= 6; demo++)
            Assert.True(sections.Any(section => section.Text.Contains($"demo-runbook.md#demo-{demo}-", StringComparison.Ordinal)),
                $"{Walkthrough} never links to Demo {demo} of the runbook");
        foreach (var (heading, text) in sections.Where(section => Regex.IsMatch(section.Text, @"demo-runbook\.md#demo-[1-5]-")))
        {
            Assert.True(Regex.IsMatch(text, @"\b(expect|observe)", RegexOptions.IgnoreCase), $"'{heading}' does not say what to expect");
            Assert.True(Regex.IsMatch(text, @"\bwhy\b", RegexOptions.IgnoreCase), $"'{heading}' does not say why it matters");
            Assert.True(Regex.IsMatch(text, @"\b(recover|if it fails)", RegexOptions.IgnoreCase), $"'{heading}' does not say how to recover");
        }
    }

    [Fact]
    public void Runbook_has_six_demonstrations_each_with_commands_expected_results_and_recovery()
    {
        var sections = Sections(Read(Runbook));
        for (var demo = 1; demo <= 6; demo++)
        {
            var section = sections.SingleOrDefault(s => s.Heading.StartsWith($"Demo {demo} ", StringComparison.Ordinal));
            Assert.True(section.Text is not null, $"{Runbook} has no '## Demo {demo} ...' section; it has: {string.Join(" | ", sections.Select(s => s.Heading))}");
            if (demo == 6) continue; // the parallel-workflow demonstration is a discussion, with commands optional
            Assert.True(Commands(section.Text).Count > 0, $"Demo {demo} has no commands");
            Assert.True(Regex.IsMatch(section.Text, @"\bexpect", RegexOptions.IgnoreCase), $"Demo {demo} does not say what to expect");
            Assert.True(Regex.IsMatch(section.Text, @"\bwhy\b", RegexOptions.IgnoreCase), $"Demo {demo} does not say why it matters");
            Assert.True(Regex.IsMatch(section.Text, @"\brecover", RegexOptions.IgnoreCase), $"Demo {demo} does not say how to recover");
        }
    }

    [Fact]
    public void Runbook_demo_1_checks_docker_then_configures_validates_starts_and_checks_health_in_order()
    {
        var section = Demo(1);
        foreach (var language in new[] { "bash", "powershell" })
        {
            var commands = CodeLines(FencedBlocks(section).Where(block => block.Language == language));
            Assert.True(commands.Count > 0, $"Demo 1 has no {language} commands");
            AssertInOrder(commands, $"Demo 1 ({language})",
                "docker info", ".env.example .env", "docker compose config --quiet", "docker compose up --build -d --wait --wait-timeout ",
                "docker compose ps", "migrate", "/health");
            var health = commands.Where(line => line.Contains("localhost:8000/health", StringComparison.Ordinal)).ToArray();
            Assert.NotEmpty(health);
            Assert.All(health, line => Assert.Matches(@"--fail .*--retry \d+ .*--retry-all-errors", line));
        }
    }

    [Fact]
    public void Runbook_demo_2_inspects_the_schema_and_history_the_Sample_module_owns()
    {
        var section = Demo(2);
        var schema = Regex.Match(Read("src/Modules/Sample/Shipping.Modules.Sample.Infrastructure/Persistence/SamplePersistence.cs"),
            @"Schema = ""(?<schema>[a-z_]+)""").Groups["schema"].Value;
        Assert.Equal("sample", schema);
        Assert.Contains("SampleDbContext", section, StringComparison.Ordinal);
        Assert.Contains("src/Modules/Sample/Shipping.Modules.Sample.Infrastructure/Persistence/Migrations", section, StringComparison.Ordinal);
        var commands = Commands(section);
        Assert.Contains(commands, line => line.StartsWith("docker compose exec postgres psql ", StringComparison.Ordinal));
        Assert.Contains(commands, line => line.Contains("docker compose logs migrate", StringComparison.Ordinal));
        // The history table's name has capitals, so psql needs it quoted.
        Assert.Contains($"{schema}.\"__EFMigrationsHistory\"", section, StringComparison.Ordinal);
    }

    [Fact]
    public void Runbook_demo_3_runs_the_CI_commands_and_each_test_project()
    {
        var commands = Commands(Demo(3));
        foreach (var command in new[] { "dotnet restore Shipping.slnx", "dotnet build Shipping.slnx -c Release --no-restore", "dotnet test Shipping.slnx -c Release --no-build" })
            Assert.True(commands.Contains(command), $"Demo 3 does not run '{command}', the command CI runs");
        foreach (var project in Directory.GetDirectories(Path.Combine(Root, "tests"), "Shipping.*Tests").Select(Path.GetFileName))
            Assert.Contains(commands, line => line.StartsWith($"dotnet test tests/{project} ", StringComparison.Ordinal));
    }

    [Fact]
    public void Runbook_demo_4_creates_a_module_in_a_disposable_clone_and_never_in_the_working_checkout()
    {
        var section = Demo(4);
        var shortName = JsonDocument.Parse(Read("templates/Module/.template.config/template.json")).RootElement.GetProperty("shortName").GetString();
        var api = $"src/Modules/{DemoModule}/Shipping.Modules.{DemoModule}.Api/Shipping.Modules.{DemoModule}.Api.csproj";
        foreach (var language in new[] { "bash", "powershell" })
        {
            var commands = CodeLines(FencedBlocks(section).Where(block => block.Language == language));
            AssertInOrder(commands, $"Demo 4 ({language})",
                "git clone ", "DOTNET_CLI_HOME", "dotnet new install ./templates/Module",
                $"dotnet new {shortName} --name {DemoModule} --output src/Modules/{DemoModule}",
                $"dotnet sln Shipping.slnx add {api}", $"dotnet add src/Shipping.Platform.Api/Shipping.Platform.Api.csproj reference {api}",
                "dotnet build Shipping.slnx", "ConnectionStrings__PostgreSql",
                $"--context {DemoModule}DbContext --output-dir Persistence/Migrations", "dotnet test Shipping.slnx");
            // The template is installed from, and the module generated in, the clone: never "git clone ... ." into the checkout.
            Assert.DoesNotContain(commands, line => Regex.IsMatch(line, @"^git clone \S+\s+\.$"));
        }
        foreach (var call in new[] { "builder.Services.AddSampleModule(databaseConnection);", "app.MapSampleModule();" })
            Assert.Contains(call.Replace("Sample", DemoModule, StringComparison.Ordinal), section, StringComparison.Ordinal);
    }

    [Fact]
    public void Runbook_demo_5_stops_and_restarts_only_postgres_and_explains_503_and_traefiks_404()
    {
        var section = Demo(5);
        var commands = Commands(section);
        AssertInOrder(commands, "Demo 5", "docker compose stop postgres", "/health", "docker compose start postgres", "/health");
        foreach (var word in new[] { "503", "404", "Server: Kestrel" })
            Assert.Contains(word, section, StringComparison.Ordinal);
    }

    [Fact]
    public void Walkthrough_documents_run_no_command_that_deletes_what_they_did_not_create()
    {
        // Only the Compose project the module demonstration creates for itself may lose its volume.
        string[] forbidden =
        [
            $@"^(?!docker compose -p {DemoProject} down\s).*\bdown\b.*(\s-v\b|--volumes|--rmi\s+all)", @"\bprune\b",
            @"\b(volume|image|container)\s+rm\b", @"\bdocker\s+(rm|rmi|kill|stop)\b", @"\bdocker\s+compose\b.*\s(rm|kill)\b",
            @"\bgit\s+push\b.*(--force|-f\b)", @"\bgit\s+reset\s+--hard\b"
        ];
        foreach (var document in WalkthroughDocuments)
        {
            foreach (var line in Commands(Read(document)))
            {
                var match = forbidden.FirstOrDefault(pattern => Regex.IsMatch(line, pattern));
                Assert.True(match is null, $"{document} runs '{line}'");
            }
        }
    }

    [Fact]
    public void Walkthrough_documents_use_curl_exe_in_powershell()
    {
        foreach (var document in WalkthroughDocuments)
        {
            var powershell = CodeLines(FencedBlocks(Read(document)).Where(block => block.Language == "powershell"));
            Assert.DoesNotContain(powershell, line => Regex.IsMatch(line, @"(^|[\s;(|])curl\s"));
        }
    }

    [Fact]
    public void Readiness_checklist_separates_agent_verification_attendance_readiness_acceptance_and_blockers()
    {
        var headings = Headings(Read(Checklist), level: 2);
        foreach (var topic in new[] { "agent", "attendance", "readiness", "stream lead", "blocker" })
            Assert.True(headings.Any(heading => heading.Contains(topic, StringComparison.OrdinalIgnoreCase)),
                $"{Checklist} has no '## ' heading about '{topic}'; it has: {string.Join(" | ", headings)}");
        var checklist = Read(Checklist);
        foreach (var status in new[] { "Pass", "Fail", "Blocked" })
            Assert.Matches($@"\b{status}\b", checklist);
    }

    [Fact]
    public void Readiness_checklist_leaves_every_human_slot_empty()
    {
        var checklist = Read(Checklist);
        Assert.DoesNotMatch(@"\[[xX]\]", checklist);
        Assert.DoesNotMatch(@"\b20\d\d-\d\d-\d\d\b", checklist);
        var slot = new Regex(@"^(Developer [1-5]|Stream Lead|Facilitator)$");
        var sections = Sections(checklist);
        foreach (var topic in new[] { "attendance", "readiness" })
        {
            var text = string.Join('\n', sections.Where(s => s.Heading.Contains(topic, StringComparison.OrdinalIgnoreCase)).Select(s => s.Text));
            for (var developer = 1; developer <= 5; developer++)
                Assert.Contains($"Developer {developer}", text, StringComparison.Ordinal);
        }
        var filled = new List<string>();
        foreach (var table in Tables(checklist))
        {
            var header = table[0];
            var slotColumns = Enumerable.Range(0, header.Length).Where(i => slot.IsMatch(header[i])).ToArray();
            foreach (var row in table.Skip(1))
            {
                filled.AddRange(slotColumns.Where(i => i < row.Length && row[i].Length > 0).Select(i => $"{header[i]} / {row[0]}: '{row[i]}'"));
                if (slot.IsMatch(row[0]))
                    filled.AddRange(row.Skip(1).Where(cell => cell.Length > 0).Select(cell => $"{row[0]}: '{cell}'"));
            }
        }
        // Owners, blockers and open questions are written in the session, never in advance: only their first column is given.
        foreach (var (heading, text) in sections.Where(s => Regex.IsMatch(s.Heading, "ownership|blocker", RegexOptions.IgnoreCase)))
            foreach (var row in Tables(text).SelectMany(table => table.Skip(1)))
                filled.AddRange(row.Skip(1).Where(cell => cell.Length > 0).Select(cell => $"{heading} / {row[0]}: '{cell}'"));
        Assert.True(filled.Count == 0, "Human slots that are already filled in:" + Environment.NewLine + string.Join(Environment.NewLine, filled));
    }

    [Fact]
    public void Walkthrough_documents_claim_no_human_outcome_and_no_capability_that_is_not_built()
    {
        string[] claims =
        [
            @"\b(walkthrough|session|demonstration)s? (was|were|has been|have been) (held|delivered|conducted|completed|given)",
            @"\b(all )?(five|5) developers (have )?(completed|attended|passed|signed)",
            @"\bstream lead (has )?(approved|accepted|attended|signed)",
            @"TEAM_WALKTHROUGH_COMPLETED",
            @"(?<!\bno )\b(kafka|rabbitmq|message broker|wolverine|cqrs) (is|are) (configured|installed|running|enabled|available|implemented)",
            @"(?<!\bno )\b(authentication|authorization|openapi|swagger)( ui)? (is|are) (configured|enabled|implemented|available)"
        ];
        foreach (var document in WalkthroughDocuments)
        {
            var text = Regex.Replace(Read(document), @"\s+", " ");
            foreach (var claim in claims)
            {
                var match = Regex.Match(text, claim, RegexOptions.IgnoreCase);
                Assert.False(match.Success, $"{document} claims '{match.Value}'");
            }
            Assert.DoesNotContain(Commands(Read(document)), line => Regex.IsMatch(line, @"/(openapi|swagger)\b|Authorization: Bearer", RegexOptions.IgnoreCase));
        }
    }

    [Fact]
    public void Walkthrough_documents_and_evidence_name_no_invented_Jira_key()
    {
        // US-EN-1, NFR-ARCH-1, NFR-OPS-1 and ADR numbers are not Jira subtask keys; UTF-8 is an encoding.
        var ignored = new Regex(@"^(EN-1|ARCH-1|OPS-1|ADR-\d+|UTF-8)$");
        foreach (var document in WalkthroughDocuments.Append(WalkthroughEvidence))
        {
            var keys = Regex.Matches(Read(document), @"\b[A-Z][A-Z0-9]+-\d+\b").Select(match => match.Value)
                .Where(key => !ignored.IsMatch(key) && !KnownKeys.Contains(key)).Distinct().ToArray();
            Assert.True(keys.Length == 0, $"{document} names keys this repository does not know: {string.Join(", ", keys)}");
        }
    }

    private static string Demo(int number) =>
        Sections(Read(Runbook)).Single(section => section.Heading.StartsWith($"Demo {number} ", StringComparison.Ordinal)).Text;

    private static void AssertInOrder(List<string> commands, string context, params string[] steps)
    {
        var position = -1;
        foreach (var step in steps)
        {
            var next = commands.FindIndex(position + 1, line => line.Contains(step, StringComparison.Ordinal));
            Assert.True(next >= 0, $"{context} has no command containing '{step}' after line {position} of its code:{Environment.NewLine}{string.Join(Environment.NewLine, commands)}");
            position = next;
        }
    }

    /// <summary>The commands of a document: every fenced block except ```text, which shows output, not something to run.</summary>
    private static List<string> Commands(string markdown) =>
        CodeLines(FencedBlocks(markdown).Where(block => block.Language != "text"));

    private static List<string> CodeLines(IEnumerable<FencedBlock> blocks) =>
        blocks.SelectMany(block => block.Lines)
            .Select(line => Regex.Replace(line, @"\s+#\s.*$", "").Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();

    /// <summary>Each heading of the given level with its text, up to the next heading of that level or higher.</summary>
    private static List<(string Heading, string Text)> Sections(string markdown, int level = 2)
    {
        var masked = WithoutFencedCode(markdown, keepLines: true).Split('\n');
        var raw = markdown.Split('\n');
        var marker = new string('#', level) + " ";
        var sections = new List<(string, string)>();
        for (var start = 0; start < masked.Length; start++)
        {
            if (!masked[start].StartsWith(marker, StringComparison.Ordinal)) continue;
            var end = Array.FindIndex(masked, start + 1, line => Regex.IsMatch(line, $"^#{{1,{level}}} "));
            sections.Add((masked[start][marker.Length..].Trim(), string.Join('\n', raw[start..(end < 0 ? raw.Length : end)])));
        }
        return sections;
    }

    /// <summary>Every pipe table outside code: the header row first, the separator row left out, cells trimmed.</summary>
    private static List<List<string[]>> Tables(string markdown)
    {
        var tables = new List<List<string[]>>();
        List<string[]>? current = null;
        foreach (var line in WithoutFencedCode(markdown, keepLines: true).Split('\n'))
        {
            var row = line.Trim();
            if (!row.StartsWith('|')) { current = null; continue; }
            if (current is null) { current = []; tables.Add(current); }
            row = row[1..];
            if (row.EndsWith('|')) row = row[..^1];
            var cells = row.Split('|').Select(cell => cell.Trim()).ToArray();
            if (cells.All(cell => Regex.IsMatch(cell, @"^:?-{3,}:?$"))) continue;
            current.Add(cells);
        }
        return tables;
    }
}
