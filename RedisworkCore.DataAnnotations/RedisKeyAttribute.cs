using System;

namespace RedisworkCore.DataAnnotations
{
	public class RedisKeyAttribute : Attribute
	{
		public int Order { get; }

		public RedisKeyAttribute(int order)
		{
			Order = order;
		}
	}
}