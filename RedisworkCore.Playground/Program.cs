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
				HostAndPort = "10.11.25.113:6379"
			};

			using (SimpleContext context = new SimpleContext(options))
			{
				await context.BeginTransactionAsync();
				for (int i = 0; i < 300; i++)
				{
					Person person = new Person
					{
						Id = i,
						Name = "Emre",
						Lastname = null
					};
					context.Set<Person>().Add(person);
				}

				await context.SaveChangesAsync();
				await context.CommitTransactionAsync();
			}

			using (SimpleContext context = new SimpleContext(options))
			{
				var items = await context.Set<Person>().Where(x => x.Id > 10).Skip(0).TakeAsync(10);
				var count = await context.Set<Person>().CountAsync();
				var filteredCount = await context.Set<Person>().CountAsync(x => x.Id > 10);
			}

			#region USING WITH DOTNET IOC
			
			ServiceCollection services = new ServiceCollection();
			services.AddRedisContext<RedisContext, SimpleContext>(o => o.HostAndPort = "localhost:6379");
			IServiceProvider provider = services.BuildServiceProvider();
			
			using (RedisContext context = provider.GetService<RedisContext>())
			{
				Person person = new Person
				{
					Id = 26,
					Name = "Emre",
					Lastname = "Hızlı"
				};
				context.Set<Person>().Add(person);
				await context.SaveChangesAsync();
			}
			
			#endregion
		}
	}
}