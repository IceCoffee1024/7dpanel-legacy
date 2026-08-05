using System;
using NJsonSchema;
using NSwag;
using NSwag.Generation.Processors.Contexts;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.OpenApi
{
    internal static class PanelOpenApiJobsRules
    {
        public static void Apply(OperationProcessorContext context)
        {
            DescribeJobsBackupsSchedules(context);
            DescribeCommunityNoContentResponses(context);
        }

        private static void DescribeJobsBackupsSchedules(OperationProcessorContext context)
        {
            var path = context.OperationDescription.Path;
            var method = context.OperationDescription.Method;
            var operation = context.OperationDescription.Operation;

            if (path.StartsWith("/api/v1/jobs", StringComparison.Ordinal))
            {
                if (method == OpenApiOperationMethod.Get &&
                    string.Equals(path, "/api/v1/jobs", StringComparison.Ordinal))
                    operation.OperationId = "listJobs";
                else if (method == OpenApiOperationMethod.Get)
                    operation.OperationId = "getJob";
                else if (method == OpenApiOperationMethod.Post)
                {
                    operation.OperationId = "cancelJob";
                    PanelOpenApiOperationRuleHelpers.MoveSuccessResponse(operation, "200", "202", "The cancellation request was accepted.");
                }
                return;
            }

            if (path.StartsWith("/api/v1/backups", StringComparison.Ordinal))
            {
                if (string.Equals(path, "/api/v1/backups", StringComparison.Ordinal) &&
                    method == OpenApiOperationMethod.Get)
                {
                    operation.OperationId = "listBackups";
                }
                else if (string.Equals(path, "/api/v1/backups/world", StringComparison.Ordinal))
                {
                    operation.OperationId = "createWorldBackup";
                    PanelOpenApiOperationRuleHelpers.MoveSuccessResponse(operation, "200", "202", "The backup job was accepted.");
                }
                else if (string.Equals(path, "/api/v1/backups/panel-database", StringComparison.Ordinal))
                {
                    operation.OperationId = "createPanelDatabaseBackup";
                    PanelOpenApiOperationRuleHelpers.MoveSuccessResponse(operation, "200", "202", "The backup job was accepted.");
                }
                else if (string.Equals(path, "/api/v1/backups/server-configuration", StringComparison.Ordinal))
                {
                    operation.OperationId = "createServerConfigurationBackup";
                    PanelOpenApiOperationRuleHelpers.MoveSuccessResponse(operation, "200", "202", "The backup job was accepted.");
                }
                else if (path.EndsWith("/download", StringComparison.Ordinal))
                {
                    operation.OperationId = "downloadBackup";
                    var response = new OpenApiResponse
                    {
                        Description = "The requested backup archive."
                    };
                    response.Content["application/zip"] =
                        new OpenApiMediaType
                        {
                            Schema = new JsonSchema
                            {
                                Type = JsonObjectType.String,
                                Format = "binary"
                            }
                        };
                    operation.Responses["200"] = response;
                }
                else if (path.EndsWith("/restore", StringComparison.Ordinal))
                {
                    operation.OperationId = "restoreBackup";
                    PanelOpenApiOperationRuleHelpers.MoveSuccessResponse(operation, "200", "202", "The restore was staged.");
                }
                else if (method == OpenApiOperationMethod.Delete)
                {
                    operation.OperationId = "deleteBackup";
                    PanelOpenApiOperationRuleHelpers.MoveSuccessResponse(operation, "200", "204", "The backup was deleted.");
                }
                return;
            }

            if (string.Equals(path, "/api/v1/announcements", StringComparison.Ordinal) &&
                method == OpenApiOperationMethod.Post)
            {
                operation.OperationId = "sendAnnouncement";
                PanelOpenApiOperationRuleHelpers.RemoveParameter(operation, "cancellationToken");
                PanelOpenApiOperationRuleHelpers.MoveSuccessResponse(operation, "200", "202", "The announcement was accepted.");
                return;
            }

            if (!path.StartsWith("/api/v1/schedules", StringComparison.Ordinal)) return;
            if (string.Equals(path, "/api/v1/schedules", StringComparison.Ordinal))
            {
                if (method == OpenApiOperationMethod.Get)
                    operation.OperationId = "listSchedules";
                else if (method == OpenApiOperationMethod.Post)
                {
                    operation.OperationId = "createSchedule";
                    PanelOpenApiOperationRuleHelpers.MoveSuccessResponse(operation, "200", "201", "The schedule was created.");
                }
                return;
            }

            if (path.EndsWith("/enable", StringComparison.Ordinal))
                operation.OperationId = "enableSchedule";
            else if (path.EndsWith("/disable", StringComparison.Ordinal))
                operation.OperationId = "disableSchedule";
            else if (method == OpenApiOperationMethod.Get)
                operation.OperationId = "getSchedule";
            else if (method == OpenApiOperationMethod.Put)
                operation.OperationId = "updateSchedule";
            else if (method == OpenApiOperationMethod.Delete)
            {
                operation.OperationId = "deleteSchedule";
                PanelOpenApiOperationRuleHelpers.MoveSuccessResponse(operation, "200", "204", "The schedule was deleted.");
            }
        }

        private static void DescribeCommunityNoContentResponses(OperationProcessorContext context)
        {
            if (context.OperationDescription.Method != OpenApiOperationMethod.Delete)
                return;

            var path = context.OperationDescription.Path;
            if (!string.Equals(
                    path,
                    "/api/v1/community/homes/{crossplatformId}/{name}",
                    StringComparison.Ordinal) &&
                !string.Equals(
                    path,
                    "/api/v1/community/friendships/{firstCrossplatformId}/{secondCrossplatformId}",
                    StringComparison.Ordinal))
            {
                return;
            }

            var operation = context.OperationDescription.Operation;
            operation.Responses.Remove("200");
            operation.Responses["204"] = new OpenApiResponse
            {
                Description = "The resource was deleted."
            };
        }
    }
}
