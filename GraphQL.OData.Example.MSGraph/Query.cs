using System.Collections.Generic;
using GraphQL.Resolvers;
using GraphQL.Types;

namespace GraphQL.OData.Example.MSGraph
{
	public sealed class Query : ObjectGraphType, IFieldResolver
	{
		private readonly ODataResolver _oDataResolver;
		private readonly AadManager _manager;
		private const string Prefix10 = "MS_1_0";
		public const string Url10 = "https://graph.microsoft.com/v1.0";
		private const string PrefixBeta = "MS_BETA";
		public const string UrlBeta = "https://graph.microsoft.com/beta";

		public const string User = "User";

		public Query(ODataResolver oDataResolver, AadManager manager)
		{
			this._oDataResolver = oDataResolver;
			this._manager = manager;
			this.Name = "MsQuery";
			this.AddField(
				new FieldType
				{
					Name = "_1_0",
					ResolvedType = oDataResolver.GetQueryType(
						Query.Prefix10,
						Query.Url10,
						null,
						manager.PreRequest,
						manager.AddAuthentication
					),
					Resolver = oDataResolver
				}
			);

			this.AddField(
				new FieldType
				{
					Name = "_beta",
					ResolvedType = oDataResolver.GetQueryType(
						Query.PrefixBeta,
						Query.UrlBeta,
						null,
						manager.PreRequest,
						manager.AddAuthentication
					),
					Resolver = oDataResolver
				}
			);
		}

		public object Resolve(IResolveFieldContext context)
		{
			return this;
		}

		public object Get10Value(
			IResolveFieldContext context,
			string function,
			Dictionary<string, object> parameters,
			bool firstResultOnly = false
		)
		{
			return this._oDataResolver.FunctionResolver.GetValue(
				context,
				function,
				parameters,
				Query.Prefix10,
				Query.Url10,
				null,
				this._manager.PreRequest,
				this._manager.AddAuthentication,
				firstResultOnly
			);
		}

		public object GetBetaValue(
			IResolveFieldContext context,
			string function,
			Dictionary<string, object> parameters,
			bool firstResultOnly = false
		)
		{
			return this._oDataResolver.FunctionResolver.GetValue(
				context,
				function,
				parameters,
				Query.PrefixBeta,
				Query.UrlBeta,
				null,
				this._manager.PreRequest,
				this._manager.AddAuthentication,
				firstResultOnly
			);
		}
	}
}
