
using Microsoft.DotNet.VersionTools;
using Microsoft.DotNet.VersionTools.Automation;
using Microsoft.DotNet.VersionTools.BuildManifest.Model;
using Microsoft.DotNet.VersionTools.Dependencies;
using Microsoft.DotNet.VersionTools.Dependencies.BuildOutput;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace UpdateExternalDependencies
{
    public static class Program
    {
        private const string _depsFile = "";
        private static Options Options { get; set; } = new Options();

        public static async Task Main(string[] args)
        {
            try
            {
                Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));
                Options.Parse(args);
                DependencyUpdateResults updateResults = await UpdateFilesAsync();
                if (updateResults.ChangesDetected())
                {
                    if (Options.UpdateOnly)
                    {
                        Trace.TraceInformation($"Changes made but no GitHub credentials specified, skipping PR creation");
                    }
                    else
                    {
                        await CreatePullRequestAsync(updateResults);
                    }
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Failed to update dependencies:{Environment.NewLine}{e.ToString()}");
                Environment.Exit(1);
            }

            Environment.Exit(0);
        }

        private static async Task<DependencyUpdateResults> UpdateFilesAsync()
        {
                IEnumerable<IDependencyInfo> buildInfos = await GetBuildInfoAsync();
                IEnumerable<IDependencyUpdater> updaters = GetUpdaters();

                return DependencyUpdateUtils.Update(updaters, buildInfos);
        }

        private static async Task<IEnumerable<IDependencyInfo>> GetBuildInfoAsync()
        {
            Trace.TraceInformation($"Retrieving build info from '{Options.BuildInfoUrl}'");

            using (HttpClient client = new HttpClient())
            using (Stream stream = await client.GetStreamAsync(Options.BuildInfoUrl))
            {
                XDocument buildInfoXml = XDocument.Load(stream);
                OrchestratedBuildModel buildInfo = OrchestratedBuildModel.Parse(buildInfoXml.Root);

                throw new NotImplementedException();
            };
        }

        private static IDependencyInfo CreateDependencyBuildInfo(string name, string latestReleaseVersion)
        {
            return new BuildDependencyInfo(
                new BuildInfo()
                {
                    Name = name,
                    LatestReleaseVersion = latestReleaseVersion,
                    LatestPackages = new Dictionary<string, string>()
                },
                false,
                Enumerable.Empty<string>());
        }

        private static async Task CreatePullRequestAsync(DependencyUpdateResults updateResults)
        {
            GitHubAuth gitHubAuth = new GitHubAuth(Options.GitHubPassword, Options.GitHubUser, Options.GitHubEmail);
            PullRequestCreator prCreator = new PullRequestCreator(gitHubAuth, Options.GitHubUser);
            PullRequestOptions prOptions = new PullRequestOptions()
            {
                BranchNamingStrategy = new SingleBranchNamingStrategy($"UpdateDependencies-{Options.GitHubUpstreamBranch}")
            };

            var commitMessage = $"Update {Options.GitHubUpstreamBranch} to latest.";

            await prCreator.CreateOrUpdateAsync(
                commitMessage,
                commitMessage,
                string.Empty,
                new GitHubBranch(Options.GitHubUpstreamBranch, new GitHubProject(Options.GitHubProject, Options.GitHubUpstreamOwner)),
                new GitHubProject(Options.GitHubProject, gitHubAuth.User),
                prOptions);
        }

        private static string GetBuildVersion(this IEnumerable<IDependencyInfo> buildInfos, string name)
        {
            return buildInfos.First(bi => bi.SimpleName == name).SimpleVersion;
        }

        private static IEnumerable<IDependencyUpdater> GetUpdaters()
        {
            Trace.TraceInformation($"Updating {_depsFile}");

            return new IDependencyUpdater[]{
                new DependenciesPropsDependencyUpdater(_depsFile)
            };
        }
    }
}
