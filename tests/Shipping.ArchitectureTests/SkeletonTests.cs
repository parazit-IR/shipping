using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using NetArchTest.Rules;

namespace Shipping.ArchitectureTests;

public class SkeletonTests
{
    private static readonly string Root = FindRoot();
    private static readonly string[] Layers = ["Domain", "Application", "Infrastructure", "Api"];
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(3);
    private static readonly string[] ApplicationPackages =
    [
        "MPCore.Application", "MPCore.Domain", "MPCore.Persistence.Abstractions",
        "MPCore.Messaging.Abstractions", "MPCore.Security.Abstractions", "MPCore.Tenancy.Abstractions"
    ];

    [Fact]
    public void Solution_contains_one_host_and_four_sample_projects()
    {
        var solution = Path.Combine(Root, "Shipping.slnx");
        Assert.True(File.Exists(solution), "Shipping.slnx is missing");
        var projects = XDocument.Load(solution).Descendants("Project")
            .Select(x => (string?)x.Attribute("Path")).Where(x => x is not null).ToArray();
        Assert.Contains(projects, p => p!.EndsWith("Shipping.Platform.Api.csproj", StringComparison.Ordinal));
        foreach (var layer in Layers)
            Assert.Contains(projects, p => p!.EndsWith($"Shipping.Modules.Sample.{layer}.csproj", StringComparison.Ordinal));
        Assert.Single(Directory.GetFiles(Path.Combine(Root, "src"), "*.csproj", SearchOption.AllDirectories),
            p => XDocument.Load(p).Root?.Attribute("Sdk")?.Value == "Microsoft.NET.Sdk.Web");
        Assert.True(File.Exists(Path.Combine(Root, "templates/Module/.template.config/template.json")));
    }

    [Fact]
    public void Compiled_sample_layers_obey_dependency_direction()
    {
        var domain = typeof(Shipping.Modules.Sample.Domain.AssemblyReference).Assembly;
        var application = typeof(Shipping.Modules.Sample.Application.AssemblyReference).Assembly;
        var infrastructure = typeof(Shipping.Modules.Sample.Infrastructure.ModuleRegistration).Assembly;
        var api = typeof(Shipping.Modules.Sample.Api.ModuleRegistration).Assembly;
        Assert.True(Types.InAssembly(domain).ShouldNot().HaveDependencyOnAny(
            "Shipping.Modules.Sample.Application", "Shipping.Modules.Sample.Infrastructure", "Shipping.Modules.Sample.Api").GetResult().IsSuccessful);
        Assert.True(Types.InAssembly(application).ShouldNot().HaveDependencyOnAny(
            "Shipping.Modules.Sample.Infrastructure", "Shipping.Modules.Sample.Api").GetResult().IsSuccessful);
        Assert.True(Types.InAssembly(infrastructure).ShouldNot().HaveDependencyOn("Shipping.Platform.Api").GetResult().IsSuccessful);
        Assert.True(Types.InAssembly(api).ShouldNot().HaveDependencyOn("Shipping.Platform.Api").GetResult().IsSuccessful);
    }

    [Fact]
    public void Compiled_host_calls_sample_service_and_endpoint_registration()
    {
        var host = typeof(Program).Assembly;
        var calls = host.GetTypes().SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            .SelectMany(method => CalledMethods(method))
            .ToArray();
        Assert.Contains(calls, method => method.DeclaringType == typeof(Shipping.Modules.Sample.Api.ModuleRegistration)
            && method.Name == "AddSampleModule");
        Assert.Contains(calls, method => method.DeclaringType == typeof(Shipping.Modules.Sample.Api.ModuleRegistration)
            && method.Name == "MapSampleModule");
    }

    private static IEnumerable<MethodBase> CalledMethods(MethodInfo method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        for (var offset = 0; offset + 4 < il.Length; offset++)
        {
            if (il[offset] != 0x28) continue; // call
            MethodBase? called = null;
            try
            {
                called = method.Module.ResolveMethod(BitConverter.ToInt32(il, offset + 1));
            }
            catch (ArgumentException) { /* A byte in another operand can equal the call opcode. */ }
            if (called is not null) yield return called;
        }
    }

