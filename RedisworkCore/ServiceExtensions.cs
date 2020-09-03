using System;
using Microsoft.Extensions.DependencyInjection;

namespace RedisworkCore
{
	public static class ServiceExtensions
	{
		public static void AddRedisContext<T, T2>(this IServiceCollection services, Action<RedisContextOptions> options)
			where T : RedisContext
			where T2 : T
		{
			RedisContextOptions opt = new RedisContextOptions();
			options(opt);
			services.AddSingleton(opt);
			services.AddScoped<T, T2>();
		}
	}
}