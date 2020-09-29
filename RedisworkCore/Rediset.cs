using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using NRediSearch;
using RedisworkCore.Redisearch;

namespace RedisworkCore
{
	public abstract class Rediset
	{
		internal readonly List<Document> AddOrUpdateds = new List<Document>();
		internal readonly List<string> Deleteds = new List<string>();
		internal Client Client;
		internal abstract void BuildIndex();
		internal abstract Type EntityType { get; }
	}

	public class Rediset<T> : Rediset, IRedisearchQueryable<T>, IRedisearchTake<T>
		where T : class, new()
	{
		private readonly RedisContext _context;
		private readonly object _locker = new object();
		private readonly List<RedisearchSortDescriptor> _sorts = new List<RedisearchSortDescriptor>();
		private int _skip;
		private int _take = 1000000;
		private string _whereQuery = string.Empty;
		internal override Type EntityType { get; }

		public Rediset(RedisContext context)
		{
			_context = context;
			Client = new Client($"{typeof(T).FullName}_idx", _context.Database);
			_context.Trackeds.Add(this);
			EntityType = typeof(T);
		}

		public IRedisearchQueryable<T> Where(Expression<Func<T, bool>> expression)
		{
			_whereQuery = expression.Where(_whereQuery);
			return this;
		}

		public IRedisearchQueryable<T> SortBy(Expression<Func<T, object>> propSelector)
		{
			propSelector.SortBy(_sorts);
			return this;
		}

		public IRedisearchQueryable<T> SortByDescending(Expression<Func<T, object>> propSelector)
		{
			propSelector.SortByDescending(_sorts);
			return this;
		}

		public IRedisearchQueryable<T> SortBy(string propertyName)
		{
			RedisearchQueryExecuter.SortBy<T>(propertyName, _sorts);
			return this;
		}

		public IRedisearchQueryable<T> SortByDescending(string propertyName)
		{
			RedisearchQueryExecuter.SortByDescending<T>(propertyName, _sorts);
			return this;
		}

		public bool Any(Expression<Func<T, bool>> expression)
		{
			string query = expression.Where();
			return Client.Any<T>(query);
		}

		public Task<long> CountAsync(Expression<Func<T, bool>> expression)
		{
			string query = expression.Where();
			return Client.CountAsync<T>(query);
		}

		public Task<long> CountAsync()
		{
			return Client.CountAsync();
		}

		public IRedisearchTake<T> Skip(int count)
		{
			_skip = count;
			return this;
		}

		public Task<List<T>> ToListAsync()
		{
			return Client.ToListAsync<T>(_whereQuery, _sorts, _skip, _take);
		}

		public Task<List<T>> TakeAsync(int count)
		{
			if (count > 1000000)
				throw new NotSupportedException("Take limit exceeds maximum of 1.000.000");
			_take = count;
			return ToListAsync();
		}

		public void Add(T model)
		{
			Document doc = CreateDocument(model);
			lock (_locker)
			{
				AddOrUpdateds.Add(doc);
			}
		}

		public void Add(params T[] models)
		{
			Document[] docs = models.Select(CreateDocument).ToArray();
			lock (_locker)
			{
				AddOrUpdateds.AddRange(docs);
			}
		}

		public void Delete(T model)
		{
			Document doc = CreateDocument(model);
			lock (_locker)
			{
				Deleteds.Add(doc.Id);
			}
		}

		public void Delete(params T[] models)
		{
			string[] docIds = models.Select(CreateDocument)
									.Select(x => x.Id)
									.ToArray();
			lock (_locker)
			{
				Deleteds.AddRange(docIds);
			}
		}

		public void Update(params T[] models)
		{
			Document[] docs = models.Select(CreateDocument)
									.ToArray();
			lock (_locker)
			{
				AddOrUpdateds.AddRange(docs);
			}
		}

		public Task<T> Find(params object[] keys)
		{
			return Client.Find<T>(keys);
		}

		public bool All(Expression<Func<T, bool>> expression)
		{
			string query = expression.Where(true);
			return Client.All<T>(query);
		}

		internal override void BuildIndex()
		{
			Client.CreateIndex<T>();
		}

		public void RebuildIndex()
		{
			bool indexExist = _context.Database.KeyExists($"idx:{Client.IndexName}");
			if (indexExist) Client.DropIndex();
			BuildIndex();
		}

		private static Document CreateDocument(T model)
		{
			string key = RedisContext.FindKey(model);

			PropertyInfo keyProp = Helpers.GetRedisKeyValueAttributes<T>();
			if (keyProp != null)
				keyProp.SetValue(model, key);

			Document doc = model.CreateDocument(key);
			return doc;
		}
	}
}