// STORY-204 — Dashboards are code
//
// BDD specification — xUnit (SPEC F78.3, F78.9). Pure file contracts over
// observability/grafana/ — no docker, no Grafana: dashboard JSON parses, the Ops board
// declares the station/env template variables, a file-provisioning skeleton accompanies
// it, and every committed query selects on contract labels only.
//
// Pending until T50 (/build-loop unskips).

using System.Text.Json;
using System.Text.RegularExpressions;

namespace GenWave.Host.Tests.Specs;

public static class FeatureDashboardsAreCode
{
    const string Pending = "pending: T50 — Ops dashboard + provisioning (unskip in /build-loop)";

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent;

        if (dir is null) throw new InvalidOperationException("repo root (GenWave.sln) not found");
        return dir.FullName;
    }

    static string GrafanaDir => Path.Combine(RepoRoot(), "observability", "grafana");

    static string[] DashboardFiles() =>
        Directory.GetFiles(Path.Combine(GrafanaDir, "dashboards"), "*.json", SearchOption.AllDirectories);

    public static class ScenarioDashboardAndProvisioningShape
    {
        [Fact(Skip = Pending)]
        public static void At_least_the_ops_dashboard_exists()
        {
            Assert.Contains(DashboardFiles(), f => Path.GetFileName(f).Contains("ops", StringComparison.OrdinalIgnoreCase));
        }

        [Fact(Skip = Pending)]
        public static void Every_committed_dashboard_parses_as_json()
        {
            Assert.All(DashboardFiles(), f => JsonDocument.Parse(File.ReadAllText(f)).Dispose());
        }

        [Fact(Skip = Pending)]
        public static void Ops_dashboard_declares_station_and_env_template_variables()
        {
            var opsPath = DashboardFiles().Single(f => Path.GetFileName(f).Contains("ops", StringComparison.OrdinalIgnoreCase));
            using var dashboard = JsonDocument.Parse(File.ReadAllText(opsPath));
            var variables = dashboard.RootElement.GetProperty("templating").GetProperty("list")
                .EnumerateArray().Select(v => v.GetProperty("name").GetString()).ToArray();

            Assert.Superset(new HashSet<string?> { "station", "env" }, variables.ToHashSet());
        }

        [Fact(Skip = Pending)]
        public static void A_file_provisioning_skeleton_accompanies_the_dashboards()
        {
            // Grafana file provisioning: a datasource yaml and a dashboard-provider yaml
            var provisioning = Path.Combine(GrafanaDir, "provisioning");
            Assert.True(
                Directory.GetFiles(provisioning, "*.y*ml", SearchOption.AllDirectories).Length >= 2,
                "expected at least a datasource yaml and a dashboard-provider yaml under observability/grafana/provisioning/");
        }
    }

    public static class ScenarioContractLabelsOnly
    {
        static readonly HashSet<string> ContractLabels = ["service", "station", "env"];

        [Fact(Skip = Pending)]
        public static void Every_query_selects_on_contract_labels_only()
        {
            // Extract every LogQL stream selector `{k="v", k2=~"v2"}` from every "expr"
            // in every committed dashboard; the label keys used must all be contract labels.
            var offenders = new List<string>();
            foreach (var file in DashboardFiles())
            {
                foreach (Match expr in Regex.Matches(File.ReadAllText(file), @"""expr""\s*:\s*""((?:[^""\\]|\\.)*)"""))
                {
                    foreach (Match selector in Regex.Matches(expr.Groups[1].Value, @"\{([^}]*)\}"))
                    {
                        foreach (Match label in Regex.Matches(selector.Groups[1].Value, @"([a-zA-Z_][a-zA-Z0-9_]*)\s*(=~?|!~?|!=)"))
                        {
                            if (!ContractLabels.Contains(label.Groups[1].Value))
                                offenders.Add($"{Path.GetFileName(file)}: {label.Groups[1].Value}");
                        }
                    }
                }
            }

            Assert.Empty(offenders);
        }
    }
}
