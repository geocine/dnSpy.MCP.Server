using System;
using System.IO;
using System.Linq;
using Nuke.Common;
using Nuke.Common.Tooling;
using static Nuke.Common.Tooling.ProcessTasks;

class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.All);

    string Root => RootDirectory;
    string SubmoduleName => "dnSpy";
    string RootGitModulesPath => Path.Combine(Root, ".gitmodules");
    string SubmodulePath => Path.Combine(Root, SubmoduleName);
    string SubmoduleGitConfig => Path.Combine(Root, ".git", "modules", SubmoduleName, "config");
    string SolutionPath => Path.Combine(SubmodulePath, "dnSpy.sln");
    string McpProjectPath => Path.Combine(Root, "dnSpy.MCP.Server.csproj");
    string HostNet48Dir => Path.Combine(SubmodulePath, "dnSpy", "dnSpy", "bin", "Release", "net48");
    string ThemesDestinationDir => Path.Combine(HostNet48Dir, "Themes");
    string ThemesSourceDir => Path.Combine(HostNet48Dir, "bin", "Themes");

    Target All => _ => _
        .Description("Repairs/builds dnSpy, then builds the MCP extension for net10.0-windows.")
        .DependsOn(DnSpy)
        .Executes(() =>
        {
            BuildMcpProject("net10.0-windows");
        });

    Target DnSpy => _ => _
        .Description("Repairs/builds dnSpy and fixes known output quirks.")
        .DependsOn(FixDnSpyThemes);

    Target PrepareDnSpyCheckout => _ => _
        .Executes(() =>
        {
            if (!EnvironmentInfo.IsWin)
                throw new InvalidOperationException("This build requires Windows.");

            if (!File.Exists(RootGitModulesPath))
                throw new InvalidOperationException($".gitmodules was not found in '{Root}'.");

            Console.WriteLine("[1/4] Ensuring dnSpy submodule metadata...");
            if (File.Exists(SubmoduleGitConfig))
            {
                Console.WriteLine("Submodule metadata already exists. Skipping sync.");
            }
            else
            {
                RunGit("submodule sync --recursive");
            }

            Console.WriteLine("[2/4] Ensuring dnSpy submodule checkout...");
            if (SubmoduleIsAligned())
            {
                Console.WriteLine("Submodule checkout already matches the pinned commits. Skipping update.");
            }
            else
            {
                try
                {
                    RunGit($"submodule update --init --recursive --force {SubmoduleName}");
                }
                catch
                {
                    Console.WriteLine("Initial submodule update failed. Attempting clean recovery...");
                    RecoverSubmodule();
                }
            }
        });

    Target BuildDnSpySolution => _ => _
        .DependsOn(PrepareDnSpyCheckout)
        .Executes(() =>
        {
            Console.WriteLine("[3/4] Building dnSpy solution without a global TargetFramework override...");
            RunDotNet($"build \"{SolutionPath}\" -c Release --nologo");
        });

    Target FixDnSpyThemes => _ => _
        .DependsOn(BuildDnSpySolution)
        .Executes(() =>
        {
            Console.WriteLine("[4/4] Normalizing net48 theme files...");

            Directory.CreateDirectory(ThemesDestinationDir);

            if (Directory.Exists(ThemesSourceDir))
            {
                foreach (var sourceFile in Directory.GetFiles(ThemesSourceDir, "*.dntheme"))
                {
                    var destinationFile = Path.Combine(ThemesDestinationDir, Path.GetFileName(sourceFile));
                    File.Copy(sourceFile, destinationFile, overwrite: true);
                }
            }

            if (!Directory.EnumerateFiles(ThemesDestinationDir, "*.dntheme").Any())
                throw new InvalidOperationException($"No .dntheme files were found in '{ThemesDestinationDir}'.");

            Console.WriteLine($"Theme files are available in '{ThemesDestinationDir}'.");
        });

    Target McpNet10 => _ => _
        .Description("Builds the MCP extension for net10.0-windows.")
        .Executes(() =>
        {
            BuildMcpProject("net10.0-windows");
        });

    Target McpNet48 => _ => _
        .Description("Attempts to build the MCP extension for net48.")
        .Executes(() =>
        {
            BuildMcpProject("net48");
        });

    Target McpAll => _ => _
        .Description("Attempts to build the MCP extension for all target frameworks.")
        .Executes(() =>
        {
            RunDotNet($"build \"{McpProjectPath}\" -c Release --nologo");
        });

    bool SubmoduleIsAligned()
    {
        var process = RunProcessChecked("git", "submodule status --recursive", logOutput: false);
        var lines = process.Output
            .Select(x => x.Text)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        return lines.Count > 0 &&
               lines.All(line => line[0] == ' ' && !line.Contains("-dirty", StringComparison.OrdinalIgnoreCase));
    }

    void RecoverSubmodule()
    {
        RunGit($"submodule deinit -f -- {SubmoduleName}");

        if (Directory.Exists(SubmodulePath))
        {
            RunProcessChecked("cmd", $"/c rmdir /s /q \"{SubmodulePath}\"");
            if (Directory.Exists(SubmodulePath))
                throw new InvalidOperationException($"Failed to remove '{SubmodulePath}' during submodule recovery.");
        }

        RunGit("submodule sync --recursive");
        RunGit($"submodule update --init --recursive --force {SubmoduleName}");
    }

    void RunGit(string arguments)
    {
        RunProcessChecked("git", arguments);
    }

    void RunDotNet(string arguments)
    {
        RunProcessChecked("dotnet", arguments);
    }

    void BuildMcpProject(string framework)
    {
        RunDotNet($"build \"{McpProjectPath}\" -c Release -f {framework} --nologo");
    }

    IProcess RunProcessChecked(string tool, string arguments, bool logOutput = true)
    {
        var process = StartProcess(tool, arguments, workingDirectory: Root, logOutput: logOutput);
        process.AssertZeroExitCode();
        return process;
    }
}
