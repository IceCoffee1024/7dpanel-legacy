using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.DependencyInjection;
using LSTY.SevenDPanel.Application.Automations;
using LSTY.SevenDPanel.Domain.Automations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Owin;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class AutomationIntegrationsHttpTests
    {
        private static readonly DateTimeOffset Now =
            new(2026, 7, 27, 8, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Controller_exposes_only_the_fixed_owner_routes()
        {
            var controller = typeof(AutomationsController);
            var prefix = controller.GetCustomAttribute<RoutePrefixAttribute>();
            var authorize = controller.GetCustomAttributes<AuthorizeAttribute>().Single();
            var actual = controller.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .SelectMany(method => method.GetCustomAttributes<RouteAttribute>()
                    .Select(route => (
                        Verb: Verb(method),
                        Route: route.Template ?? string.Empty)))
                .OrderBy(route => route.Verb, StringComparer.Ordinal)
                .ThenBy(route => route.Route, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal("api/v1/automations", prefix!.Prefix);
            Assert.Equal("Owner", authorize.Roles);
            Assert.Equal(
                new[]
                {
                    ("DELETE", "{ruleId}"),
                    ("GET", ""),
                    ("GET", "executions"),
                    ("GET", "executions/{executionId}"),
                    ("GET", "{ruleId}"),
                    ("POST", ""),
                    ("POST", "dry-run"),
                    ("POST", "validate"),
                    ("PUT", "{ruleId}")
                },
                actual);
        }

        [Theory]
        [InlineData("Admin", HttpStatusCode.Forbidden)]
        [InlineData("Viewer", HttpStatusCode.Forbidden)]
        [InlineData(null, HttpStatusCode.Unauthorized)]
        public async Task Automation_routes_are_owner_only(
            string? role,
            HttpStatusCode expectedStatus)
        {
            using var host = CreateHost(role);

            using var response = await host.Client.GetAsync(
                "api/v1/automations",
                TestContext.Current.CancellationToken);

            Assert.Equal(expectedStatus, response.StatusCode);
            if (expectedStatus == HttpStatusCode.Forbidden)
            {
                var problem = JObject.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal("owner_required", (string?)problem["code"]);
            }
        }

        [Fact]
        public async Task Owner_can_create_read_update_list_and_delete_a_typed_rule()
        {
            using var host = CreateHost("Owner");

            using var createdResponse = await PostJson(
                host.Client,
                "api/v1/automations",
                Rule());
            var created = JObject.Parse(await createdResponse.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
            Assert.EndsWith(
                "/api/v1/automations/rule-1",
                createdResponse.Headers.Location!.AbsoluteUri,
                StringComparison.Ordinal);
            AssertRule(created, expectedVersion: 1, expectedName: "Welcome");

            using var getResponse = await host.Client.GetAsync(
                "api/v1/automations/rule-1",
                TestContext.Current.CancellationToken);
            var found = JObject.Parse(await getResponse.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
            AssertRule(found, expectedVersion: 1, expectedName: "Welcome");

            var update = Rule(expectedVersion: 1);
            update["name"] = "Welcome back";
            using var updateResponse = await PutJson(
                host.Client,
                "api/v1/automations/rule-1",
                update);
            var updated = JObject.Parse(await updateResponse.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
            AssertRule(updated, expectedVersion: 2, expectedName: "Welcome back");

            using var listResponse = await host.Client.GetAsync(
                "api/v1/automations",
                TestContext.Current.CancellationToken);
            var listed = JArray.Parse(await listResponse.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            AssertRule((JObject)Assert.Single(listed), expectedVersion: 2, expectedName: "Welcome back");

            using var deleteResponse = await host.Client.DeleteAsync(
                "api/v1/automations/rule-1?expectedVersion=2",
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

            using var missingResponse = await host.Client.GetAsync(
                "api/v1/automations/rule-1",
                TestContext.Current.CancellationToken);
            var missing = JObject.Parse(await missingResponse.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
            Assert.Equal("automation_rule_not_found", (string?)missing["code"]);
        }

        [Fact]
        public async Task Stale_rule_versions_return_stable_conflict_problem_details()
        {
            using var host = CreateHost("Owner");
            using var created = await PostJson(host.Client, "api/v1/automations", Rule());
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            var stale = Rule(expectedVersion: 2);
            using var response = await PutJson(
                host.Client,
                "api/v1/automations/rule-1",
                stale);
            var problem = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
            Assert.Equal("automation_rule_version_conflict", (string?)problem["code"]);
            Assert.Equal("/api/v1/automations/rule-1", (string?)problem["instance"]);
            Assert.DoesNotContain("Exception", problem.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Unknown_or_generic_action_payload_is_rejected_without_mutation()
        {
            using var host = CreateHost("Owner");
            var request = Rule();
            ((JObject)request["actions"]![0]!)["payload"] = new JObject
            {
                ["script"] = "rm -rf /",
                ["command"] = "say secret"
            };

            using var response = await PostJson(
                host.Client,
                "api/v1/automations",
                request);
            var problem = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("invalid_automation_rule_request", (string?)problem["code"]);
            Assert.Empty(host.Store.ListRules());
        }

        [Fact]
        public async Task Validate_returns_stable_issues_without_writing_the_store()
        {
            using var host = CreateHost("Owner");
            var request = Rule();
            request["trigger"]!["type"] = "Cron";

            using var response = await PostJson(
                host.Client,
                "api/v1/automations/validate",
                request);
            var result = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False((bool?)result["isValid"] ?? true);
            Assert.Equal(
                "automation_trigger_field_not_allowed",
                (string?)result["issues"]?[0]?["code"]);
            Assert.Equal("condition.group", (string?)result["issues"]?[0]?["path"]);
            Assert.Empty(host.Store.ListRules());
            Assert.Equal(0, host.Store.ExecutionMutationCount);
        }

        [Fact]
        public async Task Dry_run_evaluates_and_plans_without_side_effects_or_sensitive_echoes()
        {
            using var host = CreateHost("Owner");
            var request = new JObject
            {
                ["rule"] = Rule(),
                ["snapshot"] = new JObject
                {
                    ["triggerId"] = "trigger-1",
                    ["trigger"] = new JObject { ["type"] = "PlayerJoined" },
                    ["occurredAtUtc"] = "2026-07-27T07:59:00Z",
                    ["actor"] = new JObject
                    {
                        ["crossplatformId"] = "EOS-sensitive-player-id",
                        ["entityId"] = 7,
                        ["group"] = "member",
                        ["permissionLevel"] = 10
                    },
                    ["gapIds"] = new JArray()
                }
            };

            using var response = await PostJson(
                host.Client,
                "api/v1/automations/dry-run",
                request);
            var result = JObject.Parse(await response.Content.ReadAsStringAsync());
            var planned = (JObject)Assert.Single(result["plannedActions"]!);
            var serialized = result.ToString();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True((bool?)result["validation"]?["isValid"]);
            Assert.Equal("Matched", (string?)result["evaluation"]?["truth"]);
            Assert.Equal("PrivateMessage", (string?)planned["actionType"]);
            Assert.Equal("Ready", (string?)planned["dependency"]?["status"]);
            Assert.True((bool?)planned["target"]?["isResolved"]);
            Assert.True((bool?)planned["wouldExecute"]);
            Assert.DoesNotContain("Welcome", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("EOS-sensitive-player-id", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("resolvedId", serialized, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("chatText", serialized, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(host.Store.ListRules());
            Assert.Equal(0, host.Store.ExecutionMutationCount);
        }

        [Theory]
        [InlineData("api/v1/automations/executions")]
        [InlineData("api/v1/automations/executions/execution-1")]
        public async Task Execution_queries_report_the_missing_application_contract_honestly(
            string path)
        {
            using var host = CreateHost("Owner");

            using var response = await host.Client.GetAsync(
                path,
                TestContext.Current.CancellationToken);
            var problem = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
            Assert.Equal("automation_execution_query_unavailable", (string?)problem["code"]);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
            Assert.Equal(0, host.Store.EvidenceReadCount);
        }

        private static string Verb(MethodInfo method)
        {
            if (method.IsDefined(typeof(HttpGetAttribute))) return "GET";
            if (method.IsDefined(typeof(HttpPostAttribute))) return "POST";
            if (method.IsDefined(typeof(HttpPutAttribute))) return "PUT";
            if (method.IsDefined(typeof(HttpDeleteAttribute))) return "DELETE";
            return string.Empty;
        }

        private static void AssertRule(
            JObject rule,
            long expectedVersion,
            string expectedName)
        {
            Assert.Equal("rule-1", (string?)rule["id"]);
            Assert.Equal(expectedVersion, (long?)rule["version"]);
            Assert.Equal(expectedName, (string?)rule["name"]);
            Assert.Equal("PlayerJoined", (string?)rule["trigger"]?["type"]);
            Assert.Equal("Predicate", (string?)rule["condition"]?["kind"]);
            Assert.Equal(
                "PlayerGroup",
                (string?)rule["condition"]?["predicate"]?["operator"]);
            Assert.Equal("PrivateMessage", (string?)rule["actions"]?[0]?["type"]);
            Assert.Equal(
                "TriggerPlayer",
                (string?)rule["actions"]?[0]?["target"]?["kind"]);
            Assert.Equal(
                "Welcome",
                (string?)rule["actions"]?[0]?["privateMessage"]?["message"]);
            Assert.Null(rule.SelectToken("$..payload"));
            Assert.Null(rule.SelectToken("$..script"));
            Assert.Null(rule.SelectToken("$..command"));
        }

        private static JObject Rule(long expectedVersion = 0) =>
            new()
            {
                ["id"] = "rule-1",
                ["expectedVersion"] = expectedVersion,
                ["name"] = "Welcome",
                ["isEnabled"] = true,
                ["trigger"] = new JObject { ["type"] = "PlayerJoined" },
                ["condition"] = new JObject
                {
                    ["nodeId"] = "group",
                    ["kind"] = "Predicate",
                    ["predicate"] = new JObject
                    {
                        ["fieldKey"] = "actor.group",
                        ["operator"] = "PlayerGroup",
                        ["scalarValue"] = "member"
                    }
                },
                ["actions"] = new JArray
                {
                    new JObject
                    {
                        ["id"] = "message",
                        ["type"] = "PrivateMessage",
                        ["target"] = new JObject { ["kind"] = "TriggerPlayer" },
                        ["privateMessage"] = new JObject { ["message"] = "Welcome" }
                    }
                },
                ["cooldownSeconds"] = 60,
                ["cooldownScope"] = "RulePlayer",
                ["concurrencyPolicy"] = "QueueOne",
                ["failurePolicy"] = "Continue"
            };

        private static async Task<HttpResponseMessage> PostJson(
            HttpClient client,
            string path,
            JToken body) =>
            await client.PostAsync(
                path,
                Json(body),
                TestContext.Current.CancellationToken);

        private static async Task<HttpResponseMessage> PutJson(
            HttpClient client,
            string path,
            JToken body) =>
            await client.PutAsync(
                path,
                Json(body),
                TestContext.Current.CancellationToken);

        private static StringContent Json(JToken body) =>
            new(body.ToString(), Encoding.UTF8, "application/json");

        private static HttpTestHost CreateHost(string? role)
        {
            var store = new RecordingAutomationStore();
            var dependencies = new ReadyDependencyCatalog();
            var validator = new AutomationRuleValidator(
                new AutomationFieldCatalog(),
                dependencies);
            var rules = new AutomationRuleUseCases(store, validator, () => Now);
            var dryRun = new DryRunAutomationRuleUseCase(
                validator,
                new AutomationConditionEvaluator(TimeZoneInfo.Utc),
                dependencies,
                new SafeTargetResolver());
            var services = new ServiceCollection();
            services.AddSingleton(rules);
            services.AddSingleton(dryRun);
            var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
            var configuration = new HttpConfiguration
            {
                DependencyResolver = new MicrosoftDependencyResolver(provider)
            };
            configuration.MapHttpAttributeRoutes();
            configuration.Formatters.Remove(configuration.Formatters.XmlFormatter);
            configuration.Formatters.JsonFormatter.SerializerSettings.ContractResolver =
                new CamelCasePropertyNamesContractResolver();
            configuration.MessageHandlers.Add(new PrincipalHandler(role));
            configuration.EnsureInitialized();
            return new HttpTestHost(provider, configuration, store);
        }

        private sealed class HttpTestHost : IDisposable
        {
            private readonly ServiceProvider provider;
            private readonly HttpConfiguration configuration;

            public HttpTestHost(
                ServiceProvider provider,
                HttpConfiguration configuration,
                RecordingAutomationStore store)
            {
                this.provider = provider;
                this.configuration = configuration;
                Store = store;
                Client = new HttpClient(new HttpServer(configuration))
                {
                    BaseAddress = new Uri("http://localhost/")
                };
            }

            public HttpClient Client { get; }
            public RecordingAutomationStore Store { get; }

            public void Dispose()
            {
                Client.Dispose();
                configuration.Dispose();
                provider.Dispose();
            }
        }

        private sealed class PrincipalHandler : DelegatingHandler
        {
            private readonly string? role;

            public PrincipalHandler(string? role)
            {
                this.role = role;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var identity = role == null
                    ? new ClaimsIdentity()
                    : new ClaimsIdentity(
                        new[]
                        {
                            new Claim(ClaimTypes.NameIdentifier, "owner-1"),
                            new Claim(ClaimTypes.Role, role)
                        },
                        "Test");
                var principal = new ClaimsPrincipal(identity);
                var owin = new OwinContext();
                owin.Authentication.User = principal;
                request.SetOwinContext(owin);
                request.GetRequestContext().Principal = principal;
                return base.SendAsync(request, cancellationToken);
            }
        }

        private sealed class ReadyDependencyCatalog : IAutomationDependencyCatalog
        {
            public AutomationDependencyState Resolve(AutomationAction action) =>
                AutomationDependencyState.Ready;
        }

        private sealed class SafeTargetResolver : IAutomationTargetResolver
        {
            public AutomationTargetResolution Resolve(
                AutomationAction action,
                AutomationTriggerSnapshot snapshot)
            {
                if (action.TargetKind == AutomationTargetKind.Global.ToString())
                    return AutomationTargetResolution.Resolved("global");
                if (action.TargetKind == AutomationTargetKind.TriggerPlayer.ToString() &&
                    snapshot.ActorCrossplatformId != null)
                {
                    return AutomationTargetResolution.Resolved(snapshot.ActorCrossplatformId);
                }
                if (action.ReferenceId != null)
                    return AutomationTargetResolution.Resolved(action.ReferenceId);
                return AutomationTargetResolution.Unresolved("automation_target_not_found");
            }
        }

        public sealed class RecordingAutomationStore : IAutomationStore
        {
            private readonly Dictionary<string, AutomationRule> rules =
                new(StringComparer.Ordinal);

            public int ExecutionMutationCount { get; private set; }
            public int EvidenceReadCount { get; private set; }

            public IReadOnlyList<AutomationRule> ListRules() =>
                rules.Values.OrderBy(rule => rule.Id, StringComparer.Ordinal).ToArray();

            public AutomationRule? FindRule(string ruleId) =>
                rules.TryGetValue(ruleId, out var rule) ? rule : null;

            public void SaveRule(AutomationRule rule, long expectedVersion)
            {
                var currentVersion = FindRule(rule.Id)?.Version ?? 0;
                if (currentVersion != expectedVersion || rule.Version != expectedVersion + 1)
                    throw new AutomationVersionConflictException();
                rules[rule.Id] = rule;
            }

            public void DeleteRule(
                string ruleId,
                long expectedVersion,
                DateTimeOffset deletedAtUtc)
            {
                var current = FindRule(ruleId);
                if (current == null || current.Version != expectedVersion)
                    throw new AutomationVersionConflictException();
                rules.Remove(ruleId);
            }

            public void SaveTrigger(AutomationTriggerSnapshot trigger) =>
                ExecutionMutationCount++;

            public AutomationExecutionStartResult TryStartExecution(
                AutomationExecutionRecord execution)
            {
                ExecutionMutationCount++;
                return new AutomationExecutionStartResult(execution, true);
            }

            public void RecordConditionResult(AutomationConditionExecutionResult result) =>
                ExecutionMutationCount++;

            public void RecordActionResult(AutomationActionExecutionResult result) =>
                ExecutionMutationCount++;

            public IReadOnlyList<AutomationConditionExecutionResult> ListConditionResults(
                string executionId)
            {
                EvidenceReadCount++;
                return Array.Empty<AutomationConditionExecutionResult>();
            }

            public IReadOnlyList<AutomationActionExecutionResult> ListActionResults(
                string executionId)
            {
                EvidenceReadCount++;
                return Array.Empty<AutomationActionExecutionResult>();
            }
        }
    }
}
