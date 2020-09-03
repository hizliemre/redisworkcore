using System;
using System.Linq;
using Newtonsoft.Json;

namespace RedisworkCore.Converters
{
	public class StringListConverter : JsonConverter
	{
		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) { }

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			string strValue = reader.Value?.ToString() ?? string.Empty;
			return strValue.Split(Helpers.TagSeperator).ToList();
		}

		public override bool CanConvert(Type objectType)
		{
			return objectType.IsGenericType && objectType.GenericTypeArguments[0] == typeof(string);
		}
	}
}