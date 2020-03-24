using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Encodings.Web;
using GraphQL.Resolvers;
using GraphQL.Types;

namespace GraphQL.OData
{
	public class ODataFunctionResolver : IFieldResolver
	{
		private readonly ODataResolver _oDataResolver;

		internal ODataFunctionResolver(ODataResolver oDataResolver)
		{
			this._oDataResolver = oDataResolver;
		}

		public object Resolve(IResolveFieldContext context)
		{
			var resolvedType = this._oDataResolver.GetResolvedType(context);

			return this.GetValue(
				context,
				context.FieldName,
				context.Arguments,
				resolvedType.Prefix,
				resolvedType.BaseUrl,
				resolvedType.PreParse,
				resolvedType.PreRequest,
				resolvedType.AugmentTypes,
				false,
				((ODataObjectGraphType) context.Source)?.Url
			);
		}

		public object GetValue(
			IResolveFieldContext context,
			string function,
			IDictionary<string, object> parameters,
			string prefix,
			string baseUrl,
			Action<IResolveFieldContext> preParse,
			Func<IResolveFieldContext, HttpRequestMessage, HttpRequestMessage> preRequest,
			Action<GraphType> augmentTypes,
			bool firstResultOnly = false,
			string localUrl = null
		)
		{
			localUrl = localUrl ?? baseUrl;
			var fnParams = parameters?
				.Where((v) => v.Key[0] != '_')
				.Select((v) =>
				{
					var value = v.Value.ToString();
					if (v.Value is string)
					{
						value = $"'{value}'";
					}
					return $"{UrlEncoder.Default.Encode(v.Key)}={UrlEncoder.Default.Encode(value)}";
				});

			var args = fnParams == null ? "" : $"({String.Join(",", fnParams)})";

			return this._oDataResolver.GetField(
				prefix,
				baseUrl,
				preParse,
				preRequest,
				context,
				$"{function}{args}",
				localUrl,
				context.FieldDefinition,
				parameters?.Where((v) => v.Key[0] == '_').ToDictionary(
					(v) => v.Key,
					(v) => v.Value
				),
				augmentTypes,
				null,
				firstResultOnly
			).Result;
		}
	}
}
