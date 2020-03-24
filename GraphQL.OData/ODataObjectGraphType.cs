using System;
using System.Collections.Generic;
using System.Net.Http;
using GraphQL.Types;

namespace GraphQL.OData
{
	public sealed class ODataObjectGraphType: ObjectGraphType
	{
		internal Dictionary<string, object> Data;
		internal Action<IResolveFieldContext> PreParse;
		public Action<GraphType> AugmentTypes;

		public ODataObjectGraphType()
		{
			base.IsTypeOf = (obj) => obj is ODataObjectGraphType oDataObj && oDataObj.Name == base.Name;
		}

		public override string CollectTypes(TypeCollectionContext context)
		{
			if (this.Name == null || this.BaseUrl == null)
			{
				throw new ArgumentException("Uninitialized ODataObjectGraphType, make sure you annotate the return types and call this.UpdateODataTypes(); in any object with a method that returns one of these");
			}
			return base.CollectTypes(context);
		}

		internal string BaseUrl { get; set; }
		internal string Url { get; set; }
		internal bool Expandable { get; set; } = true;
		internal List<QueryArgument> Arguments { get; set; }
		internal bool Selectable { get; set; } = true;
		internal string Prefix { get; set; }
		internal Func<IResolveFieldContext, HttpRequestMessage, HttpRequestMessage> PreRequest { get; set; }
	}
}
