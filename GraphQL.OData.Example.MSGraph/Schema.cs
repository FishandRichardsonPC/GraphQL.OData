namespace GraphQL.OData.Example.MSGraph
{
	public class Schema: GraphQL.Types.Schema
	{
		public Schema(Query query, Mutation mutation)
		{
			this.Query = query;
			this.Mutation = mutation;
		}
	}
}
