using System.Collections.Generic;
using Newtonsoft.Json;

namespace RedisworkCore.Converters
{
	internal class RedisearchSerializerSettings : JsonSerializerSettings
	{
		public RedisearchSerializerSettings()
		{
			Converters = new List<JsonConverter>
			{
				new BoolConverter(),
				new StringListConverter(),
				new NumberListConverter()
			};
		}

		internal static readonly RedisearchSerializerSettings SerializerSettings = new RedisearchSerializerSettings();
	}
}