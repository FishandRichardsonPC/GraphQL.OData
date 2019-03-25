using GraphQL.Types;

namespace GraphQL.OData.GraphTypes
{
	public class VoidType : BooleanGraphType
	{
		public VoidType()
		{
			this.Name = "void";
			this.Description = "Returns nothing";
		}
	}
}