using System;
using System.Collections.Generic;
using System.Linq;
using GraphQL.Resolvers;
using GraphQL.Types;
using Newtonsoft.Json.Linq;

namespace GraphQL.OData
{
	internal class ODataFieldResolver : IFieldResolver
	{
		private readonly ODataResolver _oDataResolver;

		internal ODataFieldResolver(ODataResolver resolver)
		{
			this._oDataResolver = resolver;
		}

		public object Resolve(IResolveFieldContext context)
		{
			ODataObjectGraphType odataType;
			Dictionary<string, object> data;
			if (context.Source != null && context.Source is ODataObjectGraphType obj)
			{
				data = obj.Data;
				odataType = obj;
			}
			else if (context.Source != null && context.Source is JObject jObj)
			{
				data = jObj.Properties()
					.ToDictionary(
						(v) => v.Name,
						(v) => v.Value as object
					);

				odataType = (ODataObjectGraphType)context.ParentType;
			}
			else
			{
				throw new ArgumentException("ODataFieldResolver can only resolve fields of ODataObjectGraphType");
			}

			if (data != null && data.ContainsKey(context.FieldName))
			{
				var value = data[context.FieldName];
				while (value is JValue jval)
				{
					value = jval.Value;
				}

				switch (value)
				{
					case ODataObjectGraphType oDataObj:
						return oDataObj;
					case JObject jObj:
						return this._oDataResolver.CreateObject(
							odataType.Prefix,
							odataType.BaseUrl,
							odataType.PreParse,
							odataType.PreRequest,
							jObj,
							context.FieldDefinition,
							odataType.AugmentTypes
						);
					case Dictionary<string, object> dict:
						return this._oDataResolver.CreateObject(
							odataType.Prefix,
							odataType.BaseUrl,
							odataType.PreParse,
							odataType.PreRequest,
							dict,
							context.FieldDefinition,
							odataType.AugmentTypes
						);
					default:
						return value;
				}
			}
			else
			{
				return null;
			}
		}
	}
}
