using GraphQL.Resolvers;
using GraphQL.Types;

namespace GraphQL.OData
{
	public class ODataNavigationPropertyResolver : IFieldResolver
	{
		internal ODataResolver ODataResolver;

		public object Resolve(ResolveFieldContext context)
		{
			var resolvedType = this.ODataResolver.GetResolvedType(context);
			if (context.Source != null && context.Source is ODataObjectGraphType obj)
			{
				if (obj.Data != null && obj.Data.ContainsKey(context.FieldName))
				{
					return this.ODataResolver.FieldResolver.Resolve(context);
				}
			}

			return this.ODataResolver.GetField(
				resolvedType.Prefix,
				resolvedType.BaseUrl,
				resolvedType.PreParse,
				resolvedType.PreRequest,
				context,
				context.FieldName,
				((ODataObjectGraphType) context.Source)?.Url,
				context.FieldDefinition,
				context.Arguments,
				resolvedType.AugmentTypes
			).Result;
		}
	}
}