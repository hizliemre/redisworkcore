using System;
using Newtonsoft.Json;

namespace RedisworkCore.Converters
{
	public class BoolConverter : JsonConverter
	{
		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) { }

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			return reader.Value?.ToString() == "1";
		}

		public override bool CanConvert(Type objectType)
		{
			return objectType == typeof(bool);
		}
	}
}