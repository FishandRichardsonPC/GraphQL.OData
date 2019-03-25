using System.Linq;
using System.Reflection;
using GraphQL.Types;

namespace GraphQL.OData.Annotations
{
	public static class ExtensionMethods
	{
		public static void UpdateODataTypes(this IObjectGraphType type)
		{
			foreach (var fieldType in type.Fields
				.Where((v) => v.Type == typeof(ODataObjectGraphType) && v.ResolvedType == null))
			{
				var method = type.GetType().GetMethod(
					fieldType.Name,
					BindingFlags.Instance
					| BindingFlags.Public
					| BindingFlags.IgnoreCase
				);
				var attr = (ODataTypeInitializerAttribute)method?.ReturnTypeCustomAttributes.GetCustomAttributes(true).FirstOrDefault((v) => v is ODataTypeInitializerAttribute);
				if (attr != null)
				{
					fieldType.ResolvedType = ODataResolver.GetQueryGraphType(attr.BaseUrl, attr.TypeName);
				}
			}
		}
	}
}
