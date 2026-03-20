using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nuke.Common;
using Nuke.Common.Tooling;
using static Nuke.Common.Tooling.ProcessTasks;

class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.All);

    string Root => RootDirectory;
    string RootGitModulesPath => Path.Combine(Root, ".gitmodules");

    string DnSpySubmodulePathRelative => Path.Combine("hosts", "dnSpy");
    string DnSpySubmodulePath => Path.Combine(Root, DnSpySubmodulePathRelative);
    string DnSpySubmoduleGitConfig => Path.Combine(Root, ".git", "modules", "dnSpy", "config");

    string De4dotSubmodulePathRelative => Path.Combine("vendors", "de4dotEx");
    string De4dotSubmodulePath => Path.Combine(Root, De4dotSubmodulePathRelative);
    string De4dotSubmoduleGitConfig => Path.Combine(Root, ".git", "modules", "de4dotEx", "config");

    string HollySubmodulePathRelative => Path.Combine("extensions", "dnSpy.Extension.HoLLy");
    string HollySubmodulePath => Path.Combine(Root, HollySubmodulePathRelative);
    string HollySubmoduleGitConfig => Path.Combine(Root, ".git", "modules", "dnSpy.Extension.HoLLy", "config");

    string EchoSubmodulePathRelative => Path.Combine("vendors", "Echo");
    string EchoSubmodulePath => Path.Combine(Root, EchoSubmodulePathRelative);
    string EchoSubmoduleGitConfig => Path.Combine(Root, ".git", "modules", "vendors", "Echo", "config");

    string DnSpySolutionPath => Path.Combine(DnSpySubmodulePath, "dnSpy.sln");
    string McpProjectPath => Path.Combine(Root, "extensions", "dnSpy.MCP.Server", "dnSpy.MCP.Server.csproj");
    string HollyProjectPath => Path.Combine(HollySubmodulePath, "dnSpy.Extension.HoLLy", "dnSpy.Extension.HoLLy.csproj");
    string McpBuildOutputDir => Path.Combine(Root, "extensions", "dnSpy.MCP.Server", "bin", "Release", "net10.0-windows");
    string HollyBuildOutputDir => Path.Combine(HollySubmodulePath, "dnSpy.Extension.HoLLy", "bin", "Release", "net10.0-windows");

    string DnSpyInstallRoot => Path.Combine(DnSpySubmodulePath, "dnSpy", "dnSpy", "bin", "Release", "net10.0-windows");
    string RepoBinLinkPath => Path.Combine(Root, "bin");
    string McpInstallDir => Path.Combine(DnSpyInstallRoot, "Extensions", "dnSpy.MCP.Server");
    string HollyInstallDir => Path.Combine(DnSpyInstallRoot, "Extensions", "dnSpy.Extension.HoLLy");

    Target All => _ => _
        .Description("Repairs/builds dnSpy, then builds and installs the MCP and HoLLy extensions for net10.0-windows.")
        .DependsOn(DnSpy)
        .Executes(() =>
        {
            BuildMcpProject("net10.0-windows");
            BuildHollyProject("net10.0-windows");
        });

    Target DnSpy => _ => _
        .Description("Repairs/builds the shared dnSpy host and ensures required submodules are available.")
        .DependsOn(PrepareSubmodules, BuildDnSpySolution);

    Target PrepareSubmodules => _ => _
        .Executes(() =>
        {
            if (!EnvironmentInfo.IsWin)
                throw new InvalidOperationException("This build requires Windows.");

            if (!File.Exists(RootGitModulesPath))
                throw new InvalidOperationException($".gitmodules was not found in '{Root}'.");

            Console.WriteLine("[1/3] Ensuring submodule metadata...");
            if (File.Exists(DnSpySubmoduleGitConfig) &&
                File.Exists(De4dotSubmoduleGitConfig) &&
                File.Exists(HollySubmoduleGitConfig) &&
                File.Exists(EchoSubmoduleGitConfig))
            {
                Console.WriteLine("Submodule metadata already exists. Skipping sync.");
            }
            else
            {
                RunGit("submodule sync --recursive");
            }

            Console.WriteLine("[2/3] Ensuring dnSpy, de4dotEx, HoLLy, and Echo checkouts...");
            if (Directory.Exists(DnSpySubmodulePath) &&
                Directory.Exists(De4dotSubmodulePath) &&
                Directory.Exists(HollySubmodulePath) &&
                Directory.Exists(EchoSubmodulePath))
            {
                Console.WriteLine("Required submodule working trees already exist. Skipping update.");
            }
            else
            {
                try
                {
                    RunGit(
                        "submodule update --init --recursive " +
                        $"\"{DnSpySubmodulePathRelative}\" " +
                        $"\"{De4dotSubmodulePathRelative}\" " +
                        $"\"{HollySubmodulePathRelative}\" " +
                        $"\"{EchoSubmodulePathRelative}\"");
                }
                catch
                {
                    Console.WriteLine("Initial submodule update failed. Attempting clean recovery...");
                    RecoverSubmodule(DnSpySubmodulePathRelative, DnSpySubmodulePath);
                    RecoverSubmodule(De4dotSubmodulePathRelative, De4dotSubmodulePath);
                    RecoverSubmodule(HollySubmodulePathRelative, HollySubmodulePath);
                    RecoverSubmodule(EchoSubmodulePathRelative, EchoSubmodulePath);
                }
            }
        });

    Target BuildDnSpySolution => _ => _
        .DependsOn(PrepareSubmodules)
        .Executes(() =>
        {
            Console.WriteLine("[3/3] Building dnSpy solution for net10.0-windows...");
            RunDotNet($"build \"{DnSpySolutionPath}\" -c Release -p:TargetFramework=net10.0-windows --nologo");
            EnsureRepoBinLink();
        });

    Target Mcp => _ => _
        .Description("Builds and installs the MCP extension for net10.0-windows.")
        .DependsOn(DnSpy)
        .Executes(() =>
        {
            BuildMcpProject("net10.0-windows");
        });

    Target Holly => _ => _
        .Description("Builds and installs the HoLLy extension for net10.0-windows.")
        .DependsOn(DnSpy)
        .Executes(() =>
        {
            BuildHollyProject("net10.0-windows");
        });

    Target SyncHolly => _ => _
        .Description("Fetches the HoLLy submodule, safely tracks the latest origin/master on a local sync branch, and updates nested submodules.")
        .DependsOn(PrepareSubmodules)
        .Executes(() =>
        {
            SyncHollySubmodule();
        });

    void RecoverSubmodule(string submodulePathRelative, string submodulePath)
    {
        RunGit($"submodule deinit -f -- \"{submodulePathRelative}\"");

        if (Directory.Exists(submodulePath))
        {
            RunProcessChecked("cmd", $"/c rmdir /s /q \"{submodulePath}\"");
            if (Directory.Exists(submodulePath))
                throw new InvalidOperationException($"Failed to remove '{submodulePath}' during submodule recovery.");
        }

        RunGit("submodule sync --recursive");
        RunGit($"submodule update --init --recursive --force \"{submodulePathRelative}\"");
    }

    void RunGit(string arguments, bool logOutput = true)
    {
        RunProcessChecked("git", arguments, logOutput: logOutput);
    }

    string RunGitCapture(string arguments)
    {
        var process = RunProcessChecked("git", arguments, logOutput: false);
        return string.Join(
                Environment.NewLine,
                process.Output
                    .Select(x => x.Text)
                    .Where(x => !string.IsNullOrWhiteSpace(x)))
            .Trim();
    }

    string? TryRunGitCapture(string arguments)
    {
        var process = StartProcess("git", arguments, workingDirectory: Root, logOutput: false);
        process.WaitForExit();
        if (process.ExitCode != 0)
            return null;

        return string.Join(
                Environment.NewLine,
                process.Output
                    .Select(x => x.Text)
                    .Where(x => !string.IsNullOrWhiteSpace(x)))
            .Trim();
    }

    void RunDotNet(string arguments)
    {
        RunProcessChecked("dotnet", arguments);
    }

    void BuildMcpProject(string framework)
    {
        RunDotNet(
            $"build \"{McpProjectPath}\" -c Release -f {framework} --nologo " +
            $"-p:DnSpyRoot=\"{DnSpySubmodulePath}\" " +
            $"-p:De4dotRoot=\"{De4dotSubmodulePath}\"");
        CopyDirectoryContents(McpBuildOutputDir, McpInstallDir);
        MirrorExtensionToRuntimeDirs("dnSpy.MCP.Server", McpInstallDir);
    }

    void BuildHollyProject(string framework)
    {
        RunDotNet(
            $"build \"{HollyProjectPath}\" -c Release -f {framework} --nologo " +
            $"-p:DnSpyRoot=\"{DnSpySubmodulePath}\" " +
            $"-p:EchoRoot=\"{EchoSubmodulePath}\"");
        CopyDirectoryContents(HollyBuildOutputDir, HollyInstallDir);
        MirrorExtensionToRuntimeDirs("dnSpy.Extension.HoLLy", HollyInstallDir);
    }

    void SyncHollySubmodule()
    {
        const string syncBranchName = "monorepo-sync";

        var previousTargetCommit = TryRunGitCapture($"-C \"{HollySubmodulePath}\" rev-parse --verify origin/master");
        RunGit($"-C \"{HollySubmodulePath}\" fetch origin --prune", logOutput: false);

        var currentCommit = RunGitCapture($"-C \"{HollySubmodulePath}\" rev-parse HEAD");
        var targetCommit = RunGitCapture($"-C \"{HollySubmodulePath}\" rev-parse origin/master");
        var currentBranch = RunGitCapture($"-C \"{HollySubmodulePath}\" branch --show-current");
        var isDetachedHead = string.IsNullOrWhiteSpace(currentBranch);
        var hasDirtyChanges = !string.IsNullOrWhiteSpace(RunGitCapture($"-C \"{HollySubmodulePath}\" status --porcelain"));

        ReportRemoteUpdate(previousTargetCommit, targetCommit);

        if (isDetachedHead && !string.Equals(currentCommit, targetCommit, StringComparison.OrdinalIgnoreCase))
            ProtectDetachedCommit(currentCommit);

        var shouldAutostash = hasDirtyChanges && !string.Equals(currentCommit, targetCommit, StringComparison.OrdinalIgnoreCase);
        var stashRef = string.Empty;

        if (shouldAutostash)
            stashRef = CreateAutostash();

        try
        {
            Console.WriteLine($"Updating HoLLy branch '{syncBranchName}' to {targetCommit}.");
            RunGit($"-C \"{HollySubmodulePath}\" checkout -B {syncBranchName} origin/master", logOutput: false);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(stashRef))
                RestoreAutostash(stashRef);
        }

        Console.WriteLine("Syncing HoLLy nested submodules.");
        RunGit($"-C \"{HollySubmodulePath}\" submodule update --init --recursive", logOutput: false);
    }

    void ReportRemoteUpdate(string? previousTargetCommit, string targetCommit)
    {
        if (string.IsNullOrWhiteSpace(previousTargetCommit))
        {
            Console.WriteLine($"Fetched HoLLy origin/master at {targetCommit}.");
            return;
        }

        if (string.Equals(previousTargetCommit, targetCommit, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"HoLLy origin/master is already at {targetCommit}.");
            return;
        }

        var wasFastForward = IsAncestorCommit(previousTargetCommit, targetCommit);
        var updateKind = wasFastForward ? "advanced" : "was force-updated";
        Console.WriteLine($"HoLLy origin/master {updateKind} from {previousTargetCommit} to {targetCommit}.");
    }

    bool IsAncestorCommit(string ancestorCommit, string descendantCommit)
    {
        var process = StartProcess(
            "git",
            $"-C \"{HollySubmodulePath}\" merge-base --is-ancestor {ancestorCommit} {descendantCommit}",
            workingDirectory: Root,
            logOutput: false);
        process.WaitForExit();
        return process.ExitCode == 0;
    }

    void ProtectDetachedCommit(string currentCommit)
    {
        var refsContainingCommit = RunGitCapture(
            $"-C \"{HollySubmodulePath}\" for-each-ref --format=\"%(refname)\" --contains {currentCommit}");
        if (!string.IsNullOrWhiteSpace(refsContainingCommit))
            return;

        var backupBranchName = $"sync-backup/{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        Console.WriteLine($"Creating HoLLy backup branch '{backupBranchName}' at {currentCommit} before switching away from detached HEAD.");
        RunGit($"-C \"{HollySubmodulePath}\" branch \"{backupBranchName}\" {currentCommit}");
    }

    string CreateAutostash()
    {
        var stashMessage = $"sync-holly-autostash-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        RunGit($"-C \"{HollySubmodulePath}\" stash push --include-untracked --message \"{stashMessage}\"");

        var stashList = RunGitCapture($"-C \"{HollySubmodulePath}\" stash list");
        var stashEntry = stashList
            .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.Contains(stashMessage, StringComparison.Ordinal));
        if (stashEntry is null)
            throw new InvalidOperationException("HoLLy sync expected an autostash entry but none was found.");

        var match = Regex.Match(stashEntry, @"^(stash@\{\d+\})");
        if (!match.Success)
            throw new InvalidOperationException($"HoLLy sync could not parse the autostash reference from '{stashEntry}'.");

        Console.WriteLine($"Stashed HoLLy working tree changes to {match.Groups[1].Value} before switching commits.");
        return match.Groups[1].Value;
    }

    void RestoreAutostash(string stashRef)
    {
        Console.WriteLine($"Restoring HoLLy working tree changes from {stashRef}.");
        RunGit($"-C \"{HollySubmodulePath}\" stash pop {stashRef}");
    }

    void MirrorExtensionToRuntimeDirs(string extensionName, string sourceDirectory)
    {
        foreach (var runtime in new[] { "win-x64", "win-x86" })
        {
            var runtimeRoot = Path.Combine(DnSpyInstallRoot, runtime);
            if (!Directory.Exists(runtimeRoot))
                continue;

            var destinationDirectory = Path.Combine(runtimeRoot, "Extensions", extensionName);
            CopyDirectoryContents(sourceDirectory, destinationDirectory);
        }
    }

    void EnsureRepoBinLink()
    {
        var fileInfo = new FileInfo(RepoBinLinkPath);
        if (fileInfo.Exists)
            throw new InvalidOperationException($"'{RepoBinLinkPath}' exists as a file. Remove it before running the build.");

        if (Directory.Exists(RepoBinLinkPath))
        {
            var existingDirectory = new DirectoryInfo(RepoBinLinkPath);
            var existingIsLink = existingDirectory.Attributes.HasFlag(FileAttributes.ReparsePoint);

            if (!existingIsLink)
                throw new InvalidOperationException(
                    $"'{RepoBinLinkPath}' exists as a real directory. Remove or rename it before the build can create the dnSpy bin junction.");

            var currentTarget = TryGetLinkTarget(existingDirectory);
            if (PathsEqual(currentTarget, DnSpyInstallRoot))
            {
                Console.WriteLine($"Repository bin link already points to '{DnSpyInstallRoot}'.");
                return;
            }

            Console.WriteLine($"Refreshing repository bin link to point at '{DnSpyInstallRoot}'.");
            RunProcessChecked("cmd", $"/c rmdir \"{RepoBinLinkPath}\"");
        }
        else
        {
            Console.WriteLine($"Creating repository bin link -> '{DnSpyInstallRoot}'.");
        }

        RunProcessChecked("cmd", $"/c mklink /J \"{RepoBinLinkPath}\" \"{DnSpyInstallRoot}\"");
    }

    string? TryGetLinkTarget(DirectoryInfo directoryInfo)
    {
        try
        {
            var directTarget = directoryInfo.LinkTarget;
            if (!string.IsNullOrWhiteSpace(directTarget))
                return NormalizeFullPath(Path.Combine(directoryInfo.Parent?.FullName ?? Root, directTarget));

            var resolvedTarget = directoryInfo.ResolveLinkTarget(returnFinalTarget: false);
            return resolvedTarget is null ? null : NormalizeFullPath(resolvedTarget.FullName);
        }
        catch
        {
            return null;
        }
    }

    bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        return string.Equals(
            NormalizeFullPath(left),
            NormalizeFullPath(right),
            StringComparison.OrdinalIgnoreCase);
    }

    string NormalizeFullPath(string path)
    {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    void CopyDirectoryContents(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var destinationFile = Path.Combine(destinationDirectory, relativePath);
            var destinationFolder = Path.GetDirectoryName(destinationFile);
            if (!string.IsNullOrEmpty(destinationFolder))
                Directory.CreateDirectory(destinationFolder);

            var preserveExistingConfig =
                string.Equals(Path.GetFileName(file), "mcp-config.json", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(destinationFile);
            if (preserveExistingConfig)
                continue;

            File.Copy(file, destinationFile, overwrite: true);
        }
    }

    IProcess RunProcessChecked(string tool, string arguments, bool logOutput = true)
    {
        var process = StartProcess(tool, arguments, workingDirectory: Root, logOutput: logOutput);
        process.AssertZeroExitCode();
        return process;
    }
}
