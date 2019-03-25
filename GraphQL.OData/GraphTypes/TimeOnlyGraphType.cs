using System;
using GraphQL.Types;

namespace GraphQL.OData.GraphTypes
{
	public class TimeOnlyGraphType: DateGraphType
	{
		public TimeOnlyGraphType()
		{
			this.Name = "TimeOnly";
			this.Description =
				"The `TimeOnly` scalar type represents a time without a date to be formatted " +
				"in accordance with the [ISO-8601](https://en.wikipedia.org/wiki/ISO_8601) standard.";
		}

		public override object Serialize(object value)
		{
			if (!(value is DateTime))
			{
				value = base.ParseValue(value);
			}

			var dateTime = (DateTime?)value;
			return dateTime?.ToString("HH:mm:ss");
		}
	}
}
