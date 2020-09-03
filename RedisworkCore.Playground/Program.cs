using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RedisworkCore.DataAnnotations;

namespace RedisworkCore.Playground
{
	public class Person
	{
		[RedisKey(0)]
		public int Id { get; set; }
		public string Name { get; set; }
		public string Lastname { get; set; }
	}

	public class SimpleContext : RedisContext
	{
		public Rediset<Person> Persons { get; set; }
		public SimpleContext(RedisContextOptions options) : base(options) { }
	}

	class Program
	{
		static async Task Main(string[] args)
		{
			RedisContextOptions options = new RedisContextOptions
			{
				HostAndPort = "localhost:6379"
			};

			using (var context = new SimpleContext(options))
			{
				var person = new Person
				{
					Id = 26,
					Name = "Emre",
					Lastname = "Hızlı"
				};
				context.Persons.Add(person);
				await context.SaveChangesAsync();
			}

			using (var context = new SimpleContext(options))
			{
				Person person = await context.Persons.Find(26);
				List<Person> persons = await context.Persons.Where(x => x.Id > 0).ToListAsync();
			}
		}
	}
}