using GraphQL.Resolvers;
using GraphQL.Types;

namespace GraphQL.OData.Example.MSGraph
{
	public sealed class Mutation : ObjectGraphType,IFieldResolver
	{
		public Mutation(ODataResolver oDataResolver, AadManager manager)
		{
			this.Name = "MsMutation";
			this.AddField(
				new FieldType
				{
					Name = "_1_0",
					ResolvedType = oDataResolver.GetMutationType(
						"MS_1_0",
						"https://graph.microsoft.com/v1.0",
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
					ResolvedType = oDataResolver.GetMutationType(
						"MS_BETA",
						"https://graph.microsoft.com/beta",
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
	}
}
