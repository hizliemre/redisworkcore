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
		public List<Hobby> Hobbies { get; set; }
	}

	public class Hobby
	{
		public string Name { get; set; }
	}

	public class SimpleContext : RedisContext
	{
		public Rediset<Person> Person { get; set; }
		public SimpleContext(RedisContextOptions options) : base(options) { }
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
				context.BuildIndex();
			}

			using (SimpleContext context = new SimpleContext(options))
			{
				await context.BeginTransactionAsync();
				Person p1 = new Person
				{
					Id = 1,
					Name = "Emre",
					Lastname = "Hızlı",
					Hobbies = new List<Hobby>
					{
						new Hobby {Name = "İçki içmek"},
						new Hobby {Name = "Kul hakkı yemek"},
						new Hobby {Name = "Domuz eti yemek"},
						new Hobby {Name = "Zina yapmak"},
					}
				};
				context.Set<Person>().Add(p1);
				Person p2 = new Person
				{
					Id = 2,
					Name = "Yasir",
					Lastname = "Ersoy",
					Hobbies = new List<Hobby>
					{
						new Hobby {Name = "Namaz kılmak"},
						new Hobby {Name = "Hacca gitmek"},
						new Hobby {Name = "Oruç tutmak"},
						new Hobby {Name = "Zekat vermek"},
						new Hobby {Name = "Kelime-i şehadet getirmek"}
					}
				};
				context.Set<Person>().Add(p2);
				await context.SaveChangesAsync();
				await context.CommitTransactionAsync();
			}

			using (SimpleContext context = new SimpleContext(options))
			{
				var items = await context.Set<Person>().Where(x => x.Name == "Emre").ToListAsync();
			}
		}
	}
}