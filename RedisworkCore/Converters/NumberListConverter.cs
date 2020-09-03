using System;
using System.Linq;
using Newtonsoft.Json;

namespace RedisworkCore.Converters
{
	public class NumberListConverter : JsonConverter
	{
		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) { }

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			string strValue = reader.Value?.ToString() ?? string.Empty;
			string[] splitted = strValue.Split(Helpers.TagSeperator);
			return objectType.GenericTypeArguments[0].Name switch
			{
				"Byte"    => splitted.Select(x => Convert.ToByte(x)).ToList(),
				"Int64"   => splitted.Select(x => Convert.ToInt64(x)).ToList(),
				"Int32"   => splitted.Select(x => Convert.ToInt32(x)).ToList(),
				"Int16"   => splitted.Select(x => Convert.ToInt16(x)).ToList(),
				"UInt64"  => splitted.Select(x => Convert.ToUInt64(x)).ToList(),
				"UInt32"  => splitted.Select(x => Convert.ToUInt32(x)).ToList(),
				"UInt16"  => splitted.Select(x => Convert.ToInt16(x)).ToList(),
				"Decimal" => splitted.Select(Convert.ToDecimal).ToList(),
				"Double"  => splitted.Select(Convert.ToDouble).ToList(),
				"Boolean" => splitted.Select(Convert.ToBoolean).ToList(),
				_         => throw new InvalidCastException()
			};
		}

		public override bool CanConvert(Type objectType)
		{
			return objectType.IsGenericType && objectType.GenericTypeArguments[0].IsValueType;
		}
	}
}