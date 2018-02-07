
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
                Path,
                contents => ReplaceAllDependencyVersions(
                    contents,
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
            string dependencyPropsContent,
            IEnumerable<IDependencyInfo> buildInfos,
            out IEnumerable<DependencyChange> dependencyChanges)
        {
            var document = XDocument.Parse(dependencyPropsContent);

            var depChanges = new List<DependencyChange>();

            var project = document.Element("Project");

            foreach (var propGroup in project.Elements("PropertyGroup"))
            {
                foreach (var depElement in propGroup.Descendants())
                {
                    var depName = depElement.Name;
                    var depVersion = depElement.Value;
                    var infos = buildInfos.Where(s => s.SimpleName == depName && s.SimpleVersion != depVersion);
                    if(infos.Count() > 0)
                    {
                        var info = infos.First();
                        depChanges.Add(new DependencyChange {
                            PackageId = depName.LocalName,
                            After = NuGetVersion.Parse(info.SimpleVersion),
                            Before = NuGetVersion.Parse(depVersion),
                            BuildInfo = info
                        });

                        depElement.Value = info.SimpleVersion;
                    }
                }
            }

            dependencyChanges = depChanges;

            return document.ToString();
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
            public IDependencyInfo BuildInfo { get; set; }
            public string PackageId { get; set; }
            public NuGetVersion Before { get; set; }
            public NuGetVersion After { get; set; }

            public override string ToString()
            {
                return $"'{PackageId} {Before.ToNormalizedString()}' must be " +
                    $"'{After.ToNormalizedString()}' ({BuildInfo.SimpleName})";
            }
        }
    }
}