    [Fact]
    public async Task All_module_project_dependencies_obey_boundaries()
    {
        var errors = await CheckModuleDependenciesAsync(Root);
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public async Task Evaluated_references_reject_adversarial_edges()
    {
        var copy = Path.Combine(Path.GetTempPath(), "shipping-rule-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(copy);
        try
        {
            CopyDirectory(Path.Combine(Root, "src", "Modules", "Sample"), Path.Combine(copy, "src", "Modules", "Sample"));
            File.Copy(Path.Combine(Root, "Directory.Build.props"), Path.Combine(copy, "Directory.Build.props"));
            var domain = Path.Combine(copy, "src/Modules/Sample/Shipping.Modules.Sample.Domain/Shipping.Modules.Sample.Domain.csproj");
            var document = XDocument.Load(domain);
            document.Root!.Add(new XElement("ItemGroup",
                new XElement("FrameworkReference", new XAttribute("Include", "Microsoft.AspNetCore.App")),
                new XElement("PackageReference", new XAttribute("Include", "Npgsql"), new XAttribute("Version", "1.0.0"))));
            document.Save(domain);
            var errors = await CheckModuleDependenciesAsync(copy);
            Assert.Contains(errors, e => e.Contains("Sample.Domain", StringComparison.Ordinal) && e.Contains("Microsoft.AspNetCore.App", StringComparison.Ordinal));
            Assert.Contains(errors, e => e.Contains("Sample.Domain", StringComparison.Ordinal) && e.Contains("Npgsql", StringComparison.Ordinal));

            var other = Path.Combine(copy, "src/Modules/Other/Shipping.Modules.Other.Api");
            Directory.CreateDirectory(other);
            var otherProject = Path.Combine(other, "Shipping.Modules.Other.Api.csproj");
            File.WriteAllText(otherProject, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            var infrastructure = Path.Combine(copy, "src/Modules/Sample/Shipping.Modules.Sample.Infrastructure/Shipping.Modules.Sample.Infrastructure.csproj");
            document = XDocument.Load(infrastructure);
            document.Root!.Add(new XElement("ItemGroup", new XElement("ProjectReference", new XAttribute("Include", Path.GetRelativePath(Path.GetDirectoryName(infrastructure)!, otherProject)))));
            document.Save(infrastructure);
            errors = await CheckModuleDependenciesAsync(copy);
            Assert.Contains(errors, e => e.Contains("Sample.Infrastructure", StringComparison.Ordinal) && e.Contains("Other.Api", StringComparison.Ordinal));

            document.Root!.Add(new XElement("ItemGroup", new XElement("ProjectReference", new XAttribute("Include", "../Shipping.Modules.Sample.Api/Shipping.Modules.Sample.Api.csproj"))));
            document.Save(infrastructure);
            errors = await CheckModuleDependenciesAsync(copy);
            Assert.Contains(errors, e => e.Contains("Project dependency cycle", StringComparison.Ordinal));
        }
        finally { Directory.Delete(copy, true); }
    }

    private static async Task<List<string>> CheckModuleDependenciesAsync(string root)
    {
        var moduleRoot = Path.Combine(root, "src", "Modules");
        var paths = Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(Path.GetFullPath).ToArray();
        var projects = Directory.GetFiles(moduleRoot, "*.csproj", SearchOption.AllDirectories)
            .ToDictionary(Path.GetFullPath, ProjectIdentity.FromPath, StringComparer.OrdinalIgnoreCase);
        var graph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        foreach (var path in paths)
        {
            var items = await EvaluateItemsAsync(path);
            graph[path] = [];
            var isModule = projects.TryGetValue(path, out var project);
            foreach (var reference in items.ProjectReferences)
            {
                var target = Path.GetFullPath(reference);
                graph[path].Add(target);
                if (!File.Exists(target)) { errors.Add($"{path}: missing project {target}"); continue; }
                if (!isModule) continue;
                if (!projects.TryGetValue(target, out var dependency))
                {
                    errors.Add($"{project}: module references external project {target}");
                    continue;
                }
                if (project!.Module != dependency.Module)
                    errors.Add($"{project}: cross-module reference to {dependency}");
                else if (!AllowedLayerEdge(project.Layer, dependency.Layer))
                    errors.Add($"{project}: forbidden project reference to {dependency}");
            }
            foreach (var package in items.PackageReferences)
            {
                if (!isModule) continue;
                if (package.Equals("Shipping.Platform.Api", StringComparison.OrdinalIgnoreCase) ||
                    package.StartsWith("Shipping.Modules.", StringComparison.OrdinalIgnoreCase) &&
                    !package.EndsWith(".Contracts", StringComparison.OrdinalIgnoreCase))
                    errors.Add($"{project}: forbidden module or host package {package}");
                if (project!.Layer == "Domain" && !package.Equals("MPCore.Domain", StringComparison.OrdinalIgnoreCase))
                    errors.Add($"{project}: forbidden Domain package {package}");
                if (project.Layer == "Application" && !ApplicationPackages.Contains(package, StringComparer.OrdinalIgnoreCase))
                    errors.Add($"{project}: forbidden Application package {package}");
            }
            foreach (var framework in items.FrameworkReferences)
            {
                if (isModule && (project!.Layer is "Domain" or "Application") && framework != "Microsoft.NETCore.App")
                    errors.Add($"{project}: forbidden framework {framework}");
            }
        }
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
            Visit(path);
        return errors;

        void Visit(string path)
        {
            if (active.Contains(path)) { errors.Add($"Project dependency cycle through {path}"); return; }
            if (!visited.Add(path)) return;
            active.Add(path);
            foreach (var target in graph[path].Where(graph.ContainsKey)) Visit(target);
            active.Remove(path);
        }
    }

    private static bool AllowedLayerEdge(string source, string target) => source switch
    {
        "Domain" => false,
        "Application" => target == "Domain",
        "Infrastructure" => target is "Application" or "Domain",
        "Api" => target is "Application" or "Infrastructure" or "Domain",
        _ => false
    };

    private sealed record ProjectIdentity(string Module, string Layer)
    {
        public static ProjectIdentity FromPath(string path)
        {
            var directory = new DirectoryInfo(Path.GetDirectoryName(path)!);
            var module = directory.Parent?.Name ?? throw new InvalidOperationException(path);
            var layer = Layers.SingleOrDefault(l => directory.Name == $"Shipping.Modules.{module}.{l}")
                ?? throw new InvalidOperationException($"Unexpected module project: {path}");
            return new(module, layer);
        }
        public override string ToString() => $"{Module}.{Layer}";
    }

    private sealed record EvaluatedItems(string[] ProjectReferences, string[] PackageReferences, string[] FrameworkReferences);

    private static async Task<EvaluatedItems> EvaluateItemsAsync(string path)
    {
        var result = await RunAsync("dotnet", ["msbuild", path, "-getItem:ProjectReference,PackageReference,FrameworkReference", "-nologo"], Path.GetDirectoryName(path)!);
        using var json = JsonDocument.Parse(result);
        var items = json.RootElement.GetProperty("Items");
        return new(
            Read("ProjectReference", "FullPath"),
            Read("PackageReference", "Identity"),
            Read("FrameworkReference", "Identity"));

        string[] Read(string kind, string field) => items.GetProperty(kind).EnumerateArray()
            .Select(item => item.GetProperty(field).GetString()!).ToArray();
    }

    [Fact]
    public async Task Template_can_generate_and_compile_a_disposable_host()
    {
        var template = Path.Combine(Root, "templates/Module");
        Assert.True(Directory.Exists(template), "Module template is missing");
        var output = Path.Combine(Path.GetTempPath(), "shipping-module-test-" + Guid.NewGuid().ToString("N"));
        var cliHome = Path.Combine(output, "cli-home");
        Directory.CreateDirectory(output);
        try
        {
            Directory.CreateDirectory(cliHome);
            await RunAsync("dotnet", ["new", "install", template, "--force"], output, cliHome);
            await RunAsync("dotnet", ["new", "shipping-module", "--name", "Verification", "--output", output, "--no-update-check"], output, cliHome);
            Assert.Equal(4, Directory.GetFiles(output, "*.csproj", SearchOption.AllDirectories).Length);
            foreach (var layer in Layers)
            {
                var project = Path.Combine(output, $"Shipping.Modules.Verification.{layer}", $"Shipping.Modules.Verification.{layer}.csproj");
                Assert.True(File.Exists(project));
                var references = XDocument.Load(project).Descendants("ProjectReference")
                    .Select(x => Path.GetFileNameWithoutExtension((string?)x.Attribute("Include"))).ToArray();
                var expected = layer switch
                {
                    "Domain" => Array.Empty<string>(),
                    "Application" => ["Shipping.Modules.Verification.Domain"],
                    "Infrastructure" => ["Shipping.Modules.Verification.Application"],
                    _ => ["Shipping.Modules.Verification.Application", "Shipping.Modules.Verification.Infrastructure"]
                };
                Assert.Equal(expected, references);
            }
            foreach (var file in Directory.GetFiles(output, "*.cs", SearchOption.AllDirectories)
                         .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
            {
                var contents = File.ReadAllText(file);
                Assert.DoesNotContain("Shipping.Modules.Sample", contents, StringComparison.Ordinal);
                Assert.Contains("Shipping.Modules.Verification", contents, StringComparison.Ordinal);
            }

            var host = Path.Combine(output, "DisposableHost");
            Directory.CreateDirectory(host);
            File.WriteAllText(Path.Combine(host, "DisposableHost.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk.Web"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
                <ItemGroup><ProjectReference Include="../Shipping.Modules.Verification.Api/Shipping.Modules.Verification.Api.csproj" /></ItemGroup></Project>
                """);
            File.WriteAllText(Path.Combine(host, "Program.cs"), """
                using Shipping.Modules.Verification.Api;
                var builder = WebApplication.CreateBuilder(args);
                builder.Services.AddVerificationModule();
                var app = builder.Build();
                app.MapVerificationModule();
                await app.RunAsync();
                """);
            await RunAsync("dotnet", ["build", Path.Combine(host, "DisposableHost.csproj"), "-c", "Release", "--ignore-failed-sources", "-m:1", "-p:BuildInParallel=false"], output, cliHome);
            foreach (var layer in Layers)
                Assert.True(File.Exists(Path.Combine(output, $"Shipping.Modules.Verification.{layer}", "bin", "Release", "net10.0", $"Shipping.Modules.Verification.{layer}.dll")));
            Assert.True(File.Exists(Path.Combine(host, "bin", "Release", "net10.0", "DisposableHost.dll")));
        }
        finally { Directory.Delete(output, true); }
    }

    private static async Task<string> RunAsync(string file, IReadOnlyList<string> args, string cwd, string? cliHome = null,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);
        var start = new ProcessStartInfo(file) { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        if (cliHome is not null) start.Environment["DOTNET_CLI_HOME"] = cliHome;
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {file}");
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"{file} {string.Join(' ', args)} timed out after {CommandTimeout}");
        }
        var output = await stdout + await stderr;
        Assert.True(process.ExitCode == 0, $"{file} {string.Join(' ', args)} exited {process.ExitCode}:{Environment.NewLine}{output}");
        return output;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var directory in Directory.GetDirectories(source))
        {
            if (Path.GetFileName(directory) is "bin" or "obj") continue;
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Shipping.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root");
    }
}
