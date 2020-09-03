# RedisworkCore

Redisworkcore is an ORM for redis like similar entityframework. And it using StackExchange.Redis for redis connections, NRediSearch for redis search engine and Polly for retry mechanism.

* Create a context like EntityFramework context.
* Run queries very similar LINQ.

### A simple usage example

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

### A simple usage with Microsoft.Extensions.DependencyInjection

You can use redisworkcore with microsoft ioc container like below.

    var services = new ServiceCollection();
    services.AddRedisContext<SimpleContext>(o => o.HostAndPort = "localhost:6379");
    var provider = services.BuildServiceProvider();

    using (var context = provider.GetService<SimpleContext>())
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
