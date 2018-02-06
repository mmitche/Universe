
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.DotNet.VersionTools;
using Microsoft.DotNet.VersionTools.Dependencies;
using Microsoft.DotNet.VersionTools.Util;
using NuGet.Versioning;

namespace UpdateExternalDependencies
{
    public class DependenciesPropsDependencyUpdater : IDependencyUpdater
    {

        public DependenciesPropsDependencyUpdater(string path)
        {
            if (string.IsNullOrEmpty(Path))
            {
                throw new ArgumentNullException(nameof(path));
            }

            Path = path;
        }

        public string Path { get; set; }

        public IEnumerable<DependencyUpdateTask> GetUpdateTasks(IEnumerable<IDependencyInfo> dependencyBuildInfos)
        {

            var tasks = new List<DependencyUpdateTask>();

            IEnumerable<DependencyChange> dependencyChanges = null;

            
            Action update = FileUtils.GetUpdateFileContentsTask(
                ,
                contents => ReplaceAllDependencyVersions(
                    contents,
                    Path,
                    dependencyBuildInfos,
                    out dependencyChanges));

            // The output json may be different even if there weren't any changes made.
            if (update != null && dependencyChanges.Any())
            {
                tasks.Add(new DependencyUpdateTask(
                    update,
                    dependencyChanges.Select(change => change.BuildInfo),
                    dependencyChanges.Select(change => $"In '{Path}', {change.ToString()}")));
            }

            return tasks;
        }

        private string ReplaceAllDependencyVersions(
            string input,
            string projectJsonFile,
            IEnumerable<DependencyBuildInfo> buildInfos,
            out IEnumerable<DependencyChange> dependencyChanges)
        {
            JObject projectRoot = JObject.Parse(input);

            dependencyChanges = FindAllDependencyProperties(projectRoot)
                    .Select(dependencyProperty => ReplaceDependencyVersion(projectJsonFile, dependencyProperty, buildInfos))
                    .Where(buildInfo => buildInfo != null)
                    .ToArray();

            return JsonConvert.SerializeObject(projectRoot, Formatting.Indented) + Environment.NewLine;
        }

        /// <summary>
        /// Replaces the single dependency with the updated version, if it matches any of the
        /// dependencies that need to be updated. Stops on the first updated value found.
        /// </summary>
        /// <returns>The BuildInfo used to change the value, or null if there was no change.</returns>
        private DependencyChange ReplaceDependencyVersion(
            string projectJsonFile,
            JProperty dependencyProperty,
            IEnumerable<DependencyBuildInfo> parsedBuildInfos)
        {
            string id = dependencyProperty.Name;
            foreach (DependencyBuildInfo info in parsedBuildInfos)
            {
                foreach (PackageIdentity packageInfo in info.Packages)
                {
                    if (id != packageInfo.Id)
                    {
                        continue;
                    }

                    string oldVersion;
                    if (dependencyProperty.Value is JObject)
                    {
                        oldVersion = (string)dependencyProperty.Value["version"];
                    }
                    else
                    {
                        oldVersion = (string)dependencyProperty.Value;
                    }
                    VersionRange parsedOldVersionRange;
                    if (!VersionRange.TryParse(oldVersion, out parsedOldVersionRange))
                    {
                        Trace.TraceWarning($"Couldn't parse '{oldVersion}' for package '{id}' in '{projectJsonFile}'. Skipping.");
                        continue;
                    }
                    NuGetVersion oldNuGetVersion = parsedOldVersionRange.MinVersion;

                    if (oldNuGetVersion == packageInfo.Version)
                    {
                        // Versions match, no update to make.
                        continue;
                    }

                    if (oldNuGetVersion.IsPrerelease || info.UpgradeStableVersions)
                    {
                        string newVersion = packageInfo.Version.ToNormalizedString();
                        if (dependencyProperty.Value is JObject)
                        {
                            dependencyProperty.Value["version"] = newVersion;
                        }
                        else
                        {
                            dependencyProperty.Value = newVersion;
                        }

                        return new DependencyChange
                        {
                            BuildInfo = info.BuildInfo,
                            PackageId = id,
                            Before = oldNuGetVersion,
                            After = packageInfo.Version
                        };
                    }
                }
            }
            return null;
        }

        private static IEnumerable<Dependency> FindAllDependencyProperties(XDocument dependencyProp)
        {
            var dependencies = new List<Dependency>();

            var project = dependencyProp.Element("Project");

            foreach (var propGroup in project.Elements("PropertyGroup"))
            {
                foreach (var depElement in propGroup.Descendants())
                {

                }
            }
        }

        private static string ReplaceGroupValue(
            Regex regex,
            string input,
            string groupName,
            string newValue,
            out string outOriginalValue)
        {
            string originalValue = null;
            string replacement = regex.Replace(input, m =>
            {
                string match = m.Value;
                Group group = m.Groups[groupName];
                int startIndex = group.Index - m.Index;
                originalValue = group.Value;

                return match
                    .Remove(startIndex, group.Length)
                    .Insert(startIndex, newValue);
            });
            // Assign out to captured variable.
            outOriginalValue = originalValue;
            return replacement;
        }

        private class Dependency
        {
            public string PackageId { get; set; }
            public NuGetVersion Version { get; set; }
        }

        private class DependencyChange
        {
            public BuildInfo BuildInfo { get; set; }
            public string PackageId { get; set; }
            public NuGetVersion Before { get; set; }
            public NuGetVersion After { get; set; }

            public override string ToString()
            {
                return $"'{PackageId} {Before.ToNormalizedString()}' must be " +
                    $"'{After.ToNormalizedString()}' ({BuildInfo.Name})";
            }
        }
    }
}
