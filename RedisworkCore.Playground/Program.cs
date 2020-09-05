using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RedisworkCore.DataAnnotations;

namespace RedisworkCore.Playground
{
	public class Person
	{
		[RedisKey(0)] public int Id { get; set; }
		public string Name { get; set; }
		public string Lastname { get; set; }
	}

	public class SimpleContext : RedisContext
	{
		public SimpleContext(RedisContextOptions options) : base(options) { }
		public Rediset<Person> Persons { get; set; }
	}

	internal class Program
	{
		private static async Task Main(string[] args)
		{
			RedisContextOptions options = new RedisContextOptions
			{
				HostAndPort = "localhost:6379"
			};

			using (SimpleContext context = new SimpleContext(options))
			{
				await context.BeginTransactionAsync();
				Person person = new Person
				{
					Id = 26,
					Name = "Emre",
					Lastname = null
				};
				context.Set<Person>().Add(person);
				await context.SaveChangesAsync();
				await context.CommitTransactionAsync();
			}

			using (SimpleContext context = new SimpleContext(options))
			{
				List<Person> persons = await context.Persons.Where(x => "Emre" == x.Name).ToListAsync();
			}

			// #region USING WITH DOTNET IOC
			//
			// ServiceCollection services = new ServiceCollection();
			// services.AddRedisContext<RedisContext, SimpleContext>(o => o.HostAndPort = "localhost:6379");
			// IServiceProvider provider = services.BuildServiceProvider();
			//
			// using (RedisContext context = provider.GetService<RedisContext>())
			// {
			// 	Person person = new Person
			// 	{
			// 		Id = 26,
			// 		Name = "Emre",
			// 		Lastname = "Hızlı"
			// 	};
			// 	context.Set<Person>().Add(person);
			// 	await context.SaveChangesAsync();
			// }
			//
			// #endregion
		}
	}
}