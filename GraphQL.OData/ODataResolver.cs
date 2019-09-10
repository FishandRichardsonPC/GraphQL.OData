using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using GraphQL.Language.AST;
using GraphQL.OData.GraphTypes;
using GraphQL.Resolvers;
using GraphQL.Types;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GraphQL.OData
{
	public class ODataResolver: IFieldResolver
	{
		private readonly ODataNavigationPropertyResolver _navigationResolver;
		private static ConcurrentDictionary<string, Dictionary<string, GraphType>> _queryTypes = new ConcurrentDictionary<string, Dictionary<string, GraphType>>();
		private static readonly HttpClient Client = new HttpClient();
		private static readonly Regex CollectionRegex = new Regex("^Collection\\((.*)\\)$");
		private readonly Dictionary<string, Dictionary<string, string>> _entitySets = new Dictionary<string, Dictionary<string, string>>();

		private static readonly Dictionary<string, GraphType> TypeMap = new Dictionary<string, GraphType>
		{
			{"Edm.String", new StringGraphType()},
			{"Edm.DateTime", new DateTimeGraphType()},
			{"Edm.DateTimeOffset", new DateTimeOffsetGraphType()},
			{"Edm.Boolean", new BooleanGraphType()},
			{"Edm.Guid", new StringGraphType()},
			{"Edm.Stream", new StringGraphType()},
			{"Edm.Binary", new StringGraphType()},
			{"Edm.Byte", new StringGraphType()},
			{"Edm.Int16", new IntGraphType()},
			{"Edm.Int32", new IntGraphType()},
			{"Edm.Int64", new IntGraphType()},
			{"Edm.Double", new FloatGraphType()},
			{"Edm.Single", new FloatGraphType()},
			{"Edm.Duration", new IsoTimeSpanGraphType()},
			{"Edm.TimeOfDay", new TimeOnlyGraphType()},
			{"Edm.Date", new DateOnlyGraphType()},
            {"Edm.Decimal", new FloatGraphType()}
		};

		private Dictionary<string, XmlDocument> _schemaCache = new Dictionary<string, XmlDocument>();
		internal readonly IFieldResolver FieldResolver;
		public readonly ODataFunctionResolver FunctionResolver;

		public ODataResolver(
			ODataNavigationPropertyResolver navigationResolver
		)
		{
			this._navigationResolver = navigationResolver;
			navigationResolver.ODataResolver = this;
			this.FieldResolver = new ODataFieldResolver(this);
			this.FunctionResolver = new ODataFunctionResolver(this);
		}

		public IGraphType GetQueryType(
			string prefix,
			string baseUrl,
			Action<ResolveFieldContext> preParse = null,
			Func<ResolveFieldContext, HttpRequestMessage, HttpRequestMessage> preRequest = null,
			Action<GraphType> augmentTypes = null
		)
		{
			this._entitySets[baseUrl] = new Dictionary<string, string>();
			var result = new ODataObjectGraphType
			{
				Prefix = prefix,
				BaseUrl = baseUrl,
				PreParse = preParse,
				PreRequest = preRequest,
				AugmentTypes = augmentTypes,
				Name = prefix
			};
			var schema = this.GetSchema(baseUrl);

			var graphTypes = this.GetQueryGraphTypes(prefix, baseUrl, preParse, preRequest, augmentTypes, schema);

			var entityContainers = schema?.SelectNodes("//*[local-name() = 'EntityContainer']")?.Cast<XmlElement>().ToList();

			if (entityContainers != null)
			{
				foreach (var entityContainer in entityContainers)
				{
					foreach (var child in entityContainer.ChildNodes.Cast<XmlElement>())
					{
						if (child.Name == "EntitySet")
						{
							var typeString = child.Attributes["EntityType"].Value.Split('.').Last();
							var graphType = graphTypes[typeString];
							if (graphType is UnionGraphType)
							{
								graphType = graphTypes[typeString + "_Base"];
							}

							result.AddField(
								new FieldType
								{
									Name = child.Attributes["Name"].Value,
									ResolvedType = new ListGraphType(graphTypes[typeString]),
									Resolver = this.FieldResolver,
									Arguments = new QueryArguments(
										((ODataObjectGraphType) graphType).Arguments
										?? new List<QueryArgument>())
								});
							var name = child.Attributes["Name"].Value;
							var typeName = graphTypes[typeString].Name;
							if (typeName.Contains(name.Substring(0, name.Length - 3)))
							{
								this._entitySets[baseUrl][graphTypes[typeString].Name] =
									child.Attributes["Name"].Value;
							}
						}
						else if (child.Name == "Singleton")
						{
							var typeString = child.Attributes["Type"].Value.Split('.').Last();
							result.AddField(
								new FieldType
								{
									Name = child.Attributes["Name"].Value,
									ResolvedType = graphTypes[typeString],
									Resolver = this.FieldResolver
								});
						}
					}
				}
			}

			return result;
		}

		public IGraphType GetMutationType(
			string prefix,
			string baseUrl,
			Action<ResolveFieldContext> preParse = null,
			Func<ResolveFieldContext, HttpRequestMessage, HttpRequestMessage> preRequest = null,
			Action<GraphType> augmentTypes = null
		)
		{
			var result = new ODataObjectGraphType
			{
				Prefix = prefix,
				BaseUrl = baseUrl,
				PreParse = preParse,
				PreRequest = preRequest,
				AugmentTypes = augmentTypes,
				Name = $"{prefix}Mutations"
			};
			result.AddField(new FieldType
			{
				Name = "Placeholder",
				ResolvedType = new VoidType()
			});
			return result;
		}

		private XmlDocument GetSchema(string baseUrl)
		{
			if(!this._schemaCache.ContainsKey(baseUrl)) {
				var response = ODataResolver.Client
					.GetAsync($"{baseUrl}/$metadata").Result;

				string data;
				if (response.IsSuccessStatusCode)
				{
					data = response.Content.ReadAsStringAsync().Result;
				}
				else
				{
					throw new Exception("Failed to read microsoft graph schema");
				}

				this._schemaCache[baseUrl] = new XmlDocument();
				this._schemaCache[baseUrl].LoadXml(data);
			}

			return this._schemaCache[baseUrl];
		}

		public static GraphType GetQueryGraphType(string baseUrl, string typeName)
		{
			if (!ODataResolver._queryTypes.ContainsKey(baseUrl))
			{
				throw new ArgumentException("Base Url Not Found, Try moving the creation of the ODataQuery object earlier in the startup of your application");
			}

			return ODataResolver._queryTypes[baseUrl][typeName];
		}

		private Dictionary<string, GraphType> GetQueryGraphTypes(
			string prefix,
			string baseUrl,
			Action<ResolveFieldContext> preParse,
			Func<ResolveFieldContext, HttpRequestMessage, HttpRequestMessage> preRequest,
			Action<GraphType> augmentTypes,
			XmlNode schema = null
		)
		{
			if (!ODataResolver._queryTypes.ContainsKey(baseUrl) || schema != null)
			{
				if (schema == null)
				{
					schema = this.GetSchema(baseUrl);
				}

				var functions = schema?.SelectNodes("//*[local-name() = 'Function']")?.Cast<XmlElement>().ToList();

				var enumTypes = schema?.SelectNodes("//*[local-name() = 'EnumType']")?
					.Cast<XmlElement>().ToList();
				var entityTypes = schema?.SelectNodes("//*[local-name() = 'EntityType']")?
					.Cast<XmlElement>().ToList();
				var complexTypes = schema?.SelectNodes("//*[local-name() = 'ComplexType']")?
					.Cast<XmlElement>().ToList();

				var annotations = schema?.SelectNodes("//*[local-name() = 'Annotations']")?.Cast<XmlElement>().ToList();

				var annotationTargets = annotations?
					.Aggregate(
						new Dictionary<string, Dictionary<string, List<XmlNode>>>(),
						(dict, value) =>
						{
							var target = value.GetAttribute("Target").Split('.').Last().Split('/');
							var type = target[0].ToLower();
							var prop = target.Length > 1 ? target[1] : "";
							if (!dict.ContainsKey(type))
							{
								dict[type] = new Dictionary<string, List<XmlNode>>();
							}
							if (!dict[type].ContainsKey(prop))
							{
								dict[type][prop] = new List<XmlNode>();
							}

							foreach (var child in value.ChildNodes)
							{
								dict[type][prop].Add((XmlNode)child);
							}

							return dict;
						}
					);

				var objectTypes = entityTypes.Concat(complexTypes).ToList();

				ODataResolver._queryTypes[baseUrl] = new Dictionary<string, GraphType>();

				if (enumTypes != null)
				{
					foreach (var enumEle in enumTypes)
					{
						var type = new EnumerationGraphType();
						var memberNodes = enumEle?.SelectNodes("*[local-name() = 'Member']")?.Cast<XmlElement>();
						if (memberNodes != null)
						{
							foreach (var memberNode in memberNodes)
							{
								type.AddValue(
									memberNode.Attributes["Name"].Value,
									"",
									memberNode.Attributes["Value"].Value
								);
							}
						}

						if (
							enumEle?.HasAttribute("IsFlags") == true &&
							enumEle.Attributes["IsFlags"].Value == "true")
						{
							// TODO
						}

						var nameString = enumEle?.Attributes["Name"].Value;
						type.Name = $"{prefix}_{nameString}";
						augmentTypes?.Invoke(type);

						ODataResolver._queryTypes[baseUrl][nameString] = type;
					}
				}

				foreach (var entityEle in objectTypes)
				{
					var nameString = entityEle.Attributes["Name"].Value;
					ODataResolver._queryTypes[baseUrl][nameString] = this.CreateType(prefix, baseUrl, preParse, preRequest, entityEle, augmentTypes);
				}

				this.CreateUnions(objectTypes, ODataResolver._queryTypes[baseUrl]);

				foreach (var entityEle in objectTypes)
				{
					var graphType = ODataResolver._queryTypes[baseUrl][entityEle.Attributes["Name"].Value];

					if (graphType is UnionGraphType)
					{
						graphType = ODataResolver._queryTypes[baseUrl][entityEle.Attributes["Name"].Value + "_Base"];
					}

					this.UpdateType(
						(IComplexGraphType) graphType,
						entityEle,
						ODataResolver._queryTypes[baseUrl]
					);
				}

				this.AddFunctions(
					prefix,
					ODataResolver._queryTypes[baseUrl],
					functions
				);

				this.InheritTypes(
					objectTypes,
					ODataResolver._queryTypes[baseUrl]
				);

				this.CleanupEmptyObjects(ODataResolver._queryTypes[baseUrl]);

				this.AddAnnotations(
					ODataResolver._queryTypes[baseUrl],
					annotationTargets
				);
			}

			return ODataResolver._queryTypes[baseUrl];
		}

		private void AddFunctions(
			string prefix,
			IReadOnlyDictionary<string, GraphType> queryTypes,
			List<XmlElement> functions
		)
		{
			var fnMap = functions.Aggregate(
				new Dictionary<string, List<FieldType>>(),
				(dict, item) =>
				{
					if (item.GetAttribute("IsBound") == "true")
					{
						var parameters = item.SelectNodes("*[local-name() = 'Parameter']")
							.Cast<XmlElement>().ToDictionary(
								(v) => v.GetAttribute("Name"),
								(v) =>
								{
									var type = this.GetType(v.GetAttribute("Type"), queryTypes);
									if (
										v.HasAttribute("Nullable")
										&& v.GetAttribute("Nullable") == "false"
									)
									{
										type = new NonNullGraphType(type);
									}
									return type;
								});
						var returnType = item.SelectNodes("*[local-name() = 'ReturnType']")
							.Cast<XmlElement>().First().GetAttribute("Type");
						var bindingParam = parameters.FirstOrDefault((v) => v.Key.ToLower() == "bindingparameter" || v.Key.ToLower() == "bindparameter");
						if (bindingParam.Key != null)
						{
							var type = bindingParam.Value;
							var typeName = type.Name;

							if (type is NonNullGraphType nonNullType)
							{
								// This binding param is not allowed to be null in graph anyway AFAIK
								type = (GraphType)nonNullType.ResolvedType;
							}

							if (type is ListGraphType listType)
							{
								typeName = $"List<{listType.ResolvedType.Name}>";
							}

							if (!dict.ContainsKey(typeName))
							{
								dict[typeName] = new List<FieldType>();
							}

							dict[typeName].Add(
								new FieldType
								{
									Name = item.GetAttribute("Name"),
									Arguments = new QueryArguments(
										parameters
											.Where((v) => v.Key != bindingParam.Key)
											.Select(
												(v) => new QueryArgument(
													v.Value
												) {Name = v.Key}
											)
									),
									ResolvedType = this.GetType(returnType, queryTypes),
									Resolver = this.FunctionResolver
								});
						}
					}

					return dict;
				}
			);

			foreach (var fnMapItem in fnMap)
            {
                var type = fnMapItem.Key;
                var fns = fnMapItem.Value;
				var typeName = type.Replace(prefix + "_", "");
				if (queryTypes.ContainsKey(typeName) && queryTypes[typeName] is ODataObjectGraphType oDataObj)
				{
					foreach (var fn in fns)
					{
						if (oDataObj.Fields.FirstOrDefault((v) => v.Name == fn.Name) == null)
						{
							oDataObj.AddField(fn);
						}
					}
				}
			}
		}

		// ReSharper disable once FunctionComplexityOverflow
		private void AddAnnotations(
			Dictionary<string, GraphType> queryTypes,
			IDictionary<string, Dictionary<string, List<XmlNode>>> annotationTargets
		)
		{
			foreach (var queryType in queryTypes)
            {
                var key = queryType.Key;
                var value = queryType.Value;
				var name = key.Replace("_Base", "").ToLower();
				if (value is ODataObjectGraphType)
				{
					if (!annotationTargets.ContainsKey(name))
					{
						annotationTargets[name] = new Dictionary<string, List<XmlNode>>();
					}
					if (!annotationTargets[name].ContainsKey(""))
					{
						annotationTargets[name][""] = new List<XmlNode>();
					}
				}

				if (annotationTargets.ContainsKey(name))
				{
					var annotations = annotationTargets[name];

					foreach (var annotation in annotations)
                    {
                        var property = annotation.Key;
                        var annotationList = annotation.Value;
						var description = "";
						var longDescription = "";
						var attributes = new List<string>();
						var unhandledAttributes = new HashSet<string>();
						var paramList = new List<QueryArgument>();
						var countable = true;
						var top = true;
						bool? skip = null;
						bool? search = null;
						var filter = true;
						var orderBy = true;

						foreach (var xmlNode in annotationList)
						{
							var ele = (XmlElement) xmlNode;
							var term = ele.GetAttribute("Term");
							switch (term)
							{
								case "Org.OData.Core.V1.Description":
									description = ele.GetAttribute("String");
									break;
								case "Org.OData.Core.V1.LongDescription":
									longDescription = ele.GetAttribute("String");
									break;
								case "Org.OData.Capabilities.V1.ChangeTracking":
								case "Org.OData.Core.V1.ChangeTracking":
								{
									var data = this.GetPropertyValues(ele);
									if (data.ContainsKey("Supported"))
									{
										attributes.Add($"This entity set supports the odata.track-changes preference: {data["Supported"].GetAttribute("Bool")}");
									}
									if (data.ContainsKey("FilterableProperties"))
									{
										// TODO: Not in the metadata ATM
									}
									if (data.ContainsKey("ExpandableProperties"))
									{
										// TODO: Not in the metadata ATM
									}
									break;
								}
								case "Org.OData.Capabilities.V1.ExpandRestrictions":
								{
									if (property == "" && value is ODataObjectGraphType oDataValue)
									{
										var data = this.GetPropertyValues(ele);
										if (data.ContainsKey("Expandable"))
										{
											oDataValue.Expandable = data["Expandable"].GetAttribute("Bool").ToLower() == "true";
										}

										if (data.ContainsKey("NonExpandableProperties"))
										{
											// TODO: Not in the metadata ATM
										}

										if (data.ContainsKey("MaxLevels"))
										{
											// TODO: Not in the metadata ATM
										}
									}

									break;
								}
								case "Org.OData.Capabilities.V1.NavigationRestrictions":
								{
									var data = this.GetPropertyValues(ele);
									if (data.ContainsKey("Navigability"))
									{
										attributes.Add($"Navigability: {data["Navigability"].ChildNodes[0].InnerText.Replace("Org.OData.Capabilities.V1.NavigationType/", "")}");
									}
									if (data.ContainsKey("RestrictedProperties"))
									{
										// TODO: Not in the metadata ATM
									}
									break;
								}
								case "Org.OData.Capabilities.V1.SearchRestrictions":
								{
									var data = this.GetPropertyValues(ele);
									if (data.ContainsKey("Searchable"))
									{
										search = data["Searchable"].GetAttribute("Bool") == "true";
									}
									if (data.ContainsKey("UnsupportedExpressions"))
									{
										// TODO: Not in the metadata ATM
									}
									break;
								}
								case "Org.OData.Capabilities.V1.SelectRestrictions":
								{
									if (property == "" && value is ODataObjectGraphType oDataValue)
									{
										var data = this.GetPropertyValues(ele);
										if (data.ContainsKey("Selectable"))
										{
											oDataValue.Selectable = data["Selectable"].GetAttribute("Bool").ToLower() == "true";
										}
									}
									break;
								}
								case "Org.OData.Capabilities.V1.CountRestrictions":
								{
									var data = this.GetPropertyValues(ele);
									if (data.ContainsKey("Countable"))
									{
										countable = data["Countable"].GetAttribute("Bool") == "true";
									}
									if (data.ContainsKey("NonCountableProperties"))
									{
										// TODO
									}
									if (data.ContainsKey("NonCountableNavigationProperties"))
									{
										// TODO
									}
									break;
								}
								case "Org.OData.Capabilities.V1.FilterRestrictions":
								{
									var data = this.GetPropertyValues(ele);
									if (data.ContainsKey("Filterable"))
									{
										filter = data["Filterable"].GetAttribute("Bool") == "true";
									}
									if (data.ContainsKey("RequiresFilter"))
									{
										// TODO: Not in the metadata ATM
									}
									if (data.ContainsKey("RequiredProperties"))
									{
										// TODO: Not in the metadata ATM
									}
									if (data.ContainsKey("NonFilterableProperties"))
									{
										// TODO: Not in the metadata ATM
									}
									if (data.ContainsKey("FilterExpressionRestrictions"))
									{
										// TODO: Not in the metadata ATM
									}
									if (data.ContainsKey("MaxLevels"))
									{
										// TODO: Not in the metadata ATM
									}
									break;
								}
								case "Org.OData.Capabilities.V1.SkipSupported":
								{
									skip = ele.GetAttribute("Bool") == "true";
									break;
								}
								case "Org.OData.Capabilities.V1.TopSupported":
								{
									top = ele.GetAttribute("Bool") == "true";
									break;
								}
								case "Org.OData.Capabilities.V1.InsertRestrictions":
								{
									var data = this.GetPropertyValues(ele);
									if (data.ContainsKey("Insertable"))
									{
										attributes.Add($"Entities can be inserted: {data["Insertable"].GetAttribute("Bool")}");
									}
									if (data.ContainsKey("NonInsertableNavigationProperties"))
									{
										// TODO
									}
									if (data.ContainsKey("MaxLevels"))
									{
										// TODO
									}
									if (data.ContainsKey("QueryOptions"))
									{
										// TODO
									}
									break;
								}
								case "Org.OData.Capabilities.V1.UpdateRestrictions":
								{
									var data = this.GetPropertyValues(ele);
									if (data.ContainsKey("Updatable"))
									{
										attributes.Add($"Entities can be updated: {data["Updatable"].GetAttribute("Bool")}");
									}
									if (data.ContainsKey("NonUpdatableNavigationProperties"))
									{
										// TODO
									}
									if (data.ContainsKey("MaxLevels"))
									{
										// TODO
									}
									if (data.ContainsKey("QueryOptions"))
									{
										// TODO
									}
									break;
								}
								case "Org.OData.Capabilities.V1.DeleteRestrictions":
								{
									var data = this.GetPropertyValues(ele);
									if (data.ContainsKey("Deletable"))
									{
										attributes.Add($"Entities can be deleted: {data["Deletable"].GetAttribute("Bool")}");
									}
									if (data.ContainsKey("NonDeletableNavigationProperties"))
									{
										// TODO
									}
									if (data.ContainsKey("MaxLevels"))
									{
										// TODO
									}
									break;
								}
								case "Org.OData.Capabilities.V1.SortRestrictions":
								{
									var data = this.GetPropertyValues(ele);
									if (data.ContainsKey("Sortable"))
									{
										orderBy = data["Sortable"].GetAttribute("Bool") == "true";
									}
									if (data.ContainsKey("AscendingOnlyProperties"))
									{
										// TODO: Not in the metadata ATM
									}
									if (data.ContainsKey("DescendingOnlyProperties"))
									{
										// TODO: Not in the metadata ATM
									}
									if (data.ContainsKey("NonSortableProperties"))
									{
										// TODO: Not in the metadata ATM
									}
									break;
								}
								case "Org.OData.Core.V1.Computed":
								{
									if (ele.GetAttribute("Bool") == "true")
									{
										attributes.Add("A value for this property is generated on both insert and update");
									}

									break;
								}
								case "Org.OData.Core.V1.Permissions":
								{
									// TODO var data = this.GetPropertyValues(ele);
									break;
								}
								default:
									attributes.Add($"{term}: {ele.OuterXml}");
									unhandledAttributes.Add(term);
									break;
							}
						}

						description = (
							description +
							(!string.IsNullOrEmpty(longDescription) ? "\n\n" + longDescription: "") +
							"\n\n" + String.Join("\n", attributes)
						);

						if (property == "")
						{
							if (countable)
							{
								paramList.Add(new QueryArgument(typeof(BooleanGraphType))
								{
									Name = "_count"
								});
							}

							if (top)
							{
								paramList.Add(new QueryArgument(typeof(IntGraphType))
								{
									Name = "_top",
									Description = "Return at most this many records"
								});
							}

							if (skip == true)
							{
								paramList.Add(new QueryArgument(typeof(IntGraphType))
								{
									Name = "_skip",
									Description = "Skip this many records"
								});
							}
							else if (skip == null)
							{
								paramList.Add(new QueryArgument(typeof(IntGraphType))
								{
									Name = "_skip",
									Description = "Skip this many records, may or may not be supported"
								});
							}

							if (search == true)
							{
								paramList.Add(new QueryArgument(typeof(StringGraphType))
								{
									Name = "_search",
									Description = "Search String"
								});
							}
							else if (search == null)
							{
								paramList.Add(new QueryArgument(typeof(StringGraphType))
								{
									Name = "_search",
									Description = "Search String, may or may not be supported"
								});
							}

							if (filter)
							{
								paramList.Add(new QueryArgument(typeof(StringGraphType))
								{
									Name = "_filter",
									Description = "Filter string see https://help.nintex.com/en-us/insight/OData/HE_CON_ODATAQueryCheatSheet.htm#Filter for examples"
								});
							}

							if (orderBy)
							{
								paramList.Add(new QueryArgument(typeof(StringGraphType))
								{
									Name = "_orderby",
									Description = "Fields to order by, comma separated. You may also add ' asc' or ' desc' to set the sort direction"
								});
							}

							value.Description = description;
							if (value is ODataObjectGraphType oDataValue)
							{
								oDataValue.Arguments = paramList;
							}
						}
						else if(value is IObjectGraphType objType)
						{
							var prop = objType.Fields.FirstOrDefault((v) => v.Name == property);
							if (prop != null)
							{
								prop.Description = description;
								foreach (var param in paramList)
								{
									prop.Arguments.Add(param);
								}
							}
						}
					}
				}
			}
		}

		private Dictionary<string, XmlElement> GetPropertyValues(XmlElement ele)
		{
			var result = new Dictionary<string, XmlElement>();
			if (ele.ChildNodes.Count > 0)
			{
				var dataItems = ele.ChildNodes[0].ChildNodes;
				foreach (var dataItem in dataItems)
				{
					if (dataItem is XmlElement dataElem)
					{
						result[dataElem.GetAttribute("Property")] = dataElem;
					}
				}
			}

			return result;
		}

		private void CleanupEmptyObjects(IDictionary<string,GraphType> graphTypes)
		{
			var snapshot = graphTypes.ToList();
			foreach (var graphType in snapshot)
			{
				if (graphType.Value is IObjectGraphType objType)
				{
					if (!objType.Fields.Any())
					{
						objType.AddField(
							new FieldType
							{
								Name = "Placeholder",
								ResolvedType = new VoidType(),
								Resolver = this.FieldResolver
							}
						);

						if (graphType.Key.EndsWith("_Base"))
						{
							graphTypes.Remove(graphType.Key);
							var unionType = (UnionGraphType) graphTypes[graphType.Key.Substring(
								0,
								graphType.Key.Length - 5)];
							var oldTypes = new List<IObjectGraphType>(unionType.PossibleTypes);
							oldTypes.Remove((IObjectGraphType) graphType.Value);
							unionType.PossibleTypes = oldTypes;
						}
					}
				}
			}
		}

		private void CreateUnions(List<XmlElement> objectTypes, Dictionary<string,GraphType> graphTypes)
		{
			var unions = this.GetInheritance(objectTypes, graphTypes).Aggregate(
				new Dictionary<string, List<string>>(),
				(acc, kvp) =>
				{
					if (!acc.ContainsKey(kvp.Value))
					{
						acc[kvp.Value] = new List<string>();
					}

					acc[kvp.Value].Add(kvp.Key);

					return acc;
				}
			);

			foreach (var kvp in unions)
			{
				var oldObj = graphTypes[kvp.Key];
				var union = new UnionGraphType {Name = oldObj.Name};
				union.PossibleTypes = kvp.Value.Select((v) => (IObjectGraphType) graphTypes[v]);

				oldObj.Name = oldObj.Name + "_Base";
				graphTypes[kvp.Key + "_Base"] = oldObj;
				union.AddPossibleType((IObjectGraphType)oldObj);
				graphTypes[kvp.Key] = union;
			}
		}

		private ObjectGraphType CreateType(
			string prefix,
			string baseUrl,
			Action<ResolveFieldContext> preParse,
			Func<ResolveFieldContext, HttpRequestMessage, HttpRequestMessage> preRequest,
			XmlNode entityEle,
			Action<GraphType> augmentTypes
		)
		{
			var graphType = new ODataObjectGraphType
			{
				Prefix = prefix,
				BaseUrl = baseUrl,
				PreParse = preParse,
				PreRequest = preRequest,
				AugmentTypes = augmentTypes
			};
			foreach (var childNode in entityEle.ChildNodes)
			{
				if (childNode is XmlElement childEle)
				{
					if (childEle.Name == "Property")
					{
						this.AddField(graphType, childEle);
					}
				}
			}

			graphType.AddField(
				new FieldType
				{
					Name = "_value",
					Description = "Raw Value",
					Type = typeof(StringGraphType),
					Resolver = this.FunctionResolver
				});

			graphType.Name = $"{prefix}_{entityEle.Attributes?["Name"].Value}";

			augmentTypes?.Invoke(graphType);

			return graphType;
		}

		private void UpdateType(
			IComplexGraphType graphType,
			XmlNode entityEle,
			IReadOnlyDictionary<string, GraphType> graphTypes
		)
		{
			foreach (var childNode in entityEle.ChildNodes)
			{
				if (childNode is XmlElement childEle)
				{
					if (childEle.Name == "Property" || childEle.Name == "NavigationProperty")
					{
						var nameString = childEle.Attributes["Name"].Value;
						if (graphType.Fields.FirstOrDefault((v) => v.Name == nameString) == default(FieldType))
						{
							this.AddField(graphType, childEle, graphTypes);
						}
					}
				}
			}
		}

		private void AddField(IComplexGraphType graphType, XmlNode ele, IReadOnlyDictionary<string, GraphType> graphTypes = null)
		{
			var typeString = ele.Attributes?["Type"].Value;
			var nameString = ele.Attributes?["Name"].Value;
			var propType = this.GetType(typeString, graphTypes);

			if (propType != null)
			{
				graphType.AddField(new FieldType
				{
					Name = nameString,
					ResolvedType = propType,
					Resolver = ele.Name == "NavigationProperty" ? this._navigationResolver : this.FieldResolver
				});
			}
		}

		private GraphType GetType(
			string typeString,
			IReadOnlyDictionary<string, GraphType> graphTypes
		)
		{
			var collectionMatch = ODataResolver.CollectionRegex.Match(typeString);
			if (collectionMatch.Success)
			{
				typeString = collectionMatch.Groups[1].Value;
			}

			GraphType propType = null;
			if (ODataResolver.TypeMap.ContainsKey(typeString))
			{
				propType = ODataResolver.TypeMap[typeString];
			} else if (graphTypes != null)
			{
				var noNsTypeString = typeString.Split('.').Last();
				if (graphTypes.ContainsKey(noNsTypeString))
				{
					propType = graphTypes[noNsTypeString];
				}
			}

			if (collectionMatch.Success && propType != null)
			{
				propType = new ListGraphType(propType);
			}

			return propType;
		}

		private void InheritTypes(
			IEnumerable<XmlElement> entities,
			IReadOnlyDictionary<string, GraphType> graphTypes
		)
		{
			var inheritance = this.GetInheritance(entities, graphTypes);

			while (inheritance.Any())
			{
				var snapshot = inheritance.ToDictionary((v) => v.Key, (v) => v.Value);
				var processing = new HashSet<string>(snapshot.Keys);
				foreach (var item in snapshot)
				{
                    var type = item.Key;
                    var baseType = item.Value;
					if (!processing.Contains(baseType))
					{
						var parentType = graphTypes[baseType];

						if (parentType is UnionGraphType)
						{
							parentType = graphTypes[baseType + "_Base"];
						}

						var parentGraphType = (ObjectGraphType) parentType;

						var graphType = graphTypes[type];

						if (graphType is UnionGraphType)
						{
							graphType = graphTypes[type + "_Base"];
						}

						var currentGraphType = (ObjectGraphType) graphType;
						var fieldNames = new HashSet<string>(currentGraphType.Fields.Select((v) => v.Name));

						foreach (var fieldType in parentGraphType.Fields)
						{
							if (!fieldNames.Contains(fieldType.Name))
							{
								currentGraphType.AddField(fieldType);
							}
						}

						inheritance.Remove(type);
						processing.Remove(type);
					}
				}
			}
		}

		private Dictionary<string, string> GetInheritance(IEnumerable<XmlElement> entities, IReadOnlyDictionary<string, GraphType> graphTypes)
		{
			var inheritance =  new Dictionary<string, string>();

			foreach (var entityEle in entities)
			{
				if (entityEle.HasAttribute("BaseType"))
				{
					var name = entityEle.Attributes["Name"].Value;
					var baseType = entityEle.Attributes["BaseType"].Value;
					if (!String.IsNullOrEmpty(baseType))
					{
						baseType = baseType.Split('.').Last();

						if (graphTypes.ContainsKey(baseType))
						{
							inheritance[name] = baseType;
						}
					}
				}
			}

			return inheritance;
		}

		public object Resolve(ResolveFieldContext context)
		{
			var results = new Dictionary<string, Task<object>>();
			var resolvedType = (ODataObjectGraphType) context.FieldDefinition.ResolvedType;

			resolvedType.PreParse?.Invoke(context);

			var baseUrl = resolvedType.BaseUrl;
			if (context.Source is ODataObjectGraphType oDataObj)
			{
				baseUrl = oDataObj.BaseUrl;
			}
			foreach (var subField in context.SubFields)
			{
				results[subField.Key] = this.GetField(
					resolvedType.Prefix,
					resolvedType.BaseUrl,
					resolvedType.PreParse,
					resolvedType.PreRequest,
					context,
					subField.Key,
					baseUrl,
					resolvedType.GetField(subField.Key),
					subField.Value.Arguments.ToDictionary(
						(v) => v.Name,
						(v) => v.Value.Value
					),
					resolvedType.AugmentTypes,
					resolvedType.Selectable ? subField.Value.SelectionSet : null
				);
			}

			var _ = Task.WhenAll(results.Values).Result;

			return new ODataObjectGraphType
			{
				Prefix = resolvedType.Prefix,
				BaseUrl = resolvedType.BaseUrl,
				PreParse = resolvedType.PreParse,
				PreRequest = resolvedType.PreRequest,
				AugmentTypes = resolvedType.AugmentTypes,
				Name = resolvedType.Prefix,
				Data = results.ToDictionary(
					(v) => v.Key,
					(v) => v.Value.Result
				)
			};
		}

		private ODataObjectGraphType CreateObject(
			string prefix,
			string baseUrl,
			Action<ResolveFieldContext> preParse,
			Func<ResolveFieldContext, HttpRequestMessage, HttpRequestMessage> preRequest,
			string json,
			FieldType field,
			Action<GraphType> augmentTypes,
			string url = null
		)
		{
			return this.CreateObject(
				prefix,
				baseUrl,
				preParse,
				preRequest,
				(JObject) JsonConvert.DeserializeObject(json),
				field,
				augmentTypes,
				url
			);
		}

		internal ODataObjectGraphType CreateObject(
			string prefix,
			string baseUrl,
			Action<ResolveFieldContext> preParse,
			Func<ResolveFieldContext, HttpRequestMessage, HttpRequestMessage> preRequest,
			JObject jObject,
			FieldType field,
			Action<GraphType> augmentTypes,
			string url = null
		)
		{
			return this.CreateObject(
				prefix,
				baseUrl,
				preParse,
				preRequest,
				jObject
					.Properties()
					.ToDictionary(
						(v) => v.Name,
						(v) => v.Value as object
					),
				field,
				augmentTypes,
				url
			);
		}

		internal ODataObjectGraphType CreateObject(
			string prefix,
			string baseUrl,
			Action<ResolveFieldContext> preParse,
			Func<ResolveFieldContext, HttpRequestMessage, HttpRequestMessage> preRequest,
			Dictionary<string, object> jsonData,
			FieldType field,
			Action<GraphType> augmentTypes,
			string url = null
		)
		{
			var name = field.ResolvedType.Name;
			if (field.ResolvedType is ListGraphType listType)
			{
				name = listType.ResolvedType.Name;
			}
			if (jsonData.ContainsKey("@odata.type"))
			{
				var types = this.GetQueryGraphTypes(prefix, baseUrl, preParse, preRequest, augmentTypes);
				var type = ((JValue) jsonData["@odata.type"]).Value.ToString().Split('.').Last();
				if (types.ContainsKey(type))
				{
					name = types[type].Name;
				}
			}

			if (this._entitySets[baseUrl].ContainsKey(name) && jsonData.ContainsKey("id"))
			{
				url = $"{baseUrl}/{this._entitySets[baseUrl][name]}/{jsonData["id"]}";
			}

			return new ODataObjectGraphType
			{
				Prefix = prefix,
				BaseUrl = baseUrl,
				PreParse = preParse,
				PreRequest = preRequest,
				AugmentTypes = augmentTypes,
				Name = name,
				Url = url,
				Data = jsonData
			};
		}

		internal async Task<object> GetField(
			string prefix,
			string rootBaseUrl,
			Action<ResolveFieldContext> preParse,
			Func<ResolveFieldContext, HttpRequestMessage, HttpRequestMessage> preRequest,
			ResolveFieldContext context,
			string key,
			string baseUrl,
			FieldType field,
			Dictionary<string, object> parameters,
			Action<GraphType> augmentTypes,
			SelectionSet fieldSelection = null,
			bool firstResultOnly = false
		)
		{
			if (key == "__typename")
			{
				return prefix;
			}

			if (key[0] == '_')
			{
				key = $"${key.Substring(1)}";
			}

			var url = $"{baseUrl}/{key}";

			if (parameters != null && parameters.Any())
			{
				url += "?" + String.Join(
					"&",
					parameters.Select(
						(v) =>
						{
							var result = v.Key;
							if (result[0] == '_')
							{
								result = "$" + result.Substring(1);
							}

							result += "=";

							if (v.Value is bool boolValue)
							{
								result += boolValue ? "true" : "false";
							}
							else
							{
								result += UrlEncoder.Default.Encode(v.Value.ToString());
							}

							return result;
						})
				);
			}

			if (fieldSelection != null)
			{
				var selection = string.Join(
					",",
					fieldSelection.Selections
						.Where((v) => v is Field)
						.Cast<Field>()
						.Select((v) => v.Name)
						.Concat(new [] { "id" })
						.Distinct()
				);
				url += (parameters != null && parameters.Any()) ? "&" : "?";
				url += $"$select={selection}";
			}

			var req = preRequest(context, new HttpRequestMessage(HttpMethod.Get, url));
			if (req != null)
			{
				var response = await ODataResolver.Client.SendAsync(req);

				string data;
				if (response.IsSuccessStatusCode)
				{
					var contentType = response.Content.Headers.ContentType.ToString();
					if (contentType.Contains("json"))
					{
						data = response.Content.ReadAsStringAsync().Result;
					}
					else
					{
						var binary = response.Content.ReadAsByteArrayAsync().Result;
						data = $"data:{contentType};base64,{Convert.ToBase64String(binary)}";
					}
				}
				else
				{
					var result = response.Content.ReadAsStringAsync().Result;
					var parsed = JsonConvert.DeserializeObject<JObject>(result);
					if (
						((IDictionary<string, JToken>) parsed).ContainsKey("error")
						&& parsed["error"] is JObject error
						&& ((IDictionary<string, JToken>) error).ContainsKey("code")
						&& error["code"].ToString() == "Authorization_RequestDenied"
					)
					{
						context.AddError(
							"Failed to fetch data from odata api",
							new UnauthorizedAccessException()
						);
						return null;
					}
					else
					{
						context.AddError(
							"Failed to fetch data from odata api",
							parsed
						);
						return null;
					}
				}

				if (field.ResolvedType is ListGraphType || firstResultOnly)
				{
					var jsonData = (JObject) JsonConvert.DeserializeObject(data);
					var result = ((JArray) jsonData["value"]).Select(
						(v) => (
							this.CreateObject(
								prefix,
								rootBaseUrl,
								preParse,
								preRequest,
								(JObject) v,
								field,
								augmentTypes,
								url
							)
						)
					);
					if (firstResultOnly)
					{
						return result.FirstOrDefault();
					}
					else
					{
						return result;
					}
				}
				else if (
					field.ResolvedType is ODataObjectGraphType ||
					(
						field.ResolvedType is UnionGraphType union &&
						union.PossibleTypes.First() is ODataObjectGraphType
					)
				)
				{
					return this.CreateObject(
						prefix,
						rootBaseUrl,
						preParse,
						preRequest,
						data,
						field,
						augmentTypes,
						url
					);
				}
				else if (field.ResolvedType is StringGraphType)
				{
					return data;
				}
				else
				{
					throw new ArgumentException(
						$"Could not resolve field of type {field.ResolvedType.GetType()}");
				}
			}

			return null;
		}

		internal IGraphType GetResolvedType(IGraphType resolvedType)
		{
			if (resolvedType is ListGraphType listType)
			{
				return listType.ResolvedType;
			}

			return resolvedType;
		}

		internal ODataObjectGraphType GetResolvedType(ResolveFieldContext context)
		{
			if (this.GetResolvedType(context.FieldDefinition.ResolvedType) is ODataObjectGraphType odataObj)
			{
				return odataObj;
			}
			else
			{
				return (ODataObjectGraphType) this.GetResolvedType(context.ParentType);
			}
		}
	}
}
