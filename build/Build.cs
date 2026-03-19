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
    string DnSpySubmodulePath => Path.Combine(Root, SubmoduleName);
    string De4dotSubmoduleName => "de4dotEx";
    string DnSpySubmoduleGitConfig => Path.Combine(Root, ".git", "modules", SubmoduleName, "config");
    string De4dotSubmoduleGitConfig => Path.Combine(Root, ".git", "modules", De4dotSubmoduleName, "config");
    string SolutionPath => Path.Combine(DnSpySubmodulePath, "dnSpy.sln");
    string McpProjectPath => Path.Combine(Root, "dnSpy.MCP.Server.csproj");

    Target All => _ => _
        .Description("Repairs/builds dnSpy and de4dotEx, then builds the MCP extension for net10.0-windows.")
        .DependsOn(DnSpy)
        .Executes(() =>
        {
            BuildMcpProject("net10.0-windows");
        });

    Target DnSpy => _ => _
        .Description("Repairs/builds dnSpy and ensures the de4dotEx submodule is available.")
        .DependsOn(PrepareSubmodules, BuildDnSpySolution);

    Target PrepareSubmodules => _ => _
        .Executes(() =>
        {
            if (!EnvironmentInfo.IsWin)
                throw new InvalidOperationException("This build requires Windows.");

            if (!File.Exists(RootGitModulesPath))
                throw new InvalidOperationException($".gitmodules was not found in '{Root}'.");

            Console.WriteLine("[1/3] Ensuring dnSpy submodule metadata...");
            if (File.Exists(DnSpySubmoduleGitConfig) && File.Exists(De4dotSubmoduleGitConfig))
            {
                Console.WriteLine("Submodule metadata already exists. Skipping sync.");
            }
            else
            {
                RunGit("submodule sync --recursive");
            }

            Console.WriteLine("[2/3] Ensuring dnSpy and de4dotEx submodule checkouts...");
            if (SubmodulesAreAligned())
            {
                Console.WriteLine("Submodule checkouts already match the pinned commits. Skipping update.");
            }
            else
            {
                try
                {
                    RunGit($"submodule update --init --recursive --force {SubmoduleName} {De4dotSubmoduleName}");
                }
                catch
                {
                    Console.WriteLine("Initial submodule update failed. Attempting clean recovery...");
                    RecoverSubmodule(SubmoduleName, DnSpySubmodulePath);
                    RecoverSubmodule(De4dotSubmoduleName, Path.Combine(Root, De4dotSubmoduleName));
                }
            }
        });

    Target BuildDnSpySolution => _ => _
        .DependsOn(PrepareSubmodules)
        .Executes(() =>
        {
            Console.WriteLine("[3/3] Building dnSpy solution for net10.0-windows...");
            RunDotNet($"build \"{SolutionPath}\" -c Release -p:TargetFramework=net10.0-windows --nologo");
        });

    Target Mcp => _ => _
        .Description("Builds the MCP extension for net10.0-windows.")
        .Executes(() =>
        {
            BuildMcpProject("net10.0-windows");
        });

    bool SubmodulesAreAligned()
    {
        var process = RunProcessChecked("git", "submodule status --recursive", logOutput: false);
        var lines = process.Output
            .Select(x => x.Text)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        return lines.Count > 0 &&
               lines.All(line => line[0] == ' ' && !line.Contains("-dirty", StringComparison.OrdinalIgnoreCase));
    }

    void RecoverSubmodule(string submoduleName, string submodulePath)
    {
        RunGit($"submodule deinit -f -- {submoduleName}");

        if (Directory.Exists(submodulePath))
        {
            RunProcessChecked("cmd", $"/c rmdir /s /q \"{submodulePath}\"");
            if (Directory.Exists(submodulePath))
                throw new InvalidOperationException($"Failed to remove '{submodulePath}' during submodule recovery.");
        }

        RunGit("submodule sync --recursive");
        RunGit($"submodule update --init --recursive --force {submoduleName}");
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
