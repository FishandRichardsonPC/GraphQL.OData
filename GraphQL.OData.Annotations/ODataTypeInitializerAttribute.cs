using System;

namespace GraphQL.OData.Annotations
{
	[AttributeUsage(AttributeTargets.ReturnValue)]
	public class ODataTypeInitializerAttribute: Attribute
	{
		public string BaseUrl;
		public string TypeName;
	}
}
