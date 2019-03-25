using System;
using System.Collections.Generic;
using System.Linq;
using GraphQL.Types;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace GraphQL.OData
{
	public static class ExtensionMethods
	{
        // ReSharper disable once InconsistentNaming
        public static IServiceCollection AddGraphQLOData(this IServiceCollection services)
        {
            services.AddSingleton<ODataNavigationPropertyResolver>();
            services.AddSingleton<ODataResolver>();

            return services;
        }

        internal static void AddError(
            this ResolveFieldContext context,
            string message,
            JObject data
        )
        {
            context.AddError(
                message,
                data.Properties().ToDictionary(
                    (v) => v.Name,
                    (v) => v.Value.GetValue()
                )
            );
        }

        internal static void AddError(
            this ResolveFieldContext context,
            string message,
            Dictionary<string, object> data)
        {
            var err = new ExecutionError(
                message,
                data
            )
            {
                Path = context.Path
            };
            err.AddLocation(context.FieldAst, context.Document);
            context.Errors.Add(err);
        }

        internal static void AddError(
            this ResolveFieldContext context,
            string message,
            Exception ex
        )
        {
            var err = new ExecutionError(
                message,
                ex
            )
            {
                Path = context.Path
            };
            err.AddLocation(context.FieldAst, context.Document);
            context.Errors.Add(err);
        }
	}
}
